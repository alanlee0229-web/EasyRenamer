using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// Structural integrity checks for a frozen RenamePlan. These checks never reinterpret naming rules;
/// they only verify that the persisted Source/Temp/Target contract is internally coherent.
/// </summary>
public static class RenamePlanIntegrity
{
    public static IReadOnlyList<TransactionIssue> Validate(RenamePlan? plan)
    {
        var issues = new List<TransactionIssue>();
        if (plan is null)
        {
            issues.Add(Error("PLAN_NULL", "执行计划为空或无法反序列化。"));
            return issues;
        }

        if (plan.TransactionId == Guid.Empty)
            issues.Add(Error("PLAN_TRANSACTION_ID_INVALID", "执行计划缺少有效的 TransactionId。"));
        if (plan.SchemaVersion != RenamePlanner.CurrentSchemaVersion)
            issues.Add(Error("PLAN_SCHEMA_UNSUPPORTED", $"不支持的 RenamePlan Schema：{plan.SchemaVersion}。"));
        var entries = plan.Entries;
        var directorySemantics = plan.DirectorySemantics;
        if (entries is null || entries.Count == 0)
            issues.Add(Error("PLAN_EMPTY", "执行计划没有任何改名项。"));
        if (directorySemantics is null || directorySemantics.Count == 0)
            issues.Add(Error("PLAN_SEMANTICS_MISSING", "执行计划缺少目录 PathSemantics 快照。"));

        if (issues.Any(x => x.Severity == ValidationSeverity.Error)) return issues;

        // The guarded return above establishes these frozen collections as non-null for the rest
        // of integrity validation. Capture them once so Release nullable-flow analysis does not
        // re-dereference nullable record properties and emit CS8602.
        var safeEntries = entries!;
        var safeDirectorySemantics = directorySemantics!;

        var semanticsByDirectory = new Dictionary<string, RenamePlanDirectorySemantics>(StringComparer.Ordinal);
        foreach (var snapshot in safeDirectorySemantics)
        {
            if (string.IsNullOrWhiteSpace(snapshot.DirectoryPath))
            {
                issues.Add(Error("PLAN_SEMANTICS_PATH_INVALID", "执行计划包含空的目录语义路径。"));
                continue;
            }

            var directory = NormalizeFullPath(snapshot.DirectoryPath);
            if (!semanticsByDirectory.TryAdd(directory, snapshot))
                issues.Add(Error("PLAN_SEMANTICS_DUPLICATE", $"执行计划重复记录目录语义：{snapshot.DirectoryPath}。", path: snapshot.DirectoryPath));
        }

        var itemIds = new HashSet<Guid>();
        var ordinals = new HashSet<int>();
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var tempKeys = new HashSet<string>(StringComparer.Ordinal);
        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        var sourceOrTargetKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in safeEntries)
        {
            if (entry.Ordinal < 0 || !ordinals.Add(entry.Ordinal))
                issues.Add(Error("PLAN_ORDINAL_INVALID", $"执行计划包含无效或重复的 Ordinal：{entry.Ordinal}。", entry));
            if (entry.ItemId == Guid.Empty || !itemIds.Add(entry.ItemId))
                issues.Add(Error("PLAN_ITEM_ID_INVALID", "执行计划包含空或重复的 ItemId。", entry));

            if (string.IsNullOrWhiteSpace(entry.SourcePath)
                || string.IsNullOrWhiteSpace(entry.TemporaryPath)
                || string.IsNullOrWhiteSpace(entry.TargetPath))
            {
                issues.Add(Error("PLAN_PATH_EMPTY", "Source/Temporary/Target 路径不能为空。", entry));
                continue;
            }

            var source = NormalizeFullPath(entry.SourcePath);
            var temp = NormalizeFullPath(entry.TemporaryPath);
            var target = NormalizeFullPath(entry.TargetPath);
            var sourceDirectory = NormalizeFullPath(Path.GetDirectoryName(source) ?? string.Empty);
            var tempDirectory = NormalizeFullPath(Path.GetDirectoryName(temp) ?? string.Empty);
            var targetDirectory = NormalizeFullPath(Path.GetDirectoryName(target) ?? string.Empty);

            if (!string.Equals(sourceDirectory, tempDirectory, StringComparison.Ordinal)
                || !string.Equals(sourceDirectory, targetDirectory, StringComparison.Ordinal))
            {
                issues.Add(Error("PLAN_CROSS_DIRECTORY_MOVE", "V1 RenamePlan 不允许跨目录 Source/Temp/Target。", entry));
                continue;
            }

            if (!semanticsByDirectory.TryGetValue(sourceDirectory, out var semantics))
            {
                issues.Add(Error("PLAN_SEMANTICS_NOT_FOUND", "执行计划中的目录没有对应 PathSemantics 快照。", entry, sourceDirectory));
                continue;
            }

            if (string.Equals(source, target, StringComparison.Ordinal))
                issues.Add(Error("PLAN_NOOP_ENTRY", "执行计划包含 Source 与 Target 完全相同的无变化项。", entry));

            if (!Path.GetFileName(temp).StartsWith(".~br-", StringComparison.Ordinal))
                issues.Add(Error("PLAN_TEMP_NAMESPACE_INVALID", "TemporaryPath 不在 BatchRenamer 保留临时命名空间中。", entry, temp));

            var sourceKey = PathKey(source, semantics);
            var tempKey = PathKey(temp, semantics);
            var targetKey = PathKey(target, semantics);

            if (!sourceKeys.Add(sourceKey))
                issues.Add(Error("PLAN_DUPLICATE_SOURCE", "执行计划包含重复 Source namespace。", entry, source));
            if (!tempKeys.Add(tempKey))
                issues.Add(Error("PLAN_DUPLICATE_TEMP", "执行计划包含重复 Temporary namespace。", entry, temp));
            if (!targetKeys.Add(targetKey))
                issues.Add(Error("PLAN_DUPLICATE_TARGET", "执行计划包含重复 Target namespace。", entry, target));

            sourceOrTargetKeys.Add(sourceKey);
            sourceOrTargetKeys.Add(targetKey);
        }

        foreach (var tempKey in tempKeys)
        {
            if (sourceOrTargetKeys.Contains(tempKey))
                issues.Add(Error("PLAN_TEMP_COLLISION", "Temporary namespace 与 Source/Target namespace 冲突。"));
        }

        if (ordinals.Count == safeEntries.Count)
        {
            for (var expected = 0; expected < safeEntries.Count; expected++)
            {
                if (!ordinals.Contains(expected))
                {
                    issues.Add(Error("PLAN_ORDINAL_GAP", "执行计划 Ordinal 必须从 0 连续递增。"));
                    break;
                }
            }
        }

        return issues;
    }

    internal static string NormalizeFullPath(string path)
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

    internal static string PathKey(string path, RenamePlanDirectorySemantics semantics)
    {
        var normalized = NormalizeFullPath(path);
        return semantics.IsCaseSensitive ? normalized : normalized.ToUpperInvariant();
    }

    private static TransactionIssue Error(string code, string message, RenamePlanEntry? entry = null, string? path = null)
        => new(ValidationSeverity.Error, code, message, entry?.Ordinal, entry?.ItemId, path);
}
