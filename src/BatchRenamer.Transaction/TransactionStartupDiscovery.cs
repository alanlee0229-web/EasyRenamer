using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.7.2 startup discovery and recovery gate. It scans BatchRenamer-owned transaction metadata,
/// reconciles every valid transaction directory against the current filesystem, and decides whether
/// starting a new transaction is safe. It never performs Source/Temp/Target namespace mutation.
/// </summary>
public static class TransactionStartupDiscovery
{
    public static TransactionStartupDiscoveryResult Scan(
        string transactionsRoot,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        if (string.IsNullOrWhiteSpace(transactionsRoot))
            throw new ArgumentException("Transaction root is required.", nameof(transactionsRoot));
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        var stopwatch = Stopwatch.StartNew();
        var root = Path.GetFullPath(transactionsRoot);
        var candidates = new List<TransactionStartupCandidate>();
        var issues = new List<TransactionIssue>();

        if (!Directory.Exists(root))
        {
            stopwatch.Stop();
            return new(
                TransactionStartupGateState.Clear,
                root,
                Array.Empty<TransactionStartupCandidate>(),
                Array.Empty<TransactionIssue>(),
                stopwatch.Elapsed);
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "STARTUP_TRANSACTION_ROOT_ENUMERATION_FAILED",
                $"无法枚举事务目录：{ex.GetType().Name}: {ex.Message}",
                Path: root));
            stopwatch.Stop();
            return new(
                TransactionStartupGateState.ManualRequired,
                root,
                Array.Empty<TransactionStartupCandidate>(),
                issues.ToArray(),
                stopwatch.Elapsed);
        }

        foreach (var transactionDirectory in directories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.GetFullPath(transactionDirectory);
            var directoryName = new DirectoryInfo(directory).Name;
            if (!Guid.TryParseExact(directoryName, "N", out var directoryTransactionId))
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Warning,
                    "STARTUP_UNKNOWN_DIRECTORY_IGNORED",
                    "事务根目录中存在非 TransactionId 目录；Startup Gate 已忽略该目录。",
                    Path: directory));
                continue;
            }

            // Acquire the same single-writer lease used by Execute/Recover while taking the snapshot.
            // This creates/opens only BatchRenamer-owned session.lock metadata; it never touches user
            // Source/Temp/Target namespace. If another process owns the lease, this transaction is live
            // and startup must not infer a crash state from a moving filesystem.
            var leaseResult = TransactionSessionLease.TryAcquire(directory);
            var lease = leaseResult.Lease;
            if (!leaseResult.Success || lease is null)
            {
                var busy = leaseResult.Issues.Any(x => x.Code == "TRANSACTION_SESSION_BUSY");
                var disposition = busy
                    ? TransactionStartupDisposition.SessionBusy
                    : TransactionStartupDisposition.ManualRequired;
                var candidateIssues = leaseResult.Issues.ToArray();
                candidates.Add(new(
                    directoryTransactionId,
                    directory,
                    disposition,
                    null,
                    candidateIssues));
                issues.AddRange(candidateIssues);
                continue;
            }

            using (lease)
            {
                TransactionRecoveryAnalysis analysis;
                try
                {
                    analysis = TransactionRecoveryAnalyzer.Analyze(
                        directory,
                        fileSystem,
                        semanticsProvider,
                        identityProvider,
                        exactNamespaceInspector);
                }
                catch (Exception ex)
                {
                    var candidateIssues = new[]
                    {
                        new TransactionIssue(
                            ValidationSeverity.Error,
                            "STARTUP_TRANSACTION_ANALYSIS_FAILED",
                            $"Startup Recovery Analysis 异常：{ex.GetType().Name}: {ex.Message}",
                            Path: directory),
                    };
                    candidates.Add(new(
                        directoryTransactionId,
                        directory,
                        TransactionStartupDisposition.ManualRequired,
                        null,
                        candidateIssues));
                    issues.AddRange(candidateIssues);
                    continue;
                }

                var localIssues = analysis.Issues.ToList();
                if (analysis.Plan is { } plan && plan.TransactionId != directoryTransactionId)
                {
                    localIssues.Add(new TransactionIssue(
                        ValidationSeverity.Error,
                        "STARTUP_TRANSACTION_DIRECTORY_MISMATCH",
                        "事务目录名称与 plan.json TransactionId 不一致。",
                        Path: directory));
                }

                var disposition = Classify(analysis, localIssues);
                var candidate = new TransactionStartupCandidate(
                    directoryTransactionId,
                    directory,
                    disposition,
                    analysis,
                    localIssues.ToArray());
                candidates.Add(candidate);
                issues.AddRange(localIssues);
            }
        }

        var gateState = ComputeGateState(candidates);
        stopwatch.Stop();
        return new(gateState, root, candidates.ToArray(), issues.ToArray(), stopwatch.Elapsed);
    }

    private static TransactionStartupDisposition Classify(
        TransactionRecoveryAnalysis analysis,
        IReadOnlyList<TransactionIssue> issues)
    {
        if (analysis.Plan is not { } plan || analysis.Journal is not { Success: true } journal)
            return TransactionStartupDisposition.ManualRequired;

        // Startup is a historical catalog scan, not only an immediate crash analyzer. A transaction
        // that was durably completed/rolled back must not block BatchRenamer forever merely because
        // the user later moved/deleted/edited those files. Trust a terminal checkpoint only when the
        // append-only Journal contains the corresponding durable mutation evidence.
        var checkpoint = analysis.Checkpoint;
        if (checkpoint?.Success == true
            && checkpoint.Checkpoint?.Phase == TransactionCheckpointPhase.Completed
            && HasAllPhase2Done(plan, journal.Events))
        {
            return TransactionStartupDisposition.Completed;
        }

        if (checkpoint?.Success == true
            && checkpoint.Checkpoint?.Phase == TransactionCheckpointPhase.RolledBack
            && journal.Events.Any(x => x.Kind == TransactionJournalEventKind.Done
                                       && (x.Operation == TransactionJournalOperation.RollbackTargetToTemp
                                           || x.Operation == TransactionJournalOperation.RollbackTempToSource)))
        {
            return TransactionStartupDisposition.RolledBack;
        }

        var hasDurableMutationEvidence = journal.Events.Count > 0;
        if (!hasDurableMutationEvidence)
        {
            // Persisted dry-run/prepared plans from V0.6+ are common. With a valid plan and a valid,
            // empty Journal there is no durable evidence that BatchRenamer ever crossed a mutation
            // boundary. External changes to Source after that must not create a fake crash-recovery
            // emergency. However an object already at Temp/Target without Journal evidence violates
            // the durable protocol and is manual-only.
            var checkpointPhase = checkpoint?.Checkpoint?.Phase;
            var checkpointClaimsAppliedMutation = checkpointPhase is TransactionCheckpointPhase.Phase1Applied
                or TransactionCheckpointPhase.Phase2InProgress
                or TransactionCheckpointPhase.Completed
                or TransactionCheckpointPhase.RollbackInProgress
                or TransactionCheckpointPhase.RolledBack
                or TransactionCheckpointPhase.RecoveryRequired
                or TransactionCheckpointPhase.Ambiguous;
            var observedAppliedNamespace = analysis.Entries.Any(x => x.State is RecoveryEntryState.Phase1Applied
                or RecoveryEntryState.Phase2Applied);

            if (!checkpointClaimsAppliedMutation && !observedAppliedNamespace)
                return TransactionStartupDisposition.NotStarted;

            return TransactionStartupDisposition.ManualRequired;
        }

        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
            return TransactionStartupDisposition.ManualRequired;

        return analysis.State switch
        {
            TransactionRecoveryState.NotStarted => TransactionStartupDisposition.NotStarted,
            TransactionRecoveryState.Completed => TransactionStartupDisposition.Completed,
            TransactionRecoveryState.RolledBack => TransactionStartupDisposition.RolledBack,
            TransactionRecoveryState.Phase1InProgress
                or TransactionRecoveryState.Phase1Applied
                or TransactionRecoveryState.Phase2InProgress
                or TransactionRecoveryState.RollbackInProgress
                when analysis.CanAutoRollback => TransactionStartupDisposition.RecoveryRequired,
            _ => TransactionStartupDisposition.ManualRequired,
        };
    }

    private static bool HasAllPhase2Done(RenamePlan plan, IReadOnlyList<TransactionJournalEvent> events)
    {
        if (plan.Entries.Count == 0) return false;
        var doneOrdinals = events
            .Where(x => x.Kind == TransactionJournalEventKind.Done
                        && x.Operation == TransactionJournalOperation.Phase2TempToTarget)
            .Select(x => x.Ordinal)
            .ToHashSet();
        return plan.Entries.All(x => doneOrdinals.Contains(x.Ordinal));
    }

    private static TransactionStartupGateState ComputeGateState(IReadOnlyList<TransactionStartupCandidate> candidates)
    {
        if (candidates.Any(x => x.Disposition == TransactionStartupDisposition.ManualRequired))
            return TransactionStartupGateState.ManualRequired;
        if (candidates.Any(x => x.Disposition == TransactionStartupDisposition.SessionBusy))
            return TransactionStartupGateState.SessionBusy;
        if (candidates.Any(x => x.Disposition == TransactionStartupDisposition.RecoveryRequired))
            return TransactionStartupGateState.RecoveryRequired;
        return TransactionStartupGateState.Clear;
    }
}
