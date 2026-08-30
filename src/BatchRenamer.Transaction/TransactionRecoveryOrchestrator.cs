using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.7.1 recovery orchestration. It first performs read-only analysis, then automatically invokes
/// rollback only for states that the analyzer proves safe. ExternallyModified/Ambiguous states are
/// never mutated automatically.
/// </summary>
public static class TransactionRecoveryOrchestrator
{
    public static TransactionRecoveryResult Recover(
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
        var initial = TransactionRecoveryAnalyzer.Analyze(
            directory,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        var issues = initial.Issues.ToList();

        if (initial.Plan is not { } plan)
        {
            stopwatch.Stop();
            return new(TransactionRecoveryAction.ManualRequired, initial, null, null, issues.ToArray(), stopwatch.Elapsed);
        }

        if (!initial.RequiresRecoveryAction)
        {
            var action = initial.State switch
            {
                TransactionRecoveryState.Completed => TransactionRecoveryAction.NoActionCompleted,
                TransactionRecoveryState.NotStarted => TransactionRecoveryAction.NoActionNotStarted,
                TransactionRecoveryState.RolledBack => TransactionRecoveryAction.NoActionRolledBack,
                _ => TransactionRecoveryAction.ManualRequired,
            };
            stopwatch.Stop();
            return new(action, initial, null, initial, issues.ToArray(), stopwatch.Elapsed);
        }

        if (!initial.CanAutoRollback)
        {
            stopwatch.Stop();
            return new(TransactionRecoveryAction.ManualRequired, initial, null, null, issues.ToArray(), stopwatch.Elapsed);
        }

        var leaseResult = TransactionSessionLease.TryAcquire(directory);
        issues.AddRange(leaseResult.Issues);
        if (!leaseResult.Success || leaseResult.Lease is null)
        {
            stopwatch.Stop();
            return new(TransactionRecoveryAction.SessionBusy, initial, null, null, issues.ToArray(), stopwatch.Elapsed);
        }
        using var sessionLease = leaseResult.Lease;

        // Re-analyze after acquiring the single-writer lease. Another process may have completed or
        // altered the transaction between the initial read-only analysis and lease acquisition.
        var lockedAnalysis = TransactionRecoveryAnalyzer.Analyze(
            directory,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(lockedAnalysis.Issues);
        if (lockedAnalysis.Plan is not { } lockedPlan)
        {
            stopwatch.Stop();
            return new(TransactionRecoveryAction.ManualRequired, initial, null, lockedAnalysis, issues.ToArray(), stopwatch.Elapsed);
        }
        if (!lockedAnalysis.RequiresRecoveryAction)
        {
            var noAction = lockedAnalysis.State switch
            {
                TransactionRecoveryState.Completed => TransactionRecoveryAction.NoActionCompleted,
                TransactionRecoveryState.NotStarted => TransactionRecoveryAction.NoActionNotStarted,
                TransactionRecoveryState.RolledBack => TransactionRecoveryAction.NoActionRolledBack,
                _ => TransactionRecoveryAction.ManualRequired,
            };
            stopwatch.Stop();
            return new(noAction, initial, null, lockedAnalysis, issues.ToArray(), stopwatch.Elapsed);
        }
        if (!lockedAnalysis.CanAutoRollback)
        {
            stopwatch.Stop();
            return new(TransactionRecoveryAction.ManualRequired, initial, null, lockedAnalysis, issues.ToArray(), stopwatch.Elapsed);
        }

        plan = lockedPlan;
        TransactionExecutionOrchestrator.WriteCheckpointAdvisory(
            directory,
            plan,
            TransactionCheckpointPhase.RollbackInProgress,
            null,
            $"Automatic crash recovery rollback started from {initial.State}.",
            issues);

        using var journaledMutation = new JournaledRenameMutationFileSystem(plan, directory, mutationFileSystem, journalSink);
        var rollback = TransactionRollbackExecutor.Execute(
            plan,
            fileSystem,
            semanticsProvider,
            identityProvider,
            journaledMutation,
            exactNamespaceInspector);
        issues.AddRange(rollback.Issues);

        if (rollback.Success)
        {
            TransactionExecutionOrchestrator.WriteCheckpointAdvisory(
                directory,
                plan,
                TransactionCheckpointPhase.RolledBack,
                rollback.AppliedMoves.LastOrDefault()?.Ordinal,
                "Automatic crash recovery rollback completed.",
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
                "Automatic crash recovery rollback did not complete; manual recovery may be required.",
                issues);
        }

        var final = TransactionRecoveryAnalyzer.Analyze(
            directory,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(final.Issues);

        var actionResult = rollback.Success && final.State == TransactionRecoveryState.RolledBack
            ? TransactionRecoveryAction.AutoRollbackCompleted
            : TransactionRecoveryAction.ManualRequired;

        stopwatch.Stop();
        return new(actionResult, initial, rollback, final, issues.ToArray(), stopwatch.Elapsed);
    }
}
