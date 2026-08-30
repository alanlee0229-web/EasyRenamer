using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.7.1 production transaction orchestration over an already-persisted frozen plan.
/// It does not reinterpret naming rules and it does not expose any overwrite/delete behavior.
/// Every Source/Temp/Target mutation passes through JournaledRenameMutationFileSystem.
/// </summary>
public static class TransactionExecutionOrchestrator
{
    public static TransactionExecutionResult Execute(
        string transactionDirectory,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        IExactNamespaceInspector exactNamespaceInspector,
        ITransactionJournalSink? journalSink = null,
        CancellationToken cancellationToken = default)
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
                TransactionExecutionOverallState.SessionBusy,
                null,
                null,
                null,
                issues.ToArray(),
                stopwatch.Elapsed);
        }
        using var sessionLease = leaseResult.Lease;

        var planLoad = RenamePlanPersistence.Load(Path.Combine(directory, RenamePlanPersistence.PlanFileName));
        issues.AddRange(planLoad.Issues);
        if (!planLoad.Success || planLoad.Plan is not { } plan)
        {
            stopwatch.Stop();
            return new(
                TransactionExecutionOverallState.NotStarted,
                null,
                null,
                null,
                issues.ToArray(),
                stopwatch.Elapsed);
        }
        cancellationToken.ThrowIfCancellationRequested();

        // A TransactionId is a single immutable execution attempt. Before any new mutation, reconcile
        // persisted history with the filesystem. Completed, rolled-back, partial, externally modified,
        // ambiguous, or corrupted transactions must never be silently re-executed under the same ID.
        var initialAnalysis = TransactionRecoveryAnalyzer.Analyze(
            directory,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(initialAnalysis.Issues);
        if (initialAnalysis.State != TransactionRecoveryState.NotStarted
            || initialAnalysis.Issues.Any(x => x.Severity == ValidationSeverity.Error))
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "TRANSACTION_NOT_PRISTINE",
                $"TransactionId cannot start from recovery state {initialAnalysis.State}. Use recovery/new plan instead of re-executing the same transaction.",
                Path: directory));
            stopwatch.Stop();
            return new(
                TransactionExecutionOverallState.RejectedByRecoveryState,
                plan,
                null,
                null,
                issues.ToArray(),
                stopwatch.Elapsed);
        }

        WriteCheckpointAdvisory(directory, plan, TransactionCheckpointPhase.Phase1InProgress, null, "Phase1 durable execution started.", issues);
        using var journaledMutation = new JournaledRenameMutationFileSystem(plan, directory, mutationFileSystem, journalSink);
        var phase1 = TransactionPhase1Executor.Execute(
            plan,
            fileSystem,
            semanticsProvider,
            identityProvider,
            journaledMutation,
            cancellationToken);
        issues.AddRange(phase1.Issues);

        if (!phase1.Success)
        {
            var hasMutation = phase1.HasMutation;
            WriteCheckpointAdvisory(
                directory,
                plan,
                hasMutation ? TransactionCheckpointPhase.RecoveryRequired : TransactionCheckpointPhase.Prepared,
                phase1.AppliedEntries.LastOrDefault()?.Ordinal,
                hasMutation ? "Phase1 stopped after mutation; recovery analysis required." : "Phase1 stopped before namespace mutation.",
                issues);
            stopwatch.Stop();
            return new(
                hasMutation ? TransactionExecutionOverallState.RecoveryRequired : TransactionExecutionOverallState.FailedBeforeMutation,
                plan,
                phase1,
                null,
                issues.ToArray(),
                stopwatch.Elapsed);
        }

        WriteCheckpointAdvisory(
            directory,
            plan,
            TransactionCheckpointPhase.Phase1Applied,
            phase1.AppliedEntries.LastOrDefault()?.Ordinal,
            "All Source -> Temp mutations completed.",
            issues);
        WriteCheckpointAdvisory(directory, plan, TransactionCheckpointPhase.Phase2InProgress, null, "Phase2 durable execution started.", issues);

        var phase2 = TransactionPhase2Executor.Execute(
            plan,
            fileSystem,
            semanticsProvider,
            identityProvider,
            journaledMutation,
            exactNamespaceInspector,
            cancellationToken);
        issues.AddRange(phase2.Issues);

        if (!phase2.Success)
        {
            WriteCheckpointAdvisory(
                directory,
                plan,
                TransactionCheckpointPhase.RecoveryRequired,
                phase2.AppliedEntries.LastOrDefault()?.Ordinal,
                "Phase2 stopped; recovery analysis required.",
                issues);
            stopwatch.Stop();
            return new(
                TransactionExecutionOverallState.RecoveryRequired,
                plan,
                phase1,
                phase2,
                issues.ToArray(),
                stopwatch.Elapsed);
        }

        WriteCheckpointAdvisory(
            directory,
            plan,
            TransactionCheckpointPhase.Completed,
            phase2.AppliedEntries.LastOrDefault()?.Ordinal,
            "Two-phase rename completed.",
            issues);

        stopwatch.Stop();
        return new(
            TransactionExecutionOverallState.Completed,
            plan,
            phase1,
            phase2,
            issues.ToArray(),
            stopwatch.Elapsed);
    }

    internal static void WriteCheckpointAdvisory(
        string transactionDirectory,
        RenamePlan plan,
        TransactionCheckpointPhase phase,
        int? lastCompletedOrdinal,
        string note,
        ICollection<TransactionIssue> issues)
    {
        var result = TransactionStateStore.Write(
            transactionDirectory,
            TransactionStateStore.Create(plan, phase, lastCompletedOrdinal, note));
        if (result.Success) return;

        // state.json is explicitly advisory. Its failure is retained as a warning but must not cause
        // an otherwise journal-protected namespace mutation to be misreported as failed.
        var detail = string.Join(" | ", result.Issues.Select(x => $"{x.Code}: {x.Message}"));
        issues.Add(new TransactionIssue(
            ValidationSeverity.Warning,
            "STATE_ADVISORY_WRITE_FAILED",
            string.IsNullOrWhiteSpace(detail) ? "state.json advisory checkpoint could not be written." : detail,
            Path: result.StatePath));
    }
}
