using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.8.0 explicit user Undo for a completed frozen transaction. Undo is not a reinterpretation of
/// naming rules: it reuses the frozen RenamePlan and the already validated durable Rollback protocol.
/// A transaction is eligible only while every frozen object is still proven at its Target namespace.
/// </summary>
public static class TransactionUndoOrchestrator
{
    public static TransactionUndoResult Undo(
        string transactionDirectory,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        IExactNamespaceInspector exactNamespaceInspector,
        ITransactionJournalSink? journalSink = null)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(mutationFileSystem);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        var stopwatch = Stopwatch.StartNew();
        var directory = Path.GetFullPath(transactionDirectory);
        var issues = new List<TransactionIssue>();

        var leaseResult = TransactionSessionLease.TryAcquire(directory);
        issues.AddRange(leaseResult.Issues);
        if (!leaseResult.Success || leaseResult.Lease is null)
        {
            stopwatch.Stop();
            return new(
                leaseResult.Issues.Any(x => x.Code == "TRANSACTION_SESSION_BUSY")
                    ? TransactionUndoState.SessionBusy
                    : TransactionUndoState.ManualRequired,
                null,
                null,
                null,
                null,
                issues.ToArray(),
                stopwatch.Elapsed);
        }
        using var sessionLease = leaseResult.Lease;

        var initial = TransactionRecoveryAnalyzer.Analyze(
            directory,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(initial.Issues);
        var plan = initial.Plan;

        if (plan is null)
        {
            stopwatch.Stop();
            return new(TransactionUndoState.ManualRequired, null, initial, null, null, issues.ToArray(), stopwatch.Elapsed);
        }

        if (initial.State == TransactionRecoveryState.RolledBack)
        {
            stopwatch.Stop();
            return new(TransactionUndoState.AlreadyUndone, plan, initial, null, initial, issues.ToArray(), stopwatch.Elapsed);
        }

        if (!IsUndoEligible(initial, plan))
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Warning,
                "UNDO_NOT_ELIGIBLE",
                "该事务当前不满足安全撤销条件；只有仍完整保持在冻结 Target 上的已完成事务才能撤销。",
                Path: directory));
            stopwatch.Stop();
            return new(TransactionUndoState.NotEligible, plan, initial, null, initial, issues.ToArray(), stopwatch.Elapsed);
        }

        TransactionExecutionOrchestrator.WriteCheckpointAdvisory(
            directory,
            plan,
            TransactionCheckpointPhase.RollbackInProgress,
            null,
            "User Undo rollback started.",
            issues);

        TransactionRollbackResult rollback;
        try
        {
            using var journaledMutation = new JournaledRenameMutationFileSystem(plan, directory, mutationFileSystem, journalSink);
            rollback = TransactionRollbackExecutor.Execute(
                plan,
                fileSystem,
                semanticsProvider,
                identityProvider,
                journaledMutation,
                exactNamespaceInspector);
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "UNDO_ORCHESTRATION_FAILED",
                $"撤销执行异常：{ex.GetType().Name}: {ex.Message}",
                Path: directory));
            var failedFinal = TransactionRecoveryAnalyzer.Analyze(
                directory,
                fileSystem,
                semanticsProvider,
                identityProvider,
                exactNamespaceInspector);
            issues.AddRange(failedFinal.Issues);
            stopwatch.Stop();
            return new(
                failedFinal.State == TransactionRecoveryState.Completed
                    ? TransactionUndoState.FailedNoMutation
                    : failedFinal.CanAutoRollback
                        ? TransactionUndoState.RecoveryRequired
                        : TransactionUndoState.ManualRequired,
                plan,
                initial,
                null,
                failedFinal,
                issues.ToArray(),
                stopwatch.Elapsed);
        }

        issues.AddRange(rollback.Issues);
        if (rollback.Success)
        {
            TransactionExecutionOrchestrator.WriteCheckpointAdvisory(
                directory,
                plan,
                TransactionCheckpointPhase.RolledBack,
                rollback.AppliedMoves.LastOrDefault()?.Ordinal,
                "User Undo rollback completed.",
                issues);
        }
        else
        {
            TransactionExecutionOrchestrator.WriteCheckpointAdvisory(
                directory,
                plan,
                rollback.State == RollbackExecutionState.Ambiguous
                    ? TransactionCheckpointPhase.Ambiguous
                    : TransactionCheckpointPhase.RecoveryRequired,
                rollback.AppliedMoves.LastOrDefault()?.Ordinal,
                "User Undo did not complete; recovery analysis required.",
                issues);
        }

        var final = TransactionRecoveryAnalyzer.Analyze(
            directory,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(final.Issues);

        var state = rollback.Success && final.State == TransactionRecoveryState.RolledBack
            ? TransactionUndoState.Completed
            : !rollback.HasMutation && final.State == TransactionRecoveryState.Completed
                ? TransactionUndoState.FailedNoMutation
                : final.CanAutoRollback
                    ? TransactionUndoState.RecoveryRequired
                    : TransactionUndoState.ManualRequired;

        stopwatch.Stop();
        return new(state, plan, initial, rollback, final, issues.ToArray(), stopwatch.Elapsed);
    }

    private static bool IsUndoEligible(TransactionRecoveryAnalysis analysis, RenamePlan plan)
        => analysis.State == TransactionRecoveryState.Completed
           && analysis.Issues.All(x => x.Severity != ValidationSeverity.Error)
           && analysis.Entries.Count == plan.Entries.Count
           && analysis.Entries.All(x => x.State == RecoveryEntryState.Phase2Applied);
}
