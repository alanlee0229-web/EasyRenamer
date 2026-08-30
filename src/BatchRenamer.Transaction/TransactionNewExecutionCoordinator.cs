using System.IO;
using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

public enum TransactionNewExecutionState
{
    Completed,
    CatalogBusy,
    StartupGateBlocked,
    PersistenceFailed,
    FailedBeforeMutation,
    RolledBackAfterFailure,
    RecoveryRequired,
    ManualRequired,
}

public sealed record TransactionNewExecutionResult(
    TransactionNewExecutionState State,
    RenamePlan Plan,
    TransactionStartupDiscoveryResult? InitialDiscovery,
    RenamePlanPersistenceResult? Persistence,
    TransactionExecutionResult? Execution,
    TransactionRecoveryResult? Recovery,
    TransactionStartupDiscoveryResult? FinalDiscovery,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => State == TransactionNewExecutionState.Completed;
    public bool WasSafelyRolledBack => State == TransactionNewExecutionState.RolledBackAfterFailure;
    public bool RequiresManualRecovery => State is TransactionNewExecutionState.RecoveryRequired or TransactionNewExecutionState.ManualRequired;
}

/// <summary>
/// V0.9 boundary for a brand-new user-initiated transaction. It serializes new real transaction
/// commands across BatchRenamer processes, re-checks the global startup gate under that lease,
/// persists exactly one frozen plan, executes it durably, and immediately attempts the already
/// validated recovery protocol if execution stops after a namespace mutation.
/// </summary>
public static class TransactionNewExecutionCoordinator
{
    public static TransactionNewExecutionResult Execute(
        RenamePlan plan,
        string transactionsRoot,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(transactionsRoot))
            throw new ArgumentException("Transaction root is required.", nameof(transactionsRoot));
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(mutationFileSystem);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        var stopwatch = Stopwatch.StartNew();
        var issues = new List<TransactionIssue>();
        var root = Path.GetFullPath(transactionsRoot);

        var catalogLeaseResult = TransactionCatalogLease.TryAcquire(root);
        issues.AddRange(catalogLeaseResult.Issues);
        if (!catalogLeaseResult.Success || catalogLeaseResult.Lease is null)
        {
            stopwatch.Stop();
            return new(
                catalogLeaseResult.Issues.Any(x => x.Code == "TRANSACTION_CATALOG_BUSY")
                    ? TransactionNewExecutionState.CatalogBusy
                    : TransactionNewExecutionState.ManualRequired,
                plan, null, null, null, null, null, issues.ToArray(), stopwatch.Elapsed);
        }

        using var catalogLease = catalogLeaseResult.Lease;

        var initialDiscovery = TransactionStartupDiscovery.Scan(
            root, fileSystem, semanticsProvider, identityProvider, exactNamespaceInspector);
        issues.AddRange(initialDiscovery.Issues);
        if (!initialDiscovery.CanStartNewTransaction)
        {
            stopwatch.Stop();
            return new(
                TransactionNewExecutionState.StartupGateBlocked,
                plan, initialDiscovery, null, null, null, initialDiscovery, issues.ToArray(), stopwatch.Elapsed);
        }

        var persistence = RenamePlanPersistence.PersistNew(plan, root);
        issues.AddRange(persistence.Issues);
        if (!persistence.Success
            || persistence.TransactionDirectory is not { } transactionDirectory
            || persistence.PersistedPlan is null)
        {
            stopwatch.Stop();
            return new(
                TransactionNewExecutionState.PersistenceFailed,
                plan, initialDiscovery, persistence, null, null, initialDiscovery, issues.ToArray(), stopwatch.Elapsed);
        }

        TransactionExecutionResult? execution = null;
        TransactionRecoveryResult? recovery = null;
        try
        {
            execution = TransactionExecutionOrchestrator.Execute(
                transactionDirectory,
                fileSystem,
                semanticsProvider,
                identityProvider,
                mutationFileSystem,
                exactNamespaceInspector);
            issues.AddRange(execution.Issues);
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "NEW_TRANSACTION_EXECUTION_EXCEPTION",
                $"执行事务时发生未处理异常：{ex.GetType().Name}: {ex.Message}",
                Path: transactionDirectory));
        }

        if (execution?.Success == true)
        {
            var finalCompleted = TransactionStartupDiscovery.Scan(
                root, fileSystem, semanticsProvider, identityProvider, exactNamespaceInspector);
            issues.AddRange(finalCompleted.Issues);
            stopwatch.Stop();
            return new(
                TransactionNewExecutionState.Completed,
                plan, initialDiscovery, persistence, execution, null, finalCompleted, issues.ToArray(), stopwatch.Elapsed);
        }

        // Never guess from an exception/result alone whether a namespace mutation occurred. Reconcile
        // the durable plan/journal against the actual filesystem, and only auto-rollback when the
        // existing recovery protocol explicitly proves that doing so is safe.
        var analysis = TransactionRecoveryAnalyzer.Analyze(
            transactionDirectory,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(analysis.Issues);

        if (analysis.CanAutoRollback)
        {
            try
            {
                recovery = TransactionRecoveryOrchestrator.Recover(
                    transactionDirectory,
                    fileSystem,
                    semanticsProvider,
                    identityProvider,
                    mutationFileSystem,
                    exactNamespaceInspector);
                issues.AddRange(recovery.Issues);
            }
            catch (Exception ex)
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Error,
                    "NEW_TRANSACTION_AUTO_RECOVERY_EXCEPTION",
                    $"事务失败后的自动恢复异常：{ex.GetType().Name}: {ex.Message}",
                    Path: transactionDirectory));
            }
        }

        var finalDiscovery = TransactionStartupDiscovery.Scan(
            root, fileSystem, semanticsProvider, identityProvider, exactNamespaceInspector);
        issues.AddRange(finalDiscovery.Issues);

        TransactionNewExecutionState state;
        if (recovery?.Action == TransactionRecoveryAction.AutoRollbackCompleted)
        {
            state = TransactionNewExecutionState.RolledBackAfterFailure;
        }
        else if (analysis.State == TransactionRecoveryState.NotStarted
                 && execution?.State is not TransactionExecutionOverallState.RecoveryRequired)
        {
            state = TransactionNewExecutionState.FailedBeforeMutation;
        }
        else if (finalDiscovery.GateState == TransactionStartupGateState.ManualRequired)
        {
            state = TransactionNewExecutionState.ManualRequired;
        }
        else
        {
            state = TransactionNewExecutionState.RecoveryRequired;
        }

        stopwatch.Stop();
        return new(
            state,
            plan,
            initialDiscovery,
            persistence,
            execution,
            recovery,
            finalDiscovery,
            issues.ToArray(),
            stopwatch.Elapsed);
    }
}
