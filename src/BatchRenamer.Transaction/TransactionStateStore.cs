using System.Text.Json;
using System.Text.Json.Serialization;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// Small advisory checkpoint. state.json is intentionally not authoritative; recovery always
/// re-checks plan.json, events.jsonl and the current filesystem before taking action.
/// </summary>
public static class TransactionStateStore
{
    public const int CurrentSchemaVersion = 1;
    public const string StateFileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static TransactionStateWriteResult Write(
        string transactionDirectory,
        TransactionStateCheckpoint checkpoint)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(checkpoint);

        var directory = Path.GetFullPath(transactionDirectory);
        var statePath = Path.Combine(directory, StateFileName);
        string? stagingPath = null;

        var validation = Validate(directory, checkpoint);
        if (validation.Any(x => x.Severity == ValidationSeverity.Error))
            return new(false, statePath, null, validation);

        try
        {
            if (!Directory.Exists(directory))
                return Failure(statePath, "STATE_TRANSACTION_DIRECTORY_MISSING", "事务目录不存在。", validation);

            var planLoad = RenamePlanPersistence.Load(Path.Combine(directory, RenamePlanPersistence.PlanFileName));
            if (!planLoad.Success || planLoad.Plan is null || planLoad.Plan.TransactionId != checkpoint.TransactionId)
            {
                var issues = validation.Concat(planLoad.Issues).Append(new TransactionIssue(
                    ValidationSeverity.Error,
                    "STATE_PLAN_UNAVAILABLE",
                    "无法验证冻结 plan.json，拒绝写入 state.json。",
                    Path: statePath)).ToArray();
                return new(false, statePath, null, issues);
            }

            if (checkpoint.LastCompletedOrdinal is { } lastOrdinal
                && !planLoad.Plan.Entries.Any(x => x.Ordinal == lastOrdinal))
            {
                return new(false, statePath, null, validation.Append(new TransactionIssue(
                    ValidationSeverity.Error,
                    "STATE_PLAN_ORDINAL_UNKNOWN",
                    "state.json LastCompletedOrdinal 不存在于冻结 RenamePlan。",
                    lastOrdinal,
                    Path: statePath)).ToArray());
            }

            stagingPath = Path.Combine(directory, $".{StateFileName}.tmp-{Guid.NewGuid():N}");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions);
            using (var stream = new FileStream(
                       stagingPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       16 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(stagingPath, statePath, overwrite: true);
            stagingPath = null;

            var loaded = Load(directory, checkpoint.TransactionId);
            if (!loaded.Success || loaded.Checkpoint is null)
            {
                var issues = validation.Concat(loaded.Issues).Append(new TransactionIssue(
                    ValidationSeverity.Error,
                    "STATE_READBACK_FAILED",
                    "state.json 写入后无法重新读取/验证。",
                    Path: statePath)).ToArray();
                return new(false, statePath, null, issues);
            }

            return new(true, statePath, loaded.Checkpoint, validation);
        }
        catch (Exception ex)
        {
            var issues = validation.Append(new TransactionIssue(
                ValidationSeverity.Error,
                "STATE_WRITE_FAILED",
                $"无法安全写入 state.json：{ex.GetType().Name}: {ex.Message}",
                Path: statePath)).ToArray();
            return new(false, statePath, null, issues);
        }
        finally
        {
            if (stagingPath is not null)
            {
                try { File.Delete(stagingPath); } catch { }
            }
        }
    }

    public static TransactionStateLoadResult Load(string transactionDirectory, Guid expectedTransactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));

        var directory = Path.GetFullPath(transactionDirectory);
        var statePath = Path.Combine(directory, StateFileName);
        if (!File.Exists(statePath))
        {
            return new(true, statePath, null, [
                new TransactionIssue(ValidationSeverity.Info, "STATE_NOT_CREATED", "事务尚未产生 state.json。", Path: statePath)
            ]);
        }

        try
        {
            var bytes = File.ReadAllBytes(statePath);
            var checkpoint = JsonSerializer.Deserialize<TransactionStateCheckpoint>(bytes, JsonOptions);
            if (checkpoint is null)
            {
                return new(false, statePath, null, [
                    new TransactionIssue(ValidationSeverity.Error, "STATE_JSON_NULL", "state.json 无法反序列化为检查点。", Path: statePath)
                ]);
            }

            var issues = Validate(directory, checkpoint).ToList();
            if (checkpoint.TransactionId != expectedTransactionId)
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Error,
                    "STATE_TRANSACTION_MISMATCH",
                    "state.json TransactionId 与冻结计划不一致。",
                    Path: statePath));
            }

            var success = issues.All(x => x.Severity != ValidationSeverity.Error);
            return new(success, statePath, success ? checkpoint : null, issues.ToArray());
        }
        catch (JsonException ex)
        {
            return new(false, statePath, null, [
                new TransactionIssue(ValidationSeverity.Error, "STATE_JSON_INVALID", $"state.json JSON 无效：{ex.Message}", Path: statePath)
            ]);
        }
        catch (Exception ex)
        {
            return new(false, statePath, null, [
                new TransactionIssue(ValidationSeverity.Error, "STATE_READ_FAILED", $"无法读取 state.json：{ex.GetType().Name}: {ex.Message}", Path: statePath)
            ]);
        }
    }

    public static TransactionStateCheckpoint Create(
        RenamePlan plan,
        TransactionCheckpointPhase phase,
        int? lastCompletedOrdinal = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(
            CurrentSchemaVersion,
            plan.TransactionId,
            DateTimeOffset.UtcNow,
            phase,
            lastCompletedOrdinal,
            note);
    }

    private static IReadOnlyList<TransactionIssue> Validate(string transactionDirectory, TransactionStateCheckpoint checkpoint)
    {
        var issues = new List<TransactionIssue>();
        if (checkpoint.SchemaVersion != CurrentSchemaVersion)
            issues.Add(new TransactionIssue(ValidationSeverity.Error, "STATE_SCHEMA_UNSUPPORTED", $"不支持 State SchemaVersion={checkpoint.SchemaVersion}。", Path: transactionDirectory));
        if (checkpoint.TransactionId == Guid.Empty)
            issues.Add(new TransactionIssue(ValidationSeverity.Error, "STATE_TRANSACTION_ID_EMPTY", "state.json TransactionId 不能为空。", Path: transactionDirectory));
        if (checkpoint.LastCompletedOrdinal is < 0)
            issues.Add(new TransactionIssue(ValidationSeverity.Error, "STATE_ORDINAL_INVALID", "state.json LastCompletedOrdinal 不能为负数。", Path: transactionDirectory));
        if (!Enum.IsDefined(checkpoint.Phase))
            issues.Add(new TransactionIssue(ValidationSeverity.Error, "STATE_PHASE_INVALID", "state.json Phase 不在当前 Schema 定义中。", Path: transactionDirectory));

        var directoryName = new DirectoryInfo(transactionDirectory).Name;
        if (!Guid.TryParseExact(directoryName, "N", out var directoryTransactionId)
            || directoryTransactionId != checkpoint.TransactionId)
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "STATE_TRANSACTION_DIRECTORY_MISMATCH",
                "state.json TransactionId 与事务目录名称不一致。",
                Path: transactionDirectory));
        }

        return issues;
    }

    private static TransactionStateWriteResult Failure(
        string statePath,
        string code,
        string message,
        IEnumerable<TransactionIssue> existing)
        => new(false, statePath, null, existing.Append(new TransactionIssue(
            ValidationSeverity.Error, code, message, Path: statePath)).ToArray());

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
