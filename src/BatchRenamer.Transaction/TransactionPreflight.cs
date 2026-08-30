using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.6-B final read-only filesystem gate immediately before any future Phase 1 mutation.
/// It consumes only a frozen RenamePlan and fresh filesystem facts.
/// </summary>
public static class TransactionPreflight
{
    public static TransactionPreflightResult Validate(
        RenamePlan plan,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);

        var stopwatch = Stopwatch.StartNew();
        var issues = RenamePlanIntegrity.Validate(plan).ToList();
        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return new(false, issues, stopwatch.Elapsed);
        }

        var frozenSemantics = plan.DirectorySemantics.ToDictionary(
            x => RenamePlanIntegrity.NormalizeFullPath(x.DirectoryPath),
            StringComparer.Ordinal);
        var currentSemantics = new Dictionary<string, PathSemantics>(StringComparer.Ordinal);

        foreach (var pair in frozenSemantics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = semanticsProvider.GetSemantics(pair.Key);
            currentSemantics[pair.Key] = current;
            var frozen = pair.Value;

            if (current.IsCaseSensitive != frozen.IsCaseSensitive)
            {
                issues.Add(Error(
                    "PATH_SEMANTICS_CHANGED",
                    $"目录大小写语义已变化：{pair.Key}。请重新生成执行计划。",
                    path: pair.Key));
            }
            else if (frozen.IsReliable && !current.IsReliable)
            {
                issues.Add(Error(
                    "PATH_SEMANTICS_UNVERIFIABLE",
                    $"目录语义在生成计划时可可靠确认，但执行前已无法可靠确认：{pair.Key}。",
                    path: pair.Key));
            }
            else if (!current.IsReliable)
            {
                issues.Add(Warning(
                    "PATH_SEMANTICS_BEST_EFFORT",
                    $"目录语义无法完全确认，将按 Best-effort Recovery 能力处理：{pair.Key}。",
                    path: pair.Key));
            }
        }

        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return new(false, issues, stopwatch.Elapsed);
        }

        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in plan.Entries)
        {
            var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
            var frozen = frozenSemantics[directory];
            sourceKeys.Add(RenamePlanIntegrity.PathKey(entry.SourcePath, frozen));
        }

        var identityBestEffortCount = 0;
        foreach (var entry in plan.Entries.OrderBy(x => x.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
            var frozen = frozenSemantics[directory];
            var current = currentSemantics[directory];

            ValidateCurrentPathLimits(entry, current, issues);

            var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
            var sourceKind = fileSystem.GetEntryKind(entry.SourcePath);
            if (sourceKind == FileSystemEntryKind.Missing)
            {
                issues.Add(Error("SOURCE_MISSING", "执行前源文件或文件夹已不存在。", entry, entry.SourcePath));
            }
            else if (sourceKind == FileSystemEntryKind.Other)
            {
                issues.Add(Error("SOURCE_UNREADABLE", "执行前无法可靠访问源对象。", entry, entry.SourcePath));
            }
            else if (sourceKind != expectedKind)
            {
                issues.Add(Error("SOURCE_KIND_CHANGED", "执行前源对象类型已变化（文件/文件夹不一致）。", entry, entry.SourcePath));
            }
            else if (entry.ExpectedFileIdentity is { } expectedIdentity)
            {
                var actualIdentity = identityProvider.TryGetIdentity(entry.SourcePath, entry.IsDirectory);
                if (actualIdentity is null)
                {
                    issues.Add(Error("SOURCE_IDENTITY_UNVERIFIABLE", "执行前无法重新确认此前已冻结的 FileIdentity。", entry, entry.SourcePath));
                }
                else if (actualIdentity.Value != expectedIdentity)
                {
                    issues.Add(Error("SOURCE_IDENTITY_CHANGED", "执行前源路径已不再指向冻结计划中的同一对象。", entry, entry.SourcePath));
                }
            }
            else
            {
                identityBestEffortCount++;
            }

            var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
            if (tempKind == FileSystemEntryKind.Other)
                issues.Add(Error("TEMP_NAMESPACE_UNVERIFIABLE", "执行前无法可靠确认临时路径是否空闲。", entry, entry.TemporaryPath));
            else if (tempKind != FileSystemEntryKind.Missing)
                issues.Add(Error("TEMP_ALREADY_EXISTS", "执行前临时路径已被占用，拒绝执行。", entry, entry.TemporaryPath));

            var targetKind = fileSystem.GetEntryKind(entry.TargetPath);
            if (targetKind == FileSystemEntryKind.Other)
            {
                issues.Add(Error("TARGET_NAMESPACE_UNVERIFIABLE", "执行前无法可靠确认目标路径占用状态。", entry, entry.TargetPath));
            }
            else if (targetKind != FileSystemEntryKind.Missing)
            {
                var targetKey = RenamePlanIntegrity.PathKey(entry.TargetPath, frozen);
                if (!sourceKeys.Contains(targetKey))
                    issues.Add(Error("TARGET_EXISTS", "执行前目标路径被事务外对象占用。", entry, entry.TargetPath));
            }
        }

        if (identityBestEffortCount > 0)
        {
            issues.Add(Warning(
                "SOURCE_IDENTITY_BEST_EFFORT",
                $"{identityBestEffortCount} 个计划项没有可冻结的 FileIdentity；这些对象只能使用 Best-effort 身份校验。"));
        }

        stopwatch.Stop();
        return new(!issues.Any(x => x.Severity == ValidationSeverity.Error), issues, stopwatch.Elapsed);
    }

    private static void ValidateCurrentPathLimits(
        RenamePlanEntry entry,
        PathSemantics current,
        List<TransactionIssue> issues)
    {
        foreach (var path in new[] { entry.SourcePath, entry.TemporaryPath, entry.TargetPath })
        {
            if (current.MaxComponentLength is { } maxComponent
                && Path.GetFileName(path).Length > maxComponent)
            {
                issues.Add(Error(
                    "PATH_COMPONENT_LIMIT_CHANGED",
                    $"执行前目录的文件名长度上限为 {maxComponent}，冻结路径已不满足该限制。",
                    entry,
                    path));
            }

            if (current.MaxPathLength is { } maxPath && path.Length > maxPath)
            {
                issues.Add(Error(
                    "PATH_LENGTH_LIMIT_CHANGED",
                    $"执行前目录的路径长度上限为 {maxPath}，冻结路径已不满足该限制。",
                    entry,
                    path));
            }
        }
    }

    private static TransactionIssue Error(string code, string message, RenamePlanEntry? entry = null, string? path = null)
        => new(ValidationSeverity.Error, code, message, entry?.Ordinal, entry?.ItemId, path);

    private static TransactionIssue Warning(string code, string message, RenamePlanEntry? entry = null, string? path = null)
        => new(ValidationSeverity.Warning, code, message, entry?.Ordinal, entry?.ItemId, path);
}
