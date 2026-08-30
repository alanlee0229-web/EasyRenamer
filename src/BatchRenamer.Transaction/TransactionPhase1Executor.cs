using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.6-C executes only Phase 1 of the frozen two-phase rename protocol:
/// SourcePath -> TemporaryPath.
///
/// Safety boundary:
/// - consumes only an immutable RenamePlan;
/// - performs a fresh TransactionPreflight first;
/// - rechecks Source/Temp/FileIdentity immediately before each move;
/// - never overwrites a temp path;
/// - never moves Temp -> Target;
/// - never deletes anything;
/// - once the first mutation succeeds, cancellation is not observed mid-batch because stopping on a
///   cancellation request would manufacture an avoidable partial Phase-1 state.
///
/// IMPORTANT: V0.6-C does not yet provide journal/crash recovery or production rollback. Therefore
/// the application UI must not wire this executor to the normal "执行重命名" button yet.
/// </summary>
public static class TransactionPhase1Executor
{
    public static TransactionPhase1Result Execute(
        RenamePlan plan,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(mutationFileSystem);

        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        var preflight = TransactionPreflight.Validate(
            plan,
            fileSystem,
            semanticsProvider,
            identityProvider,
            cancellationToken);

        if (!preflight.CanExecute)
        {
            stopwatch.Stop();
            return new(
                Phase1ExecutionState.NotStarted,
                preflight,
                Array.Empty<Phase1AppliedEntry>(),
                preflight.Issues,
                stopwatch.Elapsed);
        }

        // Final cancellation boundary. From the first successful namespace mutation onward we finish
        // Phase 1 unless a real filesystem failure occurs; cancellation cannot intentionally strand a
        // prefix of the batch in TemporaryPath.
        cancellationToken.ThrowIfCancellationRequested();

        var issues = preflight.Issues.ToList();
        var applied = new List<Phase1AppliedEntry>(plan.Entries.Count);

        foreach (var entry in plan.Entries.OrderBy(x => x.Ordinal))
        {
            var justInTimeIssue = ValidateEntryImmediatelyBeforeMove(entry, fileSystem, identityProvider);
            if (justInTimeIssue is not null)
            {
                issues.Add(justInTimeIssue);
                stopwatch.Stop();
                return new(
                    applied.Count == 0 ? Phase1ExecutionState.NotStarted : Phase1ExecutionState.FailedPartial,
                    preflight,
                    applied.ToArray(),
                    issues,
                    stopwatch.Elapsed);
            }

            try
            {
                if (entry.IsDirectory)
                    mutationFileSystem.MoveDirectoryNoOverwrite(entry.SourcePath, entry.TemporaryPath);
                else
                    mutationFileSystem.MoveFileNoOverwrite(entry.SourcePath, entry.TemporaryPath);
            }
            catch (Exception ex)
            {
                // A provider may theoretically report an exception after the namespace operation has
                // already taken effect. Never infer "not moved" from the exception alone: reconcile
                // Source/Temp immediately and report only confirmed applied entries. Recovery later
                // must still use the frozen Plan + real filesystem rather than trusting this result.
                var exceptionState = ObserveAfterMoveException(entry, fileSystem, identityProvider);
                if (exceptionState.ConfirmedApplied)
                {
                    applied.Add(new Phase1AppliedEntry(
                        entry.Ordinal,
                        entry.ItemId,
                        entry.SourcePath,
                        entry.TemporaryPath,
                        entry.IsDirectory,
                        entry.ExpectedFileIdentity));
                    issues.Add(Error(
                        "PHASE1_MOVE_EXCEPTION_AFTER_APPLY",
                        $"Source → Temp 已在磁盘上生效，但 move API 随后报告异常：{ex.GetType().Name}: {ex.Message}",
                        entry,
                        entry.TemporaryPath));
                    if (exceptionState.IdentityIssue is not null) issues.Add(exceptionState.IdentityIssue);
                    stopwatch.Stop();
                    return new(
                        Phase1ExecutionState.FailedPartial,
                        preflight,
                        applied.ToArray(),
                        issues,
                        stopwatch.Elapsed);
                }

                if (exceptionState.ConfirmedNotApplied)
                {
                    issues.Add(Error(
                        "PHASE1_MOVE_FAILED",
                        $"Source → Temp 失败且磁盘状态确认未应用：{ex.GetType().Name}: {ex.Message}",
                        entry,
                        entry.SourcePath));
                    stopwatch.Stop();
                    return new(
                        applied.Count == 0 ? Phase1ExecutionState.NotStarted : Phase1ExecutionState.FailedPartial,
                        preflight,
                        applied.ToArray(),
                        issues,
                        stopwatch.Elapsed);
                }

                issues.Add(Error(
                    "PHASE1_MOVE_STATE_AMBIGUOUS",
                    $"Source → Temp 报告异常，且 Source/Temp 当前状态无法可靠判定；必须进入恢复流程：{ex.GetType().Name}: {ex.Message}",
                    entry,
                    entry.SourcePath));
                stopwatch.Stop();
                return new(
                    Phase1ExecutionState.FailedPartial,
                    preflight,
                    applied.ToArray(),
                    issues,
                    stopwatch.Elapsed);
            }

            // Record the mutation before post-move verification. If verification itself fails, the
            // caller still receives an exact prefix that may require recovery.
            applied.Add(new Phase1AppliedEntry(
                entry.Ordinal,
                entry.ItemId,
                entry.SourcePath,
                entry.TemporaryPath,
                entry.IsDirectory,
                entry.ExpectedFileIdentity));

            var postMoveIssue = ValidateImmediatelyAfterMove(entry, fileSystem, identityProvider);
            if (postMoveIssue is not null)
            {
                issues.Add(postMoveIssue);
                stopwatch.Stop();
                return new(
                    Phase1ExecutionState.FailedPartial,
                    preflight,
                    applied.ToArray(),
                    issues,
                    stopwatch.Elapsed);
            }
        }

        stopwatch.Stop();
        return new(
            Phase1ExecutionState.Completed,
            preflight,
            applied.ToArray(),
            issues,
            stopwatch.Elapsed);
    }

