using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.7.3 startup recovery coordinator. It is deliberately conservative:
/// - Clear catalogs: no mutation.
/// - ManualRequired or SessionBusy anywhere in the startup catalog: zero automatic recovery mutation.
/// - RecoveryRequired-only catalogs: recover candidates one by one with the already validated
///   TransactionRecoveryOrchestrator, stopping immediately on the first non-success result.
/// A fresh startup discovery scan is always performed before returning; only a final Clear gate may
/// permit a future new transaction.
/// </summary>
public static class TransactionStartupRecoveryCoordinator
{
    public static TransactionStartupRecoveryCoordinatorResult Run(
        string transactionsRoot,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IRenameMutationFileSystem mutationFileSystem,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        if (string.IsNullOrWhiteSpace(transactionsRoot))
            throw new ArgumentException("Transaction root is required.", nameof(transactionsRoot));
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(mutationFileSystem);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        var stopwatch = Stopwatch.StartNew();
        var recoveryResults = new List<TransactionRecoveryResult>();
        var issues = new List<TransactionIssue>();

        var initial = TransactionStartupDiscovery.Scan(
            transactionsRoot,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(initial.Issues);

        if (initial.GateState == TransactionStartupGateState.Clear)
        {
            stopwatch.Stop();
            return new(
                TransactionStartupRecoveryCoordinatorState.ClearNoAction,
                initial,
                initial,
                Array.Empty<TransactionRecoveryResult>(),
                issues.ToArray(),
                stopwatch.Elapsed);
        }

        // Fail closed at the catalog level. If any transaction is manual-only or actively owned by
        // another session, do not mutate even otherwise-recoverable transactions during this startup.
        // This keeps startup behavior easy to reason about and prevents partial catalog recovery while
        // a higher-priority safety condition is unresolved.
        if (initial.GateState is TransactionStartupGateState.ManualRequired
            or TransactionStartupGateState.SessionBusy)
        {
            var state = initial.GateState == TransactionStartupGateState.ManualRequired
                ? TransactionStartupRecoveryCoordinatorState.ManualRequired
                : TransactionStartupRecoveryCoordinatorState.BlockedSessionBusy;
            stopwatch.Stop();
            return new(
                state,
                initial,
                initial,
                Array.Empty<TransactionRecoveryResult>(),
                issues.ToArray(),
                stopwatch.Elapsed);
        }

        // RecoveryRequired is the only state allowed to cross a startup mutation boundary. Discovery
        // already proved that every blocking candidate is auto-rollback eligible, but Recover() still
        // re-analyzes under the transaction's single-writer lease before making any mutation.
        foreach (var candidate in initial.Candidates
                     .Where(x => x.Disposition == TransactionStartupDisposition.RecoveryRequired)
                     .OrderBy(x => x.TransactionId))
        {
            TransactionRecoveryResult recovery;
            try
            {
                recovery = TransactionRecoveryOrchestrator.Recover(
                    candidate.TransactionDirectory,
                    fileSystem,
                    semanticsProvider,
                    identityProvider,
                    mutationFileSystem,
                    exactNamespaceInspector);
            }
            catch (Exception ex)
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Error,
                    "STARTUP_RECOVERY_ORCHESTRATION_FAILED",
                    $"启动自动恢复异常：{ex.GetType().Name}: {ex.Message}",
                    Path: candidate.TransactionDirectory));
                break;
            }

            recoveryResults.Add(recovery);
            issues.AddRange(recovery.Issues);

            if (!recovery.Success)
            {
                // Do not continue mutating additional transactions after an unexpected recovery result.
                break;
            }
        }

        var final = TransactionStartupDiscovery.Scan(
            transactionsRoot,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(final.Issues);

        var finalState = final.GateState switch
        {
            TransactionStartupGateState.Clear when recoveryResults.Count > 0
                && recoveryResults.All(x => x.Success)
                => TransactionStartupRecoveryCoordinatorState.AutoRecoveryCompleted,
            TransactionStartupGateState.Clear
                => TransactionStartupRecoveryCoordinatorState.ClearNoAction,
            TransactionStartupGateState.SessionBusy
                => TransactionStartupRecoveryCoordinatorState.BlockedSessionBusy,
            TransactionStartupGateState.ManualRequired
                => TransactionStartupRecoveryCoordinatorState.ManualRequired,
            _ => TransactionStartupRecoveryCoordinatorState.RecoveryIncomplete,
        };

        stopwatch.Stop();
        return new(
            finalState,
            initial,
            final,
            recoveryResults.ToArray(),
            issues.ToArray(),
            stopwatch.Elapsed);
    }
}
