using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.10 conservative metadata retention for long-lived desktop use.
/// It only removes BatchRenamer-owned transaction metadata directories after the transaction has
/// reached a safe terminal/prepared classification. User Source/Temp/Target namespace is never
/// mutated by this service.
/// </summary>
public static class TransactionRetentionService
{
    public const int DefaultMaxTerminalTransactions = 20;
    public static readonly TimeSpan DefaultPreparedRetention = TimeSpan.FromDays(2);

    private static readonly HashSet<string> KnownMetadataFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        RenamePlanPersistence.PlanFileName,
        TransactionJournal.JournalFileName,
        TransactionStateStore.StateFileName,
        TransactionSessionLease.LockFileName,
    };

    public static TransactionRetentionResult Cleanup(
        string transactionsRoot,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector,
        TransactionRetentionPolicy? policy = null,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(transactionsRoot))
            throw new ArgumentException("Transaction root is required.", nameof(transactionsRoot));
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        policy ??= TransactionRetentionPolicy.Default;
        if (policy.MaxTerminalTransactions < 1)
            throw new ArgumentOutOfRangeException(nameof(policy), "At least one terminal transaction must be retained.");
        if (policy.PreparedRetention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(policy), "Prepared retention cannot be negative.");

        var stopwatch = Stopwatch.StartNew();
        var root = Path.GetFullPath(transactionsRoot);
        var issues = new List<TransactionIssue>();
        var decisions = new List<TransactionRetentionDecision>();
        var utcNow = now ?? DateTimeOffset.UtcNow;

        if (!Directory.Exists(root))
        {
            stopwatch.Stop();
            return new(root, true, 0, 0, Array.Empty<TransactionRetentionDecision>(), Array.Empty<TransactionIssue>(), stopwatch.Elapsed);
        }

        var catalogLeaseResult = TransactionCatalogLease.TryAcquire(root);
        issues.AddRange(catalogLeaseResult.Issues);
        var catalogLease = catalogLeaseResult.Lease;
        if (!catalogLeaseResult.Success || catalogLease is null)
        {
            stopwatch.Stop();
            return new(root, false, 0, 0, decisions.ToArray(), issues.ToArray(), stopwatch.Elapsed);
        }

        var deleted = 0;
        var kept = 0;
        using (catalogLease)
        {
            var history = TransactionHistoryService.Scan(
            root,
            fileSystem,
            semanticsProvider,
            identityProvider,
            exactNamespaceInspector);
        issues.AddRange(history.Issues.Where(x => x.Severity == ValidationSeverity.Error));

        // HistoryService is already newest-first. Keep approximately the latest 20 terminal records,
        // matching the frozen V1 contract, and always retain every non-terminal/manual record.
        var terminal = history.Entries
            .Where(x => x.Status is TransactionHistoryStatus.Completed or TransactionHistoryStatus.Undone)
            .ToArray();
        var terminalKeep = terminal
            .Take(policy.MaxTerminalTransactions)
            .Select(x => x.TransactionId)
            .ToHashSet();
        // The UI exposes only the newest safe Undo candidate. Older completed transactions may also
        // be individually reversible in isolation, but they are not the active V1 Undo contract and
        // must not defeat the bounded ~20-record retention policy.
        var newestUndoableId = history.Entries.FirstOrDefault(x => x.CanUndo)?.TransactionId;

        var candidateIds = new HashSet<Guid>();
        foreach (var entry in history.Entries)
        {
            if (entry.Status == TransactionHistoryStatus.Prepared)
            {
                if (entry.CreatedAt is { } created && utcNow - created >= policy.PreparedRetention)
                    candidateIds.Add(entry.TransactionId);
                continue;
            }

            if (entry.Status is TransactionHistoryStatus.Completed or TransactionHistoryStatus.Undone)
            {
                if (!terminalKeep.Contains(entry.TransactionId) && entry.TransactionId != newestUndoableId)
                    candidateIds.Add(entry.TransactionId);
            }
        }

            foreach (var entry in history.Entries)
        {
            if (!candidateIds.Contains(entry.TransactionId))
            {
                kept++;
                decisions.Add(new(entry.TransactionId, entry.TransactionDirectory, entry.Status, TransactionRetentionDisposition.Kept, null));
                continue;
            }

            var decision = TryDeleteCandidate(
                root,
                entry,
                fileSystem,
                semanticsProvider,
                identityProvider,
                exactNamespaceInspector);
            decisions.Add(decision);
            if (decision.Disposition == TransactionRetentionDisposition.Deleted)
                deleted++;
            else
                kept++;

            if (decision.Issue is { } issue)
                issues.Add(issue);
        }
        }

        stopwatch.Stop();
        return new(
            root,
            issues.All(x => x.Severity != ValidationSeverity.Error),
            deleted,
            kept,
            decisions.ToArray(),
            issues.ToArray(),
            stopwatch.Elapsed);
    }

    private static TransactionRetentionDecision TryDeleteCandidate(
        string transactionsRoot,
        TransactionHistoryEntry entry,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        var directory = Path.GetFullPath(entry.TransactionDirectory);
        if (!IsDirectTransactionChild(transactionsRoot, directory, entry.TransactionId))
        {
            return Skip(entry, "RETENTION_DIRECTORY_OUTSIDE_ROOT", "事务保留清理拒绝处理不属于事务根目录的路径。", ValidationSeverity.Error);
        }

        var leaseResult = TransactionSessionLease.TryAcquire(directory);
        var sessionLease = leaseResult.Lease;
        if (!leaseResult.Success || sessionLease is null)
        {
            var busy = leaseResult.Issues.Any(x => x.Code == "TRANSACTION_SESSION_BUSY");
            return Skip(
                entry,
                busy ? "RETENTION_TRANSACTION_BUSY" : "RETENTION_TRANSACTION_LEASE_FAILED",
                busy ? "事务当前仍被其他会话占用，本次保留不清理。" : "无法验证事务单写 lease，本次保留不清理。",
                busy ? ValidationSeverity.Info : ValidationSeverity.Warning);
        }

        using (sessionLease)
        {
            // Reconcile immediately before metadata deletion. A candidate that has become interrupted,
            // externally modified or ambiguous since the history scan is never removed.
            var analysis = TransactionRecoveryAnalyzer.Analyze(
                directory,
                fileSystem,
                semanticsProvider,
                identityProvider,
                exactNamespaceInspector);

            var journalCount = analysis.Journal?.Events.Count ?? 0;
            var checkpointPhase = analysis.Checkpoint?.Checkpoint?.Phase;
            var checkpointClaimsMutation = checkpointPhase is TransactionCheckpointPhase.Phase1InProgress
                or TransactionCheckpointPhase.Phase1Applied
                or TransactionCheckpointPhase.Phase2InProgress
                or TransactionCheckpointPhase.Completed
                or TransactionCheckpointPhase.RollbackInProgress
                or TransactionCheckpointPhase.RolledBack
                or TransactionCheckpointPhase.RecoveryRequired
                or TransactionCheckpointPhase.Ambiguous;
            // HistoryService only labels a no-journal directory Prepared when it has no durable/observed
            // BatchRenamer mutation evidence. The original Source may have been externally deleted later;
            // that must not turn an abandoned dry-run plan into permanent retained metadata.
            var safePrepared = entry.Status == TransactionHistoryStatus.Prepared
                               && journalCount == 0
                               && !checkpointClaimsMutation;
            var safeTerminal = entry.Status == TransactionHistoryStatus.Completed
                ? analysis.State == TransactionRecoveryState.Completed
                : entry.Status == TransactionHistoryStatus.Undone
                  && analysis.State == TransactionRecoveryState.RolledBack;

            if (!safePrepared && !safeTerminal)
            {
                return Skip(entry, "RETENTION_STATE_CHANGED", "事务状态在清理前已变化，本次保留完整事务目录。", ValidationSeverity.Warning);
            }

            var unknown = FindUnknownMetadataEntries(directory);
            if (unknown.Count > 0)
            {
                return Skip(
                    entry,
                    "RETENTION_UNKNOWN_METADATA_PRESENT",
                    $"事务目录包含未知内容，本次拒绝自动清理：{string.Join(", ", unknown.Select(Path.GetFileName))}",
                    ValidationSeverity.Warning);
            }
        }

        // session.lock lives inside the transaction directory, so release its handle before deleting.
        // The global catalog lease remains held for the entire cleanup pass; all normal Execute/Undo
        // commands honor that lease. Deletion is metadata-only and never touches Source/Temp/Target.
        try
        {
            DeleteKnownMetadataOnly(directory);
            if (Directory.Exists(directory))
            {
                if (Directory.EnumerateFileSystemEntries(directory).Any())
                    return Skip(entry, "RETENTION_DIRECTORY_NOT_EMPTY", "清理已停止：事务目录出现新的未知内容。", ValidationSeverity.Warning);
                Directory.Delete(directory, recursive: false);
            }

            return new(entry.TransactionId, directory, entry.Status, TransactionRetentionDisposition.Deleted, null);
        }
        catch (Exception ex)
        {
            return Skip(
                entry,
                "RETENTION_DELETE_FAILED",
                $"事务元数据清理失败，已保留剩余内容：{ex.GetType().Name}: {ex.Message}",
                ValidationSeverity.Warning);
        }
    }

    private static bool IsDirectTransactionChild(string root, string directory, Guid transactionId)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var parent = Path.GetDirectoryName(directory);
        if (string.IsNullOrWhiteSpace(parent)) return false;
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        if (!string.Equals(normalizedRoot, normalizedParent, StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(new DirectoryInfo(directory).Name, transactionId.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> FindUnknownMetadataEntries(string directory)
    {
        var unknown = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
        {
            if (Directory.Exists(path))
            {
                unknown.Add(path);
                continue;
            }

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
            {
                unknown.Add(path);
                continue;
            }

            if (KnownMetadataFileNames.Contains(name)) continue;
            if (name.StartsWith($".{RenamePlanPersistence.PlanFileName}.tmp-", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith($".{TransactionStateStore.StateFileName}.tmp-", StringComparison.OrdinalIgnoreCase)) continue;
            unknown.Add(path);
        }
        return unknown;
    }

    private static void DeleteKnownMetadataOnly(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) continue;

            var known = KnownMetadataFileNames.Contains(name)
                        || name.StartsWith($".{RenamePlanPersistence.PlanFileName}.tmp-", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith($".{TransactionStateStore.StateFileName}.tmp-", StringComparison.OrdinalIgnoreCase);
            if (!known) continue;
            File.Delete(path);
        }
    }

    private static TransactionRetentionDecision Skip(
        TransactionHistoryEntry entry,
        string code,
        string message,
        ValidationSeverity severity)
    {
        var issue = new TransactionIssue(severity, code, message, Path: entry.TransactionDirectory);
        var disposition = code == "RETENTION_TRANSACTION_BUSY"
            ? TransactionRetentionDisposition.SkippedBusy
            : TransactionRetentionDisposition.SkippedSafety;
        return new(entry.TransactionId, entry.TransactionDirectory, entry.Status, disposition, issue);
    }
}

public sealed record TransactionRetentionPolicy(int MaxTerminalTransactions, TimeSpan PreparedRetention)
{
    public static TransactionRetentionPolicy Default { get; } = new(
        TransactionRetentionService.DefaultMaxTerminalTransactions,
        TransactionRetentionService.DefaultPreparedRetention);
}

public enum TransactionRetentionDisposition
{
    Kept,
    Deleted,
    SkippedBusy,
    SkippedSafety,
}

public sealed record TransactionRetentionDecision(
    Guid TransactionId,
    string TransactionDirectory,
    TransactionHistoryStatus PreviousStatus,
    TransactionRetentionDisposition Disposition,
    TransactionIssue? Issue);

public sealed record TransactionRetentionResult(
    string TransactionsRoot,
    bool Success,
    int DeletedCount,
    int KeptCount,
    IReadOnlyList<TransactionRetentionDecision> Decisions,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime);