    private static TransactionIssue? ValidateEntryImmediatelyBeforeMove(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider)
    {
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var sourceKind = fileSystem.GetEntryKind(entry.SourcePath);
        if (sourceKind == FileSystemEntryKind.Missing)
            return Error("PHASE1_SOURCE_MISSING", "Source → Temp 前源对象已不存在。", entry, entry.SourcePath);
        if (sourceKind == FileSystemEntryKind.Other)
            return Error("PHASE1_SOURCE_UNREADABLE", "Source → Temp 前无法可靠访问源对象。", entry, entry.SourcePath);
        if (sourceKind != expectedKind)
            return Error("PHASE1_SOURCE_KIND_CHANGED", "Source → Temp 前源对象类型已变化。", entry, entry.SourcePath);

        var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
        if (tempKind == FileSystemEntryKind.Other)
            return Error("PHASE1_TEMP_UNVERIFIABLE", "Source → Temp 前无法可靠确认临时路径空闲。", entry, entry.TemporaryPath);
        if (tempKind != FileSystemEntryKind.Missing)
            return Error("PHASE1_TEMP_ALREADY_EXISTS", "Source → Temp 前临时路径已被占用。", entry, entry.TemporaryPath);

        if (entry.ExpectedFileIdentity is { } expectedIdentity)
        {
            var actualIdentity = identityProvider.TryGetIdentity(entry.SourcePath, entry.IsDirectory);
            if (actualIdentity is null)
                return Error("PHASE1_SOURCE_IDENTITY_UNVERIFIABLE", "Source → Temp 前无法重新确认冻结的 FileIdentity。", entry, entry.SourcePath);
            if (actualIdentity.Value != expectedIdentity)
                return Error("PHASE1_SOURCE_IDENTITY_CHANGED", "Source → Temp 前源路径已不再指向冻结计划中的同一对象。", entry, entry.SourcePath);
        }

        return null;
    }

    private static MoveExceptionObservation ObserveAfterMoveException(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider)
    {
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var sourceKind = fileSystem.GetEntryKind(entry.SourcePath);
        var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);

        if (sourceKind == FileSystemEntryKind.Missing && tempKind == expectedKind)
        {
            TransactionIssue? identityIssue = null;
            if (entry.ExpectedFileIdentity is { } expectedIdentity)
            {
                var tempIdentity = identityProvider.TryGetIdentity(entry.TemporaryPath, entry.IsDirectory);
                if (tempIdentity is null)
                {
                    identityIssue = Error(
                        "PHASE1_TEMP_IDENTITY_UNVERIFIABLE",
                        "move 异常后确认对象位于 Temp，但无法重新确认其 FileIdentity。",
                        entry,
                        entry.TemporaryPath);
                }
                else if (tempIdentity.Value != expectedIdentity)
                {
                    identityIssue = Error(
                        "PHASE1_TEMP_IDENTITY_CHANGED",
                        "move 异常后 Temp 上的对象不是冻结计划中的同一 FileIdentity。",
                        entry,
                        entry.TemporaryPath);
                }
            }

            return new(true, false, identityIssue);
        }

        if (sourceKind == expectedKind && tempKind == FileSystemEntryKind.Missing)
            return new(false, true, null);

        return new(false, false, null);
    }

    private static TransactionIssue? ValidateImmediatelyAfterMove(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider)
    {
        var sourceKind = fileSystem.GetEntryKind(entry.SourcePath);
        if (sourceKind != FileSystemEntryKind.Missing)
        {
            return Error(
                "PHASE1_SOURCE_STILL_PRESENT",
                "Source → Temp 返回成功，但源 namespace 仍可见；事务状态不明确，必须进入恢复流程。",
                entry,
                entry.SourcePath);
        }

        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
        if (tempKind != expectedKind)
        {
            return Error(
                "PHASE1_TEMP_POSTCHECK_FAILED",
                "Source → Temp 返回成功，但临时 namespace 未出现预期对象；事务状态不明确，必须进入恢复流程。",
                entry,
                entry.TemporaryPath);
        }

        if (entry.ExpectedFileIdentity is { } expectedIdentity)
        {
            var tempIdentity = identityProvider.TryGetIdentity(entry.TemporaryPath, entry.IsDirectory);
            if (tempIdentity is null)
            {
                return Error(
                    "PHASE1_TEMP_IDENTITY_UNVERIFIABLE",
                    "Source → Temp 后无法确认临时路径上的 FileIdentity；事务状态不明确，必须进入恢复流程。",
                    entry,
                    entry.TemporaryPath);
            }

            if (tempIdentity.Value != expectedIdentity)
            {
                return Error(
                    "PHASE1_TEMP_IDENTITY_CHANGED",
                    "Source → Temp 后临时路径上的对象不是冻结计划中的同一 FileIdentity；事务状态不明确。",
                    entry,
                    entry.TemporaryPath);
            }
        }

        return null;
    }

    private readonly record struct MoveExceptionObservation(
        bool ConfirmedApplied,
        bool ConfirmedNotApplied,
        TransactionIssue? IdentityIssue);

    private static TransactionIssue Error(string code, string message, RenamePlanEntry entry, string? path = null)
        => new(ValidationSeverity.Error, code, message, entry.Ordinal, entry.ItemId, path);
}
