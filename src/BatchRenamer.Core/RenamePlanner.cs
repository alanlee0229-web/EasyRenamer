using System.Collections.ObjectModel;
using System.Diagnostics;

namespace BatchRenamer.Core;

/// <summary>
/// Final gate between preview/validation and TransactionEngine.
///
/// RenamePlanner deliberately accepts only concrete Source -> ProposedName facts. It never receives
/// RenameRuleSet, sequence state or WPF/UI state, so future Regex/Template/metadata generators can
/// evolve without changing transaction safety semantics.
///
/// BuildFinalPlan performs a fresh filesystem validation before freezing any plan. No filesystem
/// mutation occurs here.
/// </summary>
public static class RenamePlanner
{
    public const int CurrentSchemaVersion = 1;
    private const int MaxTemporaryNameAttemptsPerEntry = 32;

    public static RenamePlanBuildResult BuildFinalPlan(
        IReadOnlyList<ValidationInputItem> items,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        Guid? transactionId = null,
        DateTimeOffset? createdAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);

        var stopwatch = Stopwatch.StartNew();
        var plannerIssues = new List<RenamePlannerIssue>();
        var capturedSemantics = new SnapshottingPathSemanticsProvider(semanticsProvider);
        var capturedIdentities = new SnapshottingFileIdentityProvider(identityProvider);

        // The planner owns the final revalidation. A stale UI validation result is never trusted.
        // Semantics and FileIdentity reads are captured so the frozen plan uses the exact facts that
        // were checked by this validation pass, rather than silently re-reading a different object.
        var finalValidation = ValidationEngine.Validate(
            items,
            fileSystem,
            capturedSemantics,
            capturedIdentities,
            cancellationToken);

        if (finalValidation.ErrorItemCount > 0)
        {
            plannerIssues.Add(Error(
                "FINAL_VALIDATION_FAILED",
                $"执行前最终校验发现 {finalValidation.ErrorItemCount} 个错误，未生成执行计划。"));
            stopwatch.Stop();
            return Result(null, finalValidation, plannerIssues, stopwatch.Elapsed);
        }

