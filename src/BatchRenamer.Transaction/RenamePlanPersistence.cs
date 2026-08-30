using System.Security.Cryptography;
using System.Text.Json;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.6-A immutable RenamePlan persistence. This class may create transaction metadata files only;
/// it never renames, deletes, overwrites or otherwise mutates any Source/Temporary/Target entry.
/// </summary>
public static class RenamePlanPersistence
{
    public const string PlanFileName = "plan.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
    };

    public static RenamePlanPersistenceResult PersistNew(RenamePlan plan, string transactionsRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(transactionsRoot))
            throw new ArgumentException("Transaction root is required.", nameof(transactionsRoot));

        var integrity = RenamePlanIntegrity.Validate(plan);
        if (integrity.Any(x => x.Severity == ValidationSeverity.Error))
            return new(false, null, null, null, null, integrity);

        var root = Path.GetFullPath(transactionsRoot);
        var transactionDirectory = Path.Combine(root, plan.TransactionId.ToString("N"));
        var planPath = Path.Combine(transactionDirectory, PlanFileName);
        string? stagingPath = null;
        var createdTransactionDirectory = false;

        try
        {
            Directory.CreateDirectory(root);
            if (Directory.Exists(transactionDirectory))
            {
                return Failure(
                    transactionDirectory,
                    planPath,
                    "TRANSACTION_ALREADY_EXISTS",
                    "同一 TransactionId 的事务目录已经存在。为避免覆盖历史计划，已拒绝写入。");
            }

            Directory.CreateDirectory(transactionDirectory);
            createdTransactionDirectory = true;

            var bytes = JsonSerializer.SerializeToUtf8Bytes(plan, JsonOptions);
            var expectedHash = Convert.ToHexString(SHA256.HashData(bytes));
            stagingPath = Path.Combine(transactionDirectory, $".{PlanFileName}.tmp-{Guid.NewGuid():N}");

            using (var stream = new FileStream(
                       stagingPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            // Atomic metadata commit within the same directory. This File.Move is ONLY for the staging
            // plan.json file; it never receives SourcePath/TemporaryPath/TargetPath from RenamePlanEntry.
            File.Move(stagingPath, planPath, overwrite: false);
            stagingPath = null;

            var loaded = Load(planPath);
            if (!loaded.Success || loaded.Plan is null)
            {
                var issues = loaded.Issues.Concat([
                    new TransactionIssue(
                        ValidationSeverity.Error,
                        "PLAN_READBACK_FAILED",
                        "plan.json 已写入，但重新读取/结构校验失败；该计划不可进入执行阶段。")
                ]).ToArray();
                return new(false, transactionDirectory, planPath, loaded.Sha256, null, issues);
            }

            if (!string.Equals(expectedHash, loaded.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    transactionDirectory,
                    planPath,
                    "PLAN_HASH_MISMATCH",
                    "plan.json 写入后的 SHA256 与内存序列化结果不一致；该计划不可执行。",
                    loaded.Sha256);
            }

            return new(true, transactionDirectory, planPath, loaded.Sha256, loaded.Plan, loaded.Issues);
        }
        catch (Exception ex)
        {
            return Failure(
                transactionDirectory,
                planPath,
                "PLAN_PERSIST_FAILED",
                $"无法安全持久化 plan.json：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (stagingPath is not null)
            {
                try { File.Delete(stagingPath); } catch { /* best-effort cleanup of staging metadata only */ }
            }

            if (createdTransactionDirectory && !File.Exists(planPath))
            {
                try
                {
                    if (Directory.Exists(transactionDirectory) && !Directory.EnumerateFileSystemEntries(transactionDirectory).Any())
                        Directory.Delete(transactionDirectory);
                }
                catch
                {
                    // An empty failed transaction directory is harmless; never delete non-empty content here.
                }
            }
        }
    }

    public static RenamePlanLoadResult Load(string planPath)
    {
        if (string.IsNullOrWhiteSpace(planPath))
            throw new ArgumentException("Plan path is required.", nameof(planPath));

        var fullPath = Path.GetFullPath(planPath);
        try
        {
            if (!File.Exists(fullPath))
            {
                return new(false, fullPath, null, null, [
                    new TransactionIssue(ValidationSeverity.Error, "PLAN_FILE_MISSING", "plan.json 不存在。", Path: fullPath)
                ]);
            }

            var bytes = File.ReadAllBytes(fullPath);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var plan = JsonSerializer.Deserialize<RenamePlan>(bytes, JsonOptions);
            var issues = RenamePlanIntegrity.Validate(plan).ToList();
            if (plan is not null)
            {
                var parent = Path.GetDirectoryName(fullPath);
                var directoryName = string.IsNullOrWhiteSpace(parent) ? string.Empty : new DirectoryInfo(parent).Name;
                if (!Guid.TryParseExact(directoryName, "N", out var directoryTransactionId))
                {
                    issues.Add(new TransactionIssue(
                        ValidationSeverity.Error,
                        "PLAN_TRANSACTION_DIRECTORY_INVALID",
                        "plan.json 必须位于以 TransactionId(N) 命名的事务目录中。",
                        Path: fullPath));
                }
                else if (directoryTransactionId != plan.TransactionId)
                {
                    issues.Add(new TransactionIssue(
                        ValidationSeverity.Error,
                        "PLAN_TRANSACTION_DIRECTORY_MISMATCH",
                        "plan.json 内的 TransactionId 与事务目录名称不一致。",
                        Path: fullPath));
                }
            }

            var success = plan is not null && issues.All(x => x.Severity != ValidationSeverity.Error);
            return new(success, fullPath, hash, success ? plan : null, issues);
        }
        catch (JsonException ex)
        {
            return new(false, fullPath, null, null, [
                new TransactionIssue(ValidationSeverity.Error, "PLAN_JSON_INVALID", $"plan.json JSON 无效：{ex.Message}", Path: fullPath)
            ]);
        }
        catch (Exception ex)
        {
            return new(false, fullPath, null, null, [
                new TransactionIssue(ValidationSeverity.Error, "PLAN_READ_FAILED", $"无法读取 plan.json：{ex.GetType().Name}: {ex.Message}", Path: fullPath)
            ]);
        }
    }

    private static RenamePlanPersistenceResult Failure(
        string? transactionDirectory,
        string? planPath,
        string code,
        string message,
        string? hash = null)
        => new(false, transactionDirectory, planPath, hash, null, [
            new TransactionIssue(ValidationSeverity.Error, code, message, Path: planPath)
        ]);
}
