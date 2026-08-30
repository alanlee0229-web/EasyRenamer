using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.6-D executes only Phase 2 of the frozen two-phase rename protocol:
/// TemporaryPath -> TargetPath.
///
/// It may start only when every plan entry is confirmed at TemporaryPath and every Source/Target
/// namespace is vacant. No overwrite/delete API exists. Once the first final mutation succeeds,
/// cancellation is not observed mid-batch; failures return a precise applied prefix for rollback.
/// </summary>
public static class TransactionPhase2Executor
{
    public static TransactionPhase2Result Execute(
        RenamePlan plan,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        IExactNamespaceInspector exactNamespaceInspector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(mutationFileSystem);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        var preflight = ValidatePhase2Preflight(
            plan,
            fileSystem,
            semanticsProvider,
            identityProvider,
            cancellationToken);

        if (!preflight.CanExecute)
        {
            stopwatch.Stop();
            return new(
                Phase2ExecutionState.NotStarted,
                preflight,
                Array.Empty<Phase2AppliedEntry>(),
                preflight.Issues,
                stopwatch.Elapsed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var issues = preflight.Issues.ToList();
        var applied = new List<Phase2AppliedEntry>(plan.Entries.Count);
        var semanticsByDirectory = plan.DirectorySemantics.ToDictionary(
            x => RenamePlanIntegrity.NormalizeFullPath(x.DirectoryPath),
            StringComparer.Ordinal);

        foreach (var entry in plan.Entries.OrderBy(x => x.Ordinal))
        {
            var sourceDirectory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
            var semantics = semanticsByDirectory[sourceDirectory];

            var justInTimeIssue = ValidateEntryImmediatelyBeforeMove(entry, fileSystem, identityProvider);
            if (justInTimeIssue is not null)
            {
                issues.Add(justInTimeIssue);
                stopwatch.Stop();
                return new(
                    applied.Count == 0 ? Phase2ExecutionState.NotStarted : Phase2ExecutionState.FailedPartial,
                    preflight,
                    applied.ToArray(),
                    issues,
                    stopwatch.Elapsed);
            }

            try
            {
                if (entry.IsDirectory)
                    mutationFileSystem.MoveDirectoryNoOverwrite(entry.TemporaryPath, entry.TargetPath);
                else
                    mutationFileSystem.MoveFileNoOverwrite(entry.TemporaryPath, entry.TargetPath);
            }
            catch (Exception ex)
            {
                var observation = ObserveAfterMoveException(entry, fileSystem, identityProvider);
                if (observation.ConfirmedApplied)
                {
                    applied.Add(Applied(entry));
                    issues.Add(Error(
                        "PHASE2_MOVE_EXCEPTION_AFTER_APPLY",
                        $"Temp → Target 已在磁盘上生效，但 move API 随后报告异常：{ex.GetType().Name}: {ex.Message}",
                        entry,
                        entry.TargetPath));
                    if (observation.IdentityIssue is not null) issues.Add(observation.IdentityIssue);
                    stopwatch.Stop();
                    return new(
                        Phase2ExecutionState.FailedPartial,
                        preflight,
                        applied.ToArray(),
                        issues,
                        stopwatch.Elapsed);
                }

                if (observation.ConfirmedNotApplied)
                {
                    issues.Add(Error(
                        "PHASE2_MOVE_FAILED",
                        $"Temp → Target 失败且磁盘状态确认未应用：{ex.GetType().Name}: {ex.Message}",
                        entry,
                        entry.TemporaryPath));
                    stopwatch.Stop();
                    return new(
                        applied.Count == 0 ? Phase2ExecutionState.NotStarted : Phase2ExecutionState.FailedPartial,
                        preflight,
                        applied.ToArray(),
                        issues,
                        stopwatch.Elapsed);
                }

                issues.Add(Error(
                    "PHASE2_MOVE_STATE_AMBIGUOUS",
                    $"Temp → Target 报告异常，且 Temp/Target 当前状态无法可靠判定；必须进入回滚/恢复流程：{ex.GetType().Name}: {ex.Message}",
                    entry,
                    entry.TemporaryPath));
                stopwatch.Stop();
                return new(
                    Phase2ExecutionState.FailedPartial,
                    preflight,
                    applied.ToArray(),
                    issues,
                    stopwatch.Elapsed);
            }

            applied.Add(Applied(entry));
            var postMoveIssue = ValidateImmediatelyAfterMove(
                entry,
                fileSystem,
                identityProvider,
                exactNamespaceInspector,
                semantics.IsCaseSensitive);
            if (postMoveIssue is not null)
            {
                issues.Add(postMoveIssue);
                stopwatch.Stop();
                return new(
                    Phase2ExecutionState.FailedPartial,
                    preflight,
                    applied.ToArray(),
                    issues,
                    stopwatch.Elapsed);
            }
        }

        stopwatch.Stop();
        return new(
            Phase2ExecutionState.Completed,
            preflight,
            applied.ToArray(),
            issues,
            stopwatch.Elapsed);
    }

    private static TransactionPreflightResult ValidatePhase2Preflight(
        RenamePlan plan,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var issues = RenamePlanIntegrity.Validate(plan).ToList();
        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return new(false, issues, stopwatch.Elapsed);
        }

        var frozenByDirectory = plan.DirectorySemantics.ToDictionary(
            x => RenamePlanIntegrity.NormalizeFullPath(x.DirectoryPath),
            StringComparer.Ordinal);
        var currentByDirectory = new Dictionary<string, PathSemantics>(StringComparer.Ordinal);

        foreach (var pair in frozenByDirectory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = semanticsProvider.GetSemantics(pair.Key);
            currentByDirectory[pair.Key] = current;
            var frozen = pair.Value;
            if (current.IsCaseSensitive != frozen.IsCaseSensitive)
                issues.Add(Error("PHASE2_PATH_SEMANTICS_CHANGED", "Phase 2 前目录大小写语义已变化。", path: pair.Key));
            else if (frozen.IsReliable && !current.IsReliable)
                issues.Add(Error("PHASE2_PATH_SEMANTICS_UNVERIFIABLE", "Phase 2 前无法继续可靠确认目录语义。", path: pair.Key));
            else if (!current.IsReliable)
                issues.Add(Warning("PHASE2_PATH_SEMANTICS_BEST_EFFORT", "Phase 2 使用 Best-effort 路径语义。", path: pair.Key));
        }

        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return new(false, issues, stopwatch.Elapsed);
        }

        var bestEffortIdentity = 0;
        foreach (var entry in plan.Entries.OrderBy(x => x.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
            var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
            ValidateCurrentPathLimits(entry, currentByDirectory[directory], issues);

            var sourceKind = fileSystem.GetEntryKind(entry.SourcePath);
            if (sourceKind != FileSystemEntryKind.Missing)
                issues.Add(Error("PHASE2_SOURCE_NOT_VACATED", "Phase 2 前 Source namespace 必须已经完全腾空。", entry, entry.SourcePath));

            var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
            if (tempKind == FileSystemEntryKind.Missing)
                issues.Add(Error("PHASE2_TEMP_MISSING", "Phase 2 前临时对象不存在。", entry, entry.TemporaryPath));
            else if (tempKind == FileSystemEntryKind.Other)
                issues.Add(Error("PHASE2_TEMP_UNREADABLE", "Phase 2 前无法可靠访问临时对象。", entry, entry.TemporaryPath));
            else if (tempKind != expectedKind)
                issues.Add(Error("PHASE2_TEMP_KIND_CHANGED", "Phase 2 前临时对象类型与计划不一致。", entry, entry.TemporaryPath));
            else if (entry.ExpectedFileIdentity is { } expectedIdentity)
            {
                var actualIdentity = identityProvider.TryGetIdentity(entry.TemporaryPath, entry.IsDirectory);
                if (actualIdentity is null)
                    issues.Add(Error("PHASE2_TEMP_IDENTITY_UNVERIFIABLE", "Phase 2 前无法确认临时对象的冻结 FileIdentity。", entry, entry.TemporaryPath));
                else if (actualIdentity.Value != expectedIdentity)
                    issues.Add(Error("PHASE2_TEMP_IDENTITY_CHANGED", "Phase 2 前临时对象已不是冻结计划中的同一对象。", entry, entry.TemporaryPath));
            }
            else
            {
                bestEffortIdentity++;
            }

            var targetKind = fileSystem.GetEntryKind(entry.TargetPath);
            if (targetKind == FileSystemEntryKind.Other)
                issues.Add(Error("PHASE2_TARGET_UNVERIFIABLE", "Phase 2 前无法可靠确认目标 namespace 空闲。", entry, entry.TargetPath));
            else if (targetKind != FileSystemEntryKind.Missing)
                issues.Add(Error("PHASE2_TARGET_ALREADY_EXISTS", "Phase 2 前目标 namespace 已被占用，拒绝覆盖。", entry, entry.TargetPath));
        }

        if (bestEffortIdentity > 0)
            issues.Add(Warning("PHASE2_IDENTITY_BEST_EFFORT", $"{bestEffortIdentity} 个计划项缺少冻结 FileIdentity，Phase 2 只能使用 Best-effort 身份校验。"));

        stopwatch.Stop();
        return new(!issues.Any(x => x.Severity == ValidationSeverity.Error), issues, stopwatch.Elapsed);
    }


    private static void ValidateCurrentPathLimits(
        RenamePlanEntry entry,
        PathSemantics current,
        List<TransactionIssue> issues)
    {
        foreach (var path in new[] { entry.TemporaryPath, entry.TargetPath })
        {
            if (current.MaxComponentLength is { } maxComponent
                && Path.GetFileName(path).Length > maxComponent)
            {
                issues.Add(Error(
                    "PHASE2_PATH_COMPONENT_LIMIT_CHANGED",
                    $"Phase 2 前目录的文件名长度上限为 {maxComponent}，冻结路径已不满足该限制。",
                    entry,
                    path));
            }

            if (current.MaxPathLength is { } maxPath && path.Length > maxPath)
            {
                issues.Add(Error(
                    "PHASE2_PATH_LENGTH_LIMIT_CHANGED",
                    $"Phase 2 前目录的路径长度上限为 {maxPath}，冻结路径已不满足该限制。",
                    entry,
                    path));
            }
        }
    }

    private static TransactionIssue? ValidateEntryImmediatelyBeforeMove(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider)
    {
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
        if (tempKind == FileSystemEntryKind.Missing)
            return Error("PHASE2_TEMP_MISSING_JIT", "Temp → Target 前临时对象已不存在。", entry, entry.TemporaryPath);
        if (tempKind == FileSystemEntryKind.Other)
            return Error("PHASE2_TEMP_UNREADABLE_JIT", "Temp → Target 前无法可靠访问临时对象。", entry, entry.TemporaryPath);
        if (tempKind != expectedKind)
            return Error("PHASE2_TEMP_KIND_CHANGED_JIT", "Temp → Target 前临时对象类型已变化。", entry, entry.TemporaryPath);

        var targetKind = fileSystem.GetEntryKind(entry.TargetPath);
        if (targetKind == FileSystemEntryKind.Other)
            return Error("PHASE2_TARGET_UNVERIFIABLE_JIT", "Temp → Target 前无法可靠确认目标 namespace 空闲。", entry, entry.TargetPath);
        if (targetKind != FileSystemEntryKind.Missing)
            return Error("PHASE2_TARGET_ALREADY_EXISTS_JIT", "Temp → Target 前目标 namespace 被外部占用。", entry, entry.TargetPath);

        if (entry.ExpectedFileIdentity is { } expectedIdentity)
        {
            var actualIdentity = identityProvider.TryGetIdentity(entry.TemporaryPath, entry.IsDirectory);
            if (actualIdentity is null)
                return Error("PHASE2_TEMP_IDENTITY_UNVERIFIABLE_JIT", "Temp → Target 前无法重新确认冻结 FileIdentity。", entry, entry.TemporaryPath);
            if (actualIdentity.Value != expectedIdentity)
                return Error("PHASE2_TEMP_IDENTITY_CHANGED_JIT", "Temp → Target 前临时对象 FileIdentity 已变化。", entry, entry.TemporaryPath);
        }

        return null;
    }

    private static TransactionIssue? ValidateImmediatelyAfterMove(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector,
        bool caseSensitive)
    {
        if (fileSystem.GetEntryKind(entry.TemporaryPath) != FileSystemEntryKind.Missing)
            return Error("PHASE2_TEMP_STILL_PRESENT", "Temp → Target 返回成功，但临时 namespace 仍可见。", entry, entry.TemporaryPath);

        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        if (fileSystem.GetEntryKind(entry.TargetPath) != expectedKind)
            return Error("PHASE2_TARGET_POSTCHECK_FAILED", "Temp → Target 返回成功，但目标 namespace 未出现预期对象。", entry, entry.TargetPath);

        if (entry.ExpectedFileIdentity is { } expectedIdentity)
        {
            var actualIdentity = identityProvider.TryGetIdentity(entry.TargetPath, entry.IsDirectory);
            if (actualIdentity is null)
                return Error("PHASE2_TARGET_IDENTITY_UNVERIFIABLE", "Temp → Target 后无法确认目标对象的 FileIdentity。", entry, entry.TargetPath);
            if (actualIdentity.Value != expectedIdentity)
                return Error("PHASE2_TARGET_IDENTITY_CHANGED", "Temp → Target 后目标对象不是冻结计划中的同一 FileIdentity。", entry, entry.TargetPath);
        }

        if (!caseSensitive && IsCaseOnlyRename(entry))
        {
            var actualPath = exactNamespaceInspector.TryGetActualPath(entry.TargetPath, entry.IsDirectory, caseSensitive: false);
            if (actualPath is null
                || !string.Equals(Path.GetFileName(actualPath), Path.GetFileName(entry.TargetPath), StringComparison.Ordinal))
            {
                return Error("PHASE2_CASE_ONLY_SPELLING_MISMATCH", "大小写改名后目标 namespace 的实际拼写与冻结 Target 不一致。", entry, entry.TargetPath);
            }
        }

        return null;
    }

    private static MoveExceptionObservation ObserveAfterMoveException(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider)
    {
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
        var targetKind = fileSystem.GetEntryKind(entry.TargetPath);

        if (tempKind == FileSystemEntryKind.Missing && targetKind == expectedKind)
        {
            TransactionIssue? identityIssue = null;
            if (entry.ExpectedFileIdentity is { } expectedIdentity)
            {
                var targetIdentity = identityProvider.TryGetIdentity(entry.TargetPath, entry.IsDirectory);
                if (targetIdentity is null)
                    identityIssue = Error("PHASE2_TARGET_IDENTITY_UNVERIFIABLE", "move 异常后对象位于 Target，但无法确认 FileIdentity。", entry, entry.TargetPath);
                else if (targetIdentity.Value != expectedIdentity)
                    identityIssue = Error("PHASE2_TARGET_IDENTITY_CHANGED", "move 异常后 Target 上的对象不是冻结计划中的同一 FileIdentity。", entry, entry.TargetPath);
            }

            return new(true, false, identityIssue);
        }

        if (tempKind == expectedKind && targetKind == FileSystemEntryKind.Missing)
            return new(false, true, null);

        return new(false, false, null);
    }

    private static bool IsCaseOnlyRename(RenamePlanEntry entry)
        => !string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.Ordinal)
           && string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.OrdinalIgnoreCase);

    private static Phase2AppliedEntry Applied(RenamePlanEntry entry)
        => new(entry.Ordinal, entry.ItemId, entry.TemporaryPath, entry.TargetPath, entry.IsDirectory, entry.ExpectedFileIdentity);

    private readonly record struct MoveExceptionObservation(
        bool ConfirmedApplied,
        bool ConfirmedNotApplied,
        TransactionIssue? IdentityIssue);

    private static TransactionIssue Error(string code, string message, RenamePlanEntry? entry = null, string? path = null)
        => new(ValidationSeverity.Error, code, message, entry?.Ordinal, entry?.ItemId, path);

    private static TransactionIssue Warning(string code, string message, RenamePlanEntry? entry = null, string? path = null)
        => new(ValidationSeverity.Warning, code, message, entry?.Ordinal, entry?.ItemId, path);
}