        var validationById = finalValidation.Items.ToDictionary(x => x.ItemId);
        var changed = new List<ValidationInputItem>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsIncluded) continue;
            if (validationById.TryGetValue(item.Id, out var validation) && validation.HasError) continue;
            if (string.Equals(item.CurrentName, item.ProposedName, StringComparison.Ordinal)) continue;

            if (item.IsSynthetic)
            {
                plannerIssues.Add(Error(
                    "SYNTHETIC_ITEM_NOT_EXECUTABLE",
                    "演示/压力测试数据只能用于预览，不能进入真实执行计划。",
                    item.Id));
                continue;
            }

            // V1.0 extension protection is a planner-level invariant, not merely a UI convention.
            if (!item.IsDirectory && !HasLockedExtension(item))
            {
                plannerIssues.Add(Error(
                    "V1_EXTENSION_LOCK_VIOLATION",
                    $"V1.0 不允许修改扩展名：{item.CurrentName} → {item.ProposedName}。",
                    item.Id));
                continue;
            }

            changed.Add(item);
        }

        if (plannerIssues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return Result(null, finalValidation, plannerIssues, stopwatch.Elapsed);
        }

        if (changed.Count == 0)
        {
            plannerIssues.Add(new RenamePlannerIssue(
                ValidationSeverity.Info,
                "NO_CHANGES",
                "当前没有需要执行的名称变化。"));
            stopwatch.Stop();
            return Result(null, finalValidation, plannerIssues, stopwatch.Elapsed);
        }

        var id = transactionId ?? Guid.NewGuid();
        var timestamp = createdAt ?? DateTimeOffset.UtcNow;
        var directorySemantics = SnapshotDirectorySemantics(changed, capturedSemantics, cancellationToken);

        // Reserve all source and target namespaces so temporary names can never collide with either
        // the current batch or another temporary path generated earlier in this plan.
        var reservedPaths = BuildReservedPathSet(changed, directorySemantics);
        var entries = new List<RenamePlanEntry>(changed.Count);

        for (var index = 0; index < changed.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = changed[index];
            var tempPath = CreateUniqueTemporaryPath(
                item,
                id,
                index,
                fileSystem,
                directorySemantics,
                reservedPaths,
                cancellationToken);

            if (tempPath is null)
            {
                plannerIssues.Add(Error(
                    "TEMP_NAME_ALLOCATION_FAILED",
                    $"无法为“{item.CurrentName}”分配安全的临时名称，未生成执行计划。",
                    item.Id));
                stopwatch.Stop();
                return Result(null, finalValidation, plannerIssues, stopwatch.Elapsed);
            }

            var actualIdentity = capturedIdentities.TryGetIdentity(item.CurrentPath, item.IsDirectory);
            entries.Add(new RenamePlanEntry(
                Ordinal: index,
                ItemId: item.Id,
                SourcePath: NormalizeFullPath(item.CurrentPath),
                TemporaryPath: NormalizeFullPath(tempPath),
                TargetPath: NormalizeFullPath(Path.Combine(item.ParentDirectory, item.ProposedName)),
                IsDirectory: item.IsDirectory,
                ExpectedFileIdentity: actualIdentity));
        }

        var plan = new RenamePlan(
            TransactionId: id,
            CreatedAt: timestamp,
            SchemaVersion: CurrentSchemaVersion,
            DirectorySemantics: new ReadOnlyCollection<RenamePlanDirectorySemantics>(directorySemantics.Values
                .OrderBy(x => x.DirectoryPath, StringComparer.OrdinalIgnoreCase)
                .ToList()),
            Entries: new ReadOnlyCollection<RenamePlanEntry>(entries));

        stopwatch.Stop();
        return Result(plan, finalValidation, plannerIssues, stopwatch.Elapsed);
    }

    private static bool HasLockedExtension(ValidationInputItem item)
    {
        var proposedExtension = Path.GetExtension(item.ProposedName);
        return string.Equals(proposedExtension, item.Extension, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, RenamePlanDirectorySemantics> SnapshotDirectorySemantics(
        IEnumerable<ValidationInputItem> items,
        IPathSemanticsProvider provider,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, RenamePlanDirectorySemantics>(StringComparer.Ordinal);
        foreach (var directory in items.Select(x => NormalizeFullPath(x.ParentDirectory)).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semantics = provider.GetSemantics(directory);
            result[directory] = new RenamePlanDirectorySemantics(
                directory,
                semantics.IsCaseSensitive,
                semantics.IsReliable,
                semantics.MaxComponentLength,
                semantics.MaxPathLength,
                semantics.Source);
        }
        return result;
    }

    private static HashSet<string> BuildReservedPathSet(
        IEnumerable<ValidationInputItem> items,
        IReadOnlyDictionary<string, RenamePlanDirectorySemantics> semantics)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var directory = NormalizeFullPath(item.ParentDirectory);
            var snapshot = semantics[directory];
            reserved.Add(PathKey(item.CurrentPath, snapshot));
            reserved.Add(PathKey(Path.Combine(item.ParentDirectory, item.ProposedName), snapshot));
        }
        return reserved;
    }

    private static string? CreateUniqueTemporaryPath(
        ValidationInputItem item,
        Guid transactionId,
        int ordinal,
        IReadOnlyFileSystem fileSystem,
        IReadOnlyDictionary<string, RenamePlanDirectorySemantics> semantics,
        HashSet<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        var directory = NormalizeFullPath(item.ParentDirectory);
        var snapshot = semantics[directory];

        for (var attempt = 0; attempt < MaxTemporaryNameAttemptsPerEntry; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // GUID material keeps the temp namespace highly unpredictable; ordinal keeps diagnostics readable.
            var nonce = Guid.NewGuid().ToString("N")[..12];
            var tempName = $".~br-{transactionId:N}-{ordinal:D6}-{nonce}";
            if (snapshot.MaxComponentLength is { } max && tempName.Length > max)
            {
                // This is only expected on exotic filesystems with a very small component limit.
                var compactId = transactionId.ToString("N")[..12];
                tempName = $".~br-{compactId}-{ordinal:X}-{nonce[..6]}";
                if (tempName.Length > max) continue;
            }

            var candidate = Path.Combine(directory, tempName);
            var key = PathKey(candidate, snapshot);
            if (reservedPaths.Contains(key)) continue;
            if (fileSystem.GetEntryKind(candidate) != FileSystemEntryKind.Missing) continue;

            reservedPaths.Add(key);
            return candidate;
        }

        return null;
    }

    private static string PathKey(string path, RenamePlanDirectorySemantics semantics)
    {
        var normalized = NormalizeFullPath(path);
        return semantics.IsCaseSensitive ? normalized : normalized.ToUpperInvariant();
    }

    private static string NormalizeFullPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return full;
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static RenamePlanBuildResult Result(
        RenamePlan? plan,
        ValidationBatchResult validation,
        List<RenamePlannerIssue> plannerIssues,
        TimeSpan elapsed)
        => new(
            plan,
            validation,
            new ReadOnlyCollection<RenamePlannerIssue>(plannerIssues.ToList()),
            elapsed);

    private sealed class SnapshottingPathSemanticsProvider(IPathSemanticsProvider inner) : IPathSemanticsProvider
    {
        private readonly Dictionary<string, PathSemantics> _cache = new(StringComparer.Ordinal);

        public PathSemantics GetSemantics(string directoryPath)
        {
            var key = NormalizeFullPath(directoryPath);
            if (_cache.TryGetValue(key, out var value)) return value;
            value = inner.GetSemantics(directoryPath);
            _cache[key] = value;
            return value;
        }
    }

    private sealed class SnapshottingFileIdentityProvider(IFileIdentityProvider inner) : IFileIdentityProvider
    {
        private readonly Dictionary<(string Path, bool IsDirectory), FileIdentity?> _cache = new();

        public FileIdentity? TryGetIdentity(string path, bool isDirectory)
        {
            var key = (NormalizeFullPath(path), isDirectory);
            if (_cache.TryGetValue(key, out var value)) return value;
            value = inner.TryGetIdentity(path, isDirectory);
            _cache[key] = value;
            return value;
        }
    }

    private static RenamePlannerIssue Error(string code, string message, Guid? itemId = null)
        => new(ValidationSeverity.Error, code, message, itemId);
}
