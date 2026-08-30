using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.6-E rollback foundation for known two-phase states.
///
/// Recovery strategy is deliberately two-step and idempotent:
/// 1) every object currently at Target is moved back to its unique TemporaryPath;
/// 2) every object currently at TemporaryPath is moved back to SourcePath.
///
/// This avoids rename-cycle dependencies during rollback (A->B, B->C, C->A). The executor never
/// overwrites or deletes. It derives state from the frozen Plan + current filesystem + FileIdentity;
/// it does not trust a prior in-memory Phase1/Phase2 result as authoritative evidence.
/// </summary>
public static class TransactionRollbackExecutor
{
    public static TransactionRollbackResult Execute(
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

        var issues = RenamePlanIntegrity.Validate(plan).ToList();
        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return new(RollbackExecutionState.Ambiguous, Array.Empty<RollbackAppliedMove>(), issues, stopwatch.Elapsed);
        }

        var semanticsByDirectory = plan.DirectorySemantics.ToDictionary(
            x => RenamePlanIntegrity.NormalizeFullPath(x.DirectoryPath),
            StringComparer.Ordinal);
        var currentSemanticsByDirectory = new Dictionary<string, PathSemantics>(StringComparer.Ordinal);

        foreach (var pair in semanticsByDirectory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = semanticsProvider.GetSemantics(pair.Key);
            currentSemanticsByDirectory[pair.Key] = current;
            var frozen = pair.Value;
            if (current.IsCaseSensitive != frozen.IsCaseSensitive)
                issues.Add(Error("ROLLBACK_PATH_SEMANTICS_CHANGED", "回滚前目录大小写语义已变化，拒绝自动恢复。", path: pair.Key));
            else if (frozen.IsReliable && !current.IsReliable)
                issues.Add(Error("ROLLBACK_PATH_SEMANTICS_UNVERIFIABLE", "回滚前无法继续可靠确认目录语义，拒绝自动恢复。", path: pair.Key));
            else if (!current.IsReliable)
                issues.Add(Warning("ROLLBACK_PATH_SEMANTICS_BEST_EFFORT", "当前目录仅支持 Best-effort Recovery。", path: pair.Key));
        }

        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return new(RollbackExecutionState.Ambiguous, Array.Empty<RollbackAppliedMove>(), issues, stopwatch.Elapsed);
        }

        foreach (var entry in plan.Entries)
        {
            var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
            ValidateRollbackPathLimits(entry, currentSemanticsByDirectory[directory], issues);
        }

        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            stopwatch.Stop();
            return new(RollbackExecutionState.Ambiguous, Array.Empty<RollbackAppliedMove>(), issues, stopwatch.Elapsed);
        }

        var initial = InspectAll(plan, fileSystem, identityProvider, exactNamespaceInspector, semanticsByDirectory);
        issues.AddRange(initial.Issues);
        if (!initial.SafeToContinue)
        {
            stopwatch.Stop();
            return new(RollbackExecutionState.Ambiguous, Array.Empty<RollbackAppliedMove>(), issues, stopwatch.Elapsed);
        }

        // Final cancellation boundary. Once rollback starts mutating namespaces, an external
        // cancellation request is not used to intentionally strand another partial recovery state.
        cancellationToken.ThrowIfCancellationRequested();
        var applied = new List<RollbackAppliedMove>();

        // Stage R1: Target -> Temp. This normalizes any partial/completed Phase 2 into the same state
        // as a completed Phase 1, while unique temp paths eliminate cycle-order dependencies.
        foreach (var observation in initial.Observations
                     .Where(x => x.Location == ObservedPlanLocation.Target)
                     .OrderBy(x => x.Entry.Ordinal))
        {
            var entry = observation.Entry;
            var issue = ValidateBeforeRollbackMove(
                entry,
                entry.TargetPath,
                entry.TemporaryPath,
                "ROLLBACK_R1",
                fileSystem,
                identityProvider,
                exactNamespaceInspector,
                semanticsByDirectory);
            if (issue is not null)
                return Failed(stopwatch, applied, issues, issue);

            var moveResult = MoveAndReconcile(
                entry,
                entry.TargetPath,
                entry.TemporaryPath,
                "Target→Temp",
                "ROLLBACK_R1",
                fileSystem,
                identityProvider,
                mutationFileSystem,
                exactNamespaceInspector,
                semanticsByDirectory);
            if (moveResult.Applied)
                applied.Add(new(entry.Ordinal, entry.ItemId, entry.TargetPath, entry.TemporaryPath, entry.IsDirectory, "TargetToTemp"));
            if (moveResult.Issue is not null)
                return Failed(stopwatch, applied, issues, moveResult.Issue);
        }

        var normalized = InspectAll(plan, fileSystem, identityProvider, exactNamespaceInspector, semanticsByDirectory);
        issues.AddRange(normalized.Issues);
        if (!normalized.SafeToContinue
            || normalized.Observations.Any(x => x.Location == ObservedPlanLocation.Target))
        {
            stopwatch.Stop();
            if (normalized.SafeToContinue)
                issues.Add(Error("ROLLBACK_NORMALIZATION_INCOMPLETE", "Target → Temp 归一化后仍存在对象停留在 Target。"));
            return new(RollbackExecutionState.Ambiguous, applied.ToArray(), issues, stopwatch.Elapsed);
        }

        // Stage R2: Temp -> Source. Objects already at Source are left untouched. Re-running rollback
        // after success therefore performs zero namespace mutations.
        foreach (var observation in normalized.Observations
                     .Where(x => x.Location == ObservedPlanLocation.Temp)
                     .OrderBy(x => x.Entry.Ordinal))
        {
            var entry = observation.Entry;
            var issue = ValidateBeforeRollbackMove(
                entry,
                entry.TemporaryPath,
                entry.SourcePath,
                "ROLLBACK_R2",
                fileSystem,
                identityProvider,
                exactNamespaceInspector,
                semanticsByDirectory);
            if (issue is not null)
                return Failed(stopwatch, applied, issues, issue);

            var moveResult = MoveAndReconcile(
                entry,
                entry.TemporaryPath,
                entry.SourcePath,
                "Temp→Source",
                "ROLLBACK_R2",
                fileSystem,
                identityProvider,
                mutationFileSystem,
                exactNamespaceInspector,
                semanticsByDirectory);
            if (moveResult.Applied)
                applied.Add(new(entry.Ordinal, entry.ItemId, entry.TemporaryPath, entry.SourcePath, entry.IsDirectory, "TempToSource"));
            if (moveResult.Issue is not null)
                return Failed(stopwatch, applied, issues, moveResult.Issue);
        }

        var final = InspectAll(plan, fileSystem, identityProvider, exactNamespaceInspector, semanticsByDirectory);
        issues.AddRange(final.Issues);
        if (!final.SafeToContinue
            || final.Observations.Any(x => x.Location != ObservedPlanLocation.Source))
        {
            stopwatch.Stop();
            if (final.SafeToContinue)
                issues.Add(Error("ROLLBACK_FINAL_STATE_INCOMPLETE", "回滚结束后并非所有对象都恢复到冻结 Source。"));
            return new(RollbackExecutionState.Ambiguous, applied.ToArray(), issues, stopwatch.Elapsed);
        }

        stopwatch.Stop();
        return new(RollbackExecutionState.Completed, applied.ToArray(), issues, stopwatch.Elapsed);
    }

    private static void ValidateRollbackPathLimits(
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
                    "ROLLBACK_PATH_COMPONENT_LIMIT_CHANGED",
                    $"回滚前目录的文件名长度上限为 {maxComponent}，冻结路径已不满足该限制。",
                    entry,
                    path));
            }

            if (current.MaxPathLength is { } maxPath && path.Length > maxPath)
            {
                issues.Add(Error(
                    "ROLLBACK_PATH_LENGTH_LIMIT_CHANGED",
                    $"回滚前目录的路径长度上限为 {maxPath}，冻结路径已不满足该限制。",
                    entry,
                    path));
            }
        }
    }

    private static InspectionBatch InspectAll(
        RenamePlan plan,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector,
        IReadOnlyDictionary<string, RenamePlanDirectorySemantics> semanticsByDirectory)
    {
        var observations = new List<EntryObservation>(plan.Entries.Count);
        var issues = new List<TransactionIssue>();

        foreach (var entry in plan.Entries.OrderBy(x => x.Ordinal))
        {
            var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
            var semantics = semanticsByDirectory[directory];
            var observation = InspectEntry(entry, semantics, fileSystem, identityProvider, exactNamespaceInspector);
            observations.Add(observation);
            if (observation.Issue is not null) issues.Add(observation.Issue);
        }

        return new(observations, issues, issues.All(x => x.Severity != ValidationSeverity.Error));
    }

    private static EntryObservation InspectEntry(
        RenamePlanEntry entry,
        RenamePlanDirectorySemantics semantics,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        if (!semantics.IsCaseSensitive && IsCaseOnlyRename(entry))
            return InspectCaseOnlyEntry(entry, fileSystem, identityProvider, exactNamespaceInspector);

        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var paths = new[]
        {
            (Location: ObservedPlanLocation.Source, Path: entry.SourcePath),
            (Location: ObservedPlanLocation.Temp, Path: entry.TemporaryPath),
            (Location: ObservedPlanLocation.Target, Path: entry.TargetPath),
        };

        if (entry.ExpectedFileIdentity is { } expectedIdentity)
        {
            var matches = new List<(ObservedPlanLocation Location, string Path)>();
            foreach (var candidate in paths)
            {
                var kind = fileSystem.GetEntryKind(candidate.Path);
                if (kind == FileSystemEntryKind.Other)
                    return Ambiguous(entry, "ROLLBACK_NAMESPACE_UNVERIFIABLE", "回滚时无法可靠读取 Source/Temp/Target namespace。", candidate.Path);
                if (kind == FileSystemEntryKind.Missing || kind != expectedKind) continue;
                var identity = identityProvider.TryGetIdentity(candidate.Path, entry.IsDirectory);
                if (identity is not null && identity.Value == expectedIdentity)
                    matches.Add(candidate);
            }

            if (matches.Count != 1)
            {
                return Ambiguous(
                    entry,
                    matches.Count == 0 ? "ROLLBACK_OBJECT_NOT_FOUND" : "ROLLBACK_OBJECT_MULTIPLE_LOCATIONS",
                    matches.Count == 0
                        ? "无法在 Source/Temp/Target 中定位冻结计划对象。"
                        : "冻结计划对象似乎同时出现在多个 namespace，拒绝自动回滚。",
                    entry.SourcePath);
            }

            return new(entry, matches[0].Location, matches[0].Path, null);
        }

        // Best-effort mode: without a frozen FileIdentity, automatic rollback is allowed only when
        // exactly one of Source/Temp/Target contains an object of the expected kind.
        var present = new List<(ObservedPlanLocation Location, string Path)>();
        foreach (var candidate in paths)
        {
            var kind = fileSystem.GetEntryKind(candidate.Path);
            if (kind == FileSystemEntryKind.Other)
                return Ambiguous(entry, "ROLLBACK_NAMESPACE_UNVERIFIABLE", "回滚时无法可靠读取 Source/Temp/Target namespace。", candidate.Path);
            if (kind == expectedKind) present.Add(candidate);
        }

        if (present.Count != 1)
        {
            return Ambiguous(
                entry,
                "ROLLBACK_BEST_EFFORT_AMBIGUOUS",
                "该计划项没有冻结 FileIdentity，且当前 namespace 状态无法唯一定位对象。",
                entry.SourcePath);
        }

        return new(entry, present[0].Location, present[0].Path,
            Warning("ROLLBACK_IDENTITY_BEST_EFFORT", "该计划项缺少冻结 FileIdentity，回滚仅能 Best-effort 验证。", entry, present[0].Path));
    }

    private static EntryObservation InspectCaseOnlyEntry(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
        if (tempKind == FileSystemEntryKind.Other)
            return Ambiguous(entry, "ROLLBACK_TEMP_UNVERIFIABLE", "大小写改名回滚时无法可靠读取 Temp。", entry.TemporaryPath);

        if (tempKind == expectedKind)
        {
            if (!MatchesIdentity(entry, entry.TemporaryPath, identityProvider, out var identityIssue))
                return new(entry, ObservedPlanLocation.Ambiguous, entry.TemporaryPath, identityIssue);
            return new(entry, ObservedPlanLocation.Temp, entry.TemporaryPath, identityIssue);
        }

        var actualPath = exactNamespaceInspector.TryGetActualPath(entry.SourcePath, entry.IsDirectory, caseSensitive: false);
        if (actualPath is null)
            return Ambiguous(entry, "ROLLBACK_CASE_ONLY_OBJECT_NOT_FOUND", "无法确认大小写改名对象当前的实际 namespace 拼写。", entry.SourcePath);

        if (!MatchesIdentity(entry, actualPath, identityProvider, out var issue))
            return new(entry, ObservedPlanLocation.Ambiguous, actualPath, issue);

        var actualName = Path.GetFileName(actualPath);
        if (string.Equals(actualName, Path.GetFileName(entry.SourcePath), StringComparison.Ordinal))
            return new(entry, ObservedPlanLocation.Source, actualPath, issue);
        if (string.Equals(actualName, Path.GetFileName(entry.TargetPath), StringComparison.Ordinal))
            return new(entry, ObservedPlanLocation.Target, actualPath, issue);

        return Ambiguous(entry, "ROLLBACK_CASE_ONLY_SPELLING_AMBIGUOUS", "对象存在，但实际文件名大小写既不等于冻结 Source 也不等于冻结 Target。", actualPath);
    }

    private static TransactionIssue? ValidateBeforeRollbackMove(
        RenamePlanEntry entry,
        string fromPath,
        string toPath,
        string stageCode,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector,
        IReadOnlyDictionary<string, RenamePlanDirectorySemantics> semanticsByDirectory)
    {
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var fromKind = fileSystem.GetEntryKind(fromPath);
        if (fromKind != expectedKind)
            return Error($"{stageCode}_SOURCE_NOT_READY", "回滚 move 前源 namespace 不再包含预期对象类型。", entry, fromPath);
        if (!MatchesIdentity(entry, fromPath, identityProvider, out var identityIssue))
            return identityIssue;

        var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
        var semantics = semanticsByDirectory[directory];
        if (!semantics.IsCaseSensitive && IsCaseOnlyPair(fromPath, toPath))
        {
            var actual = exactNamespaceInspector.TryGetActualPath(fromPath, entry.IsDirectory, caseSensitive: false);
            if (actual is null || !string.Equals(Path.GetFileName(actual), Path.GetFileName(fromPath), StringComparison.Ordinal))
                return Error($"{stageCode}_CASE_ONLY_SOURCE_MISMATCH", "回滚 move 前大小写 namespace 的实际拼写已变化。", entry, fromPath);
        }

        var destinationKind = fileSystem.GetEntryKind(toPath);
        if (destinationKind == FileSystemEntryKind.Other)
            return Error($"{stageCode}_DESTINATION_UNVERIFIABLE", "回滚目标 namespace 无法可靠确认是否空闲。", entry, toPath);
        if (destinationKind != FileSystemEntryKind.Missing)
            return Error($"{stageCode}_DESTINATION_OCCUPIED", "回滚目标 namespace 已被占用，禁止覆盖。", entry, toPath);

        return null;
    }

    private static MoveAttemptResult MoveAndReconcile(
        RenamePlanEntry entry,
        string fromPath,
        string toPath,
        string stageLabel,
        string stageCode,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        IExactNamespaceInspector exactNamespaceInspector,
        IReadOnlyDictionary<string, RenamePlanDirectorySemantics> semanticsByDirectory)
    {
        try
        {
            if (entry.IsDirectory)
                mutationFileSystem.MoveDirectoryNoOverwrite(fromPath, toPath);
            else
                mutationFileSystem.MoveFileNoOverwrite(fromPath, toPath);
        }
        catch (Exception ex)
        {
            var observation = ObserveMoveState(entry, fromPath, toPath, fileSystem, identityProvider);
            if (observation.Applied)
            {
                var postIssue = ValidateAfterRollbackMove(
                    entry, fromPath, toPath, stageCode, fileSystem, identityProvider, exactNamespaceInspector, semanticsByDirectory);
                return new(true, postIssue ?? Error(
                    $"{stageCode}_MOVE_EXCEPTION_AFTER_APPLY",
                    $"{stageLabel} 已在磁盘上生效，但 move API 随后报告异常：{ex.GetType().Name}: {ex.Message}",
                    entry,
                    toPath));
            }

            if (observation.NotApplied)
                return new(false, Error($"{stageCode}_MOVE_FAILED", $"{stageLabel} 失败且磁盘状态确认未应用：{ex.GetType().Name}: {ex.Message}", entry, fromPath));

            return new(false, Error($"{stageCode}_MOVE_STATE_AMBIGUOUS", $"{stageLabel} 异常后 namespace 状态无法可靠判定：{ex.GetType().Name}: {ex.Message}", entry, fromPath));
        }

        var issue = ValidateAfterRollbackMove(
            entry, fromPath, toPath, stageCode, fileSystem, identityProvider, exactNamespaceInspector, semanticsByDirectory);
        return new(true, issue);
    }

    private static TransactionIssue? ValidateAfterRollbackMove(
        RenamePlanEntry entry,
        string fromPath,
        string toPath,
        string stageCode,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector,
        IReadOnlyDictionary<string, RenamePlanDirectorySemantics> semanticsByDirectory)
    {
        if (fileSystem.GetEntryKind(fromPath) != FileSystemEntryKind.Missing)
            return Error($"{stageCode}_SOURCE_STILL_PRESENT", "回滚 move 返回成功，但原 namespace 仍可见。", entry, fromPath);

        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        if (fileSystem.GetEntryKind(toPath) != expectedKind)
            return Error($"{stageCode}_DESTINATION_POSTCHECK_FAILED", "回滚 move 返回成功，但目标 namespace 未出现预期对象。", entry, toPath);
        if (!MatchesIdentity(entry, toPath, identityProvider, out var identityIssue))
            return identityIssue;

        var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
        var semantics = semanticsByDirectory[directory];
        if (!semantics.IsCaseSensitive && IsCaseOnlyPair(entry.SourcePath, entry.TargetPath)
            && string.Equals(toPath, entry.SourcePath, StringComparison.OrdinalIgnoreCase))
        {
            var actual = exactNamespaceInspector.TryGetActualPath(toPath, entry.IsDirectory, caseSensitive: false);
            if (actual is null || !string.Equals(Path.GetFileName(actual), Path.GetFileName(toPath), StringComparison.Ordinal))
                return Error($"{stageCode}_CASE_ONLY_DESTINATION_MISMATCH", "大小写回滚后实际文件名拼写与冻结 Source 不一致。", entry, toPath);
        }

        return identityIssue;
    }

    private static MoveStateObservation ObserveMoveState(
        RenamePlanEntry entry,
        string fromPath,
        string toPath,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider)
    {
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var fromKind = fileSystem.GetEntryKind(fromPath);
        var toKind = fileSystem.GetEntryKind(toPath);

        if (fromKind == FileSystemEntryKind.Missing && toKind == expectedKind)
        {
            if (entry.ExpectedFileIdentity is not { } expectedIdentity) return new(true, false);
            var identity = identityProvider.TryGetIdentity(toPath, entry.IsDirectory);
            return new(identity is not null && identity.Value == expectedIdentity, false);
        }

        if (fromKind == expectedKind && toKind == FileSystemEntryKind.Missing)
        {
            if (entry.ExpectedFileIdentity is not { } expectedIdentity) return new(false, true);
            var identity = identityProvider.TryGetIdentity(fromPath, entry.IsDirectory);
            return new(false, identity is not null && identity.Value == expectedIdentity);
        }

        return new(false, false);
    }

    private static bool MatchesIdentity(
        RenamePlanEntry entry,
        string path,
        IFileIdentityProvider identityProvider,
        out TransactionIssue? issue)
    {
        issue = null;
        if (entry.ExpectedFileIdentity is not { } expectedIdentity)
        {
            issue = Warning("ROLLBACK_IDENTITY_BEST_EFFORT", "该计划项没有冻结 FileIdentity，只能 Best-effort 回滚。", entry, path);
            return true;
        }

        var actual = identityProvider.TryGetIdentity(path, entry.IsDirectory);
        if (actual is null)
        {
            issue = Error("ROLLBACK_IDENTITY_UNVERIFIABLE", "回滚时无法确认冻结 FileIdentity。", entry, path);
            return false;
        }
        if (actual.Value != expectedIdentity)
        {
            issue = Error("ROLLBACK_IDENTITY_CHANGED", "当前 namespace 上的对象不是冻结计划中的同一 FileIdentity。", entry, path);
            return false;
        }

        return true;
    }

    private static TransactionRollbackResult Failed(
        Stopwatch stopwatch,
        List<RollbackAppliedMove> applied,
        List<TransactionIssue> issues,
        TransactionIssue issue)
    {
        issues.Add(issue);
        stopwatch.Stop();
        return new(RollbackExecutionState.FailedPartial, applied.ToArray(), issues, stopwatch.Elapsed);
    }

    private static EntryObservation Ambiguous(RenamePlanEntry entry, string code, string message, string path)
        => new(entry, ObservedPlanLocation.Ambiguous, path, Error(code, message, entry, path));

    private static bool IsCaseOnlyRename(RenamePlanEntry entry)
        => IsCaseOnlyPair(entry.SourcePath, entry.TargetPath);

    private static bool IsCaseOnlyPair(string a, string b)
        => !string.Equals(a, b, StringComparison.Ordinal)
           && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static TransactionIssue Error(string code, string message, RenamePlanEntry? entry = null, string? path = null)
        => new(ValidationSeverity.Error, code, message, entry?.Ordinal, entry?.ItemId, path);

    private static TransactionIssue Warning(string code, string message, RenamePlanEntry? entry = null, string? path = null)
        => new(ValidationSeverity.Warning, code, message, entry?.Ordinal, entry?.ItemId, path);

    private enum ObservedPlanLocation
    {
        Source,
        Temp,
        Target,
        Ambiguous,
    }

    private sealed record EntryObservation(
        RenamePlanEntry Entry,
        ObservedPlanLocation Location,
        string ObservedPath,
        TransactionIssue? Issue);

    private sealed record InspectionBatch(
        IReadOnlyList<EntryObservation> Observations,
        IReadOnlyList<TransactionIssue> Issues,
        bool SafeToContinue);

    private readonly record struct MoveAttemptResult(bool Applied, TransactionIssue? Issue);
    private readonly record struct MoveStateObservation(bool Applied, bool NotApplied);
}
