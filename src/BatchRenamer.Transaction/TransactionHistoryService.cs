using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.8.0 read-only transaction history projection. The service never mutates user Source/Temp/Target
/// namespace. It reconciles persisted history against the current filesystem before advertising Undo.
/// </summary>
public static class TransactionHistoryService
{
    public static TransactionHistoryResult Scan(
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
        var entries = new List<TransactionHistoryEntry>();
        var issues = new List<TransactionIssue>();

        if (!Directory.Exists(root))
        {
            stopwatch.Stop();
            return new(root, Array.Empty<TransactionHistoryEntry>(), Array.Empty<TransactionIssue>(), stopwatch.Elapsed);
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
                "HISTORY_TRANSACTION_ROOT_ENUMERATION_FAILED",
                $"无法枚举事务历史目录：{ex.GetType().Name}: {ex.Message}",
                Path: root));
            stopwatch.Stop();
            return new(root, Array.Empty<TransactionHistoryEntry>(), issues.ToArray(), stopwatch.Elapsed);
        }

        foreach (var transactionDirectory in directories)
        {
            var directory = Path.GetFullPath(transactionDirectory);
            var directoryName = new DirectoryInfo(directory).Name;
            if (!Guid.TryParseExact(directoryName, "N", out var transactionId))
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Warning,
                    "HISTORY_UNKNOWN_DIRECTORY_IGNORED",
                    "事务历史根目录中存在非 TransactionId 目录，已忽略。",
                    Path: directory));
                continue;
            }

            var leaseResult = TransactionSessionLease.TryAcquire(directory);
            if (!leaseResult.Success || leaseResult.Lease is null)
            {
                var candidateIssues = leaseResult.Issues.ToArray();
                issues.AddRange(candidateIssues);
                entries.Add(new(
                    transactionId,
                    directory,
                    TryReadCreatedAt(directory),
                    TryReadEntryCount(directory),
                    leaseResult.Issues.Any(x => x.Code == "TRANSACTION_SESSION_BUSY")
                        ? TransactionHistoryStatus.SessionBusy
                        : TransactionHistoryStatus.ManualRequired,
                    false,
                    false,
                    null,
                    candidateIssues));
                continue;
            }

            using var lease = leaseResult.Lease;
            var analysis = TransactionRecoveryAnalyzer.Analyze(
                directory,
                fileSystem,
                semanticsProvider,
                identityProvider,
                exactNamespaceInspector);
            var entryIssues = analysis.Issues.ToArray();
            issues.AddRange(entryIssues);

            var plan = analysis.Plan;
            var status = MapStatus(analysis, plan);
            var canUndo = status == TransactionHistoryStatus.Completed
                          && plan is not null
                          && entryIssues.All(x => x.Severity != ValidationSeverity.Error)
                          && analysis.Entries.Count == plan.Entries.Count
                          && analysis.Entries.All(x => x.State == RecoveryEntryState.Phase2Applied);
            var bestEffort = plan?.Entries.Any(x => x.ExpectedFileIdentity is null) == true;

            entries.Add(new(
                transactionId,
                directory,
                plan?.CreatedAt ?? TryReadCreatedAt(directory),
                plan?.Entries.Count ?? 0,
                status,
                canUndo,
                bestEffort,
                analysis,
                entryIssues));
        }

        var ordered = entries
            .OrderByDescending(x => x.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.TransactionId)
            .ToArray();
        stopwatch.Stop();
        return new(root, ordered, issues.ToArray(), stopwatch.Elapsed);
    }

    private static TransactionHistoryStatus MapStatus(TransactionRecoveryAnalysis analysis, RenamePlan? plan)
    {
        if (plan is not null && analysis.Journal is { } journal && journal.Events.Count == 0)
        {
            // Persisted plans can exist without any execution attempt (dry-run / prepared plan). A later
            // external deletion of Source must not make that historical metadata look like a failed Undo.
            // Conversely, observing Temp/Target without durable Journal evidence violates the protocol.
            var checkpointPhase = analysis.Checkpoint?.Checkpoint?.Phase;
            var checkpointClaimsMutation = checkpointPhase is TransactionCheckpointPhase.Phase1Applied
                or TransactionCheckpointPhase.Phase2InProgress
                or TransactionCheckpointPhase.Completed
                or TransactionCheckpointPhase.RollbackInProgress
                or TransactionCheckpointPhase.RolledBack
                or TransactionCheckpointPhase.RecoveryRequired
                or TransactionCheckpointPhase.Ambiguous;
            var observedAppliedNamespace = analysis.Entries.Any(x => x.State is RecoveryEntryState.Phase1Applied
                or RecoveryEntryState.Phase2Applied);

            if (!checkpointClaimsMutation && !observedAppliedNamespace)
                return TransactionHistoryStatus.Prepared;
            if (observedAppliedNamespace)
                return TransactionHistoryStatus.ManualRequired;
        }

        return analysis.State switch
        {
            TransactionRecoveryState.NotStarted => TransactionHistoryStatus.Prepared,
            TransactionRecoveryState.Completed => TransactionHistoryStatus.Completed,
            TransactionRecoveryState.RolledBack => TransactionHistoryStatus.Undone,
            TransactionRecoveryState.Phase1InProgress
                or TransactionRecoveryState.Phase1Applied
                or TransactionRecoveryState.Phase2InProgress
                or TransactionRecoveryState.RollbackInProgress => TransactionHistoryStatus.Interrupted,
            TransactionRecoveryState.ExternallyModified => TransactionHistoryStatus.ExternallyModified,
            _ => TransactionHistoryStatus.ManualRequired,
        };
    }

    private static DateTimeOffset? TryReadCreatedAt(string transactionDirectory)
    {
        try
        {
            var planPath = Path.Combine(transactionDirectory, RenamePlanPersistence.PlanFileName);
            var loaded = RenamePlanPersistence.Load(planPath);
            return loaded.Plan?.CreatedAt;
        }
        catch
        {
            return null;
        }
    }

    private static int TryReadEntryCount(string transactionDirectory)
    {
        try
        {
            var planPath = Path.Combine(transactionDirectory, RenamePlanPersistence.PlanFileName);
            var loaded = RenamePlanPersistence.Load(planPath);
            return loaded.Plan?.Entries.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
