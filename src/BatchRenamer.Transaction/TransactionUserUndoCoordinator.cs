using System.IO;
using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

public enum TransactionUserUndoCoordinatorState
{
    Completed,
    AlreadyUndone,
    CatalogBusy,
    StartupGateBlocked,
    NotEligible,
    RolledBackByRecovery,
    RecoveryRequired,
    ManualRequired,
}

public sealed record TransactionUserUndoCoordinatorResult(
    TransactionUserUndoCoordinatorState State,
    string TransactionDirectory,
    TransactionStartupDiscoveryResult? InitialDiscovery,
    TransactionUndoResult? Undo,
    TransactionRecoveryResult? Recovery,
    TransactionStartupDiscoveryResult? FinalDiscovery,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => State is TransactionUserUndoCoordinatorState.Completed
        or TransactionUserUndoCoordinatorState.AlreadyUndone
        or TransactionUserUndoCoordinatorState.RolledBackByRecovery;
}

/// <summary>
/// V0.9 user-command wrapper for Undo. It shares the transaction-catalog lease with new execution so
/// a different BatchRenamer process cannot start another real transaction while Undo is reconciling
/// the frozen target namespace. The proven V0.8 Undo core remains unchanged.
/// </summary>
public static class TransactionUserUndoCoordinator
{
    public static TransactionUserUndoCoordinatorResult Undo(
        string transactionDirectory,
        string transactionsRoot,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        if (string.IsNullOrWhiteSpace(transactionsRoot))
            throw new ArgumentException("Transaction root is required.", nameof(transactionsRoot));
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(mutationFileSystem);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        var stopwatch = Stopwatch.StartNew();
        var root = Path.GetFullPath(transactionsRoot);
        var directory = Path.GetFullPath(transactionDirectory);
        var issues = new List<TransactionIssue>();

        var catalogLeaseResult = TransactionCatalogLease.TryAcquire(root);
        issues.AddRange(catalogLeaseResult.Issues);
        if (!catalogLeaseResult.Success || catalogLeaseResult.Lease is null)
        {
            stopwatch.Stop();
            return new(
                catalogLeaseResult.Issues.Any(x => x.Code == "TRANSACTION_CATALOG_BUSY")
                    ? TransactionUserUndoCoordinatorState.CatalogBusy
                    : TransactionUserUndoCoordinatorState.ManualRequired,
                directory, null, null, null, null, issues.ToArray(), stopwatch.Elapsed);
        }

        using var catalogLease = catalogLeaseResult.Lease;

        var initialDiscovery = TransactionStartupDiscovery.Scan(
            root, fileSystem, semanticsProvider, identityProvider, exactNamespaceInspector);
        issues.AddRange(initialDiscovery.Issues);
        if (!initialDiscovery.CanStartNewTransaction)
        {
            stopwatch.Stop();
            return new(
                TransactionUserUndoCoordinatorState.StartupGateBlocked,
                directory, initialDiscovery, null, null, initialDiscovery, issues.ToArray(), stopwatch.Elapsed);
        }

        TransactionUndoResult undo;
        try
        {
            undo = TransactionUndoOrchestrator.Undo(
                directory,
                fileSystem,
                semanticsProvider,
                identityProvider,
                mutationFileSystem,
                exactNamespaceInspector);
            issues.AddRange(undo.Issues);
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "USER_UNDO_EXCEPTION",
                $"撤销事务时发生未处理异常：{ex.GetType().Name}: {ex.Message}",
                Path: directory));
            var failedDiscovery = TransactionStartupDiscovery.Scan(
                root, fileSystem, semanticsProvider, identityProvider, exactNamespaceInspector);
            issues.AddRange(failedDiscovery.Issues);
            stopwatch.Stop();
            return new(
                failedDiscovery.GateState == TransactionStartupGateState.ManualRequired
                    ? TransactionUserUndoCoordinatorState.ManualRequired
                    : TransactionUserUndoCoordinatorState.RecoveryRequired,
                directory, initialDiscovery, null, null, failedDiscovery, issues.ToArray(), stopwatch.Elapsed);
        }

        TransactionRecoveryResult? recovery = null;
        if (undo.RequiresRecovery)
        {
            try
            {
                recovery = TransactionRecoveryOrchestrator.Recover(
                    directory,
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
                    "USER_UNDO_AUTO_RECOVERY_EXCEPTION",
                    $"撤销中断后的自动恢复异常：{ex.GetType().Name}: {ex.Message}",
                    Path: directory));
            }
        }

        var finalDiscovery = TransactionStartupDiscovery.Scan(
            root, fileSystem, semanticsProvider, identityProvider, exactNamespaceInspector);
        issues.AddRange(finalDiscovery.Issues);

        var state = undo.State switch
        {
            TransactionUndoState.Completed => TransactionUserUndoCoordinatorState.Completed,
            TransactionUndoState.AlreadyUndone => TransactionUserUndoCoordinatorState.AlreadyUndone,
            TransactionUndoState.NotEligible => TransactionUserUndoCoordinatorState.NotEligible,
            TransactionUndoState.SessionBusy => TransactionUserUndoCoordinatorState.CatalogBusy,
            TransactionUndoState.RecoveryRequired when recovery?.Action == TransactionRecoveryAction.AutoRollbackCompleted
                => TransactionUserUndoCoordinatorState.RolledBackByRecovery,
            TransactionUndoState.RecoveryRequired => TransactionUserUndoCoordinatorState.RecoveryRequired,
            TransactionUndoState.ManualRequired => TransactionUserUndoCoordinatorState.ManualRequired,
            _ => finalDiscovery.GateState == TransactionStartupGateState.ManualRequired
                ? TransactionUserUndoCoordinatorState.ManualRequired
                : TransactionUserUndoCoordinatorState.NotEligible,
        };

        stopwatch.Stop();
        return new(
            state,
            directory,
            initialDiscovery,
            undo,
            recovery,
            finalDiscovery,
            issues.ToArray(),
            stopwatch.Elapsed);
    }
}
