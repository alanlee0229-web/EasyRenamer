using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

public sealed record TransactionIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    int? Ordinal = null,
    Guid? ItemId = null,
    string? Path = null);

public sealed record RenamePlanPersistenceResult(
    bool Success,
    string? TransactionDirectory,
    string? PlanPath,
    string? Sha256,
    RenamePlan? PersistedPlan,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public sealed record RenamePlanLoadResult(
    bool Success,
    string PlanPath,
    string? Sha256,
    RenamePlan? Plan,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public sealed record TransactionPreflightResult(
    bool CanExecute,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public int ErrorCount => Issues.Count(x => x.Severity == ValidationSeverity.Error);
    public int WarningCount => Issues.Count(x => x.Severity == ValidationSeverity.Warning);
}

/// <summary>
/// Narrow namespace mutation surface shared by the two-phase executor and rollback foundation.
/// No overwrite or delete API exists on this interface.
/// </summary>
public interface IRenameMutationFileSystem
{
    void MoveFileNoOverwrite(string sourcePath, string destinationPath);
    void MoveDirectoryNoOverwrite(string sourcePath, string destinationPath);
}

/// <summary>
/// Read-only exact namespace inspection used only where Windows case-insensitive lookup cannot tell
/// whether a case-only rename currently has the Source spelling or the Target spelling.
/// </summary>
public interface IExactNamespaceInspector
{
    string? TryGetActualPath(string requestedPath, bool isDirectory, bool caseSensitive);
}

public enum Phase1ExecutionState
{
    NotStarted,
    Completed,
    FailedPartial,
}

public sealed record Phase1AppliedEntry(
    int Ordinal,
    Guid ItemId,
    string SourcePath,
    string TemporaryPath,
    bool IsDirectory,
    FileIdentity? ExpectedFileIdentity);

public sealed record TransactionPhase1Result(
    Phase1ExecutionState State,
    TransactionPreflightResult Preflight,
    IReadOnlyList<Phase1AppliedEntry> AppliedEntries,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => State == Phase1ExecutionState.Completed
                           && Issues.All(x => x.Severity != ValidationSeverity.Error);
    public bool HasMutation => AppliedEntries.Count > 0;
    public bool RequiresRecovery => State == Phase1ExecutionState.FailedPartial;
}

public enum Phase2ExecutionState
{
    NotStarted,
    Completed,
    FailedPartial,
}

public sealed record Phase2AppliedEntry(
    int Ordinal,
    Guid ItemId,
    string TemporaryPath,
    string TargetPath,
    bool IsDirectory,
    FileIdentity? ExpectedFileIdentity);

/// <summary>
/// V0.6-D result. Any unsuccessful Phase-2 attempt starts from a fully applied Phase-1 state, so the
/// caller must treat it as requiring rollback/recovery even when zero Temp -> Target moves succeeded.
/// </summary>
public sealed record TransactionPhase2Result(
    Phase2ExecutionState State,
    TransactionPreflightResult Preflight,
    IReadOnlyList<Phase2AppliedEntry> AppliedEntries,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => State == Phase2ExecutionState.Completed
                           && Issues.All(x => x.Severity != ValidationSeverity.Error);
    public bool HasFinalMutation => AppliedEntries.Count > 0;
    public bool RequiresRecovery => !Success;
}

public enum RollbackExecutionState
{
    Completed,
    FailedPartial,
    Ambiguous,
}

public sealed record RollbackAppliedMove(
    int Ordinal,
    Guid ItemId,
    string FromPath,
    string ToPath,
    bool IsDirectory,
    string Stage);

/// <summary>
/// V0.6-E rollback foundation. It is intentionally filesystem-state driven and idempotent: a second
/// invocation after successful restoration must observe every item at Source and make zero moves.
/// </summary>
public sealed record TransactionRollbackResult(
    RollbackExecutionState State,
    IReadOnlyList<RollbackAppliedMove> AppliedMoves,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => State == RollbackExecutionState.Completed
                           && Issues.All(x => x.Severity != ValidationSeverity.Error);
    public bool HasMutation => AppliedMoves.Count > 0;
    public bool RequiresManualRecovery => State != RollbackExecutionState.Completed;
}

public enum TransactionJournalEventKind
{
    Intent,
    Done,
}

public enum TransactionJournalOperation
{
    Phase1SourceToTemp,
    Phase2TempToTarget,
    RollbackTargetToTemp,
    RollbackTempToSource,
}

public sealed record TransactionJournalEvent(
    int SchemaVersion,
    Guid EventId,
    Guid TransactionId,
    DateTimeOffset Timestamp,
    TransactionJournalEventKind Kind,
    TransactionJournalOperation Operation,
    int Ordinal,
    Guid ItemId);

public sealed record TransactionJournalAppendResult(
    bool Success,
    string JournalPath,
    TransactionJournalEvent? Event,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public sealed record TransactionJournalLoadResult(
    bool Success,
    string JournalPath,
    IReadOnlyList<TransactionJournalEvent> Events,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public enum TransactionCheckpointPhase
{
    Prepared,
    Phase1InProgress,
    Phase1Applied,
    Phase2InProgress,
    Completed,
    RollbackInProgress,
    RolledBack,
    RecoveryRequired,
    Ambiguous,
}

public sealed record TransactionStateCheckpoint(
    int SchemaVersion,
    Guid TransactionId,
    DateTimeOffset UpdatedAt,
    TransactionCheckpointPhase Phase,
    int? LastCompletedOrdinal,
    string? Note);

public sealed record TransactionStateWriteResult(
    bool Success,
    string StatePath,
    TransactionStateCheckpoint? Checkpoint,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public sealed record TransactionStateLoadResult(
    bool Success,
    string StatePath,
    TransactionStateCheckpoint? Checkpoint,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public enum RecoveryEntryState
{
    NotStarted,
    Phase1Applied,
    Phase2Applied,
    ExternallyModified,
    Ambiguous,
}

public sealed record TransactionRecoveryEntry(
    int Ordinal,
    Guid ItemId,
    RecoveryEntryState State,
    string? ObservedPath,
    string? ReasonCode);

public enum TransactionRecoveryState
{
    NotStarted,
    Phase1InProgress,
    Phase1Applied,
    Phase2InProgress,
    Completed,
    RollbackInProgress,
    RolledBack,
    ExternallyModified,
    Ambiguous,
}

public sealed record TransactionRecoveryAnalysis(
    TransactionRecoveryState State,
    RenamePlan? Plan,
    TransactionJournalLoadResult? Journal,
    TransactionStateLoadResult? Checkpoint,
    IReadOnlyList<TransactionRecoveryEntry> Entries,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool RequiresRecoveryAction => State is TransactionRecoveryState.Phase1InProgress
        or TransactionRecoveryState.Phase1Applied
        or TransactionRecoveryState.Phase2InProgress
        or TransactionRecoveryState.RollbackInProgress;

    public bool CanAutoRollback => RequiresRecoveryAction
                                   && !Issues.Any(x => x.Severity == ValidationSeverity.Error)
                                   && Entries.All(x => x.State is RecoveryEntryState.NotStarted
                                                      or RecoveryEntryState.Phase1Applied
                                                      or RecoveryEntryState.Phase2Applied);
}

/// <summary>
/// Injectable journal sink used by the durable mutation wrapper. Production uses
/// SystemTransactionJournalSink; tests can inject deterministic append failures without mutating
/// TransactionJournal itself.
/// </summary>
public interface ITransactionJournalSink
{
    TransactionJournalAppendResult Append(string transactionDirectory, TransactionJournalEvent journalEvent);
}

public sealed class SystemTransactionJournalSink : ITransactionJournalSink
{
    public static SystemTransactionJournalSink Instance { get; } = new();

    private SystemTransactionJournalSink() { }

    public TransactionJournalAppendResult Append(string transactionDirectory, TransactionJournalEvent journalEvent)
        => TransactionJournal.Append(transactionDirectory, journalEvent);
}

public sealed class TransactionDurabilityException : IOException
{
    public TransactionDurabilityException(
        string code,
        TransactionJournalEventKind? eventKind,
        TransactionJournalOperation? operation,
        int? ordinal,
        Guid? itemId,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        EventKind = eventKind;
        Operation = operation;
        Ordinal = ordinal;
        ItemId = itemId;
    }

    public string Code { get; }
    public TransactionJournalEventKind? EventKind { get; }
    public TransactionJournalOperation? Operation { get; }
    public int? Ordinal { get; }
    public Guid? ItemId { get; }
}

public enum TransactionExecutionOverallState
{
    NotStarted,
    SessionBusy,
    RejectedByRecoveryState,
    FailedBeforeMutation,
    Completed,
    RecoveryRequired,
}

public sealed record TransactionExecutionResult(
    TransactionExecutionOverallState State,
    RenamePlan? Plan,
    TransactionPhase1Result? Phase1,
    TransactionPhase2Result? Phase2,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => State == TransactionExecutionOverallState.Completed;
    public bool RequiresRecovery => State == TransactionExecutionOverallState.RecoveryRequired;
}

public enum TransactionRecoveryAction
{
    SessionBusy,
    NoActionNotStarted,
    NoActionCompleted,
    NoActionRolledBack,
    AutoRollbackCompleted,
    ManualRequired,
}

public sealed record TransactionRecoveryResult(
    TransactionRecoveryAction Action,
    TransactionRecoveryAnalysis InitialAnalysis,
    TransactionRollbackResult? Rollback,
    TransactionRecoveryAnalysis? FinalAnalysis,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => Action is TransactionRecoveryAction.NoActionNotStarted
        or TransactionRecoveryAction.NoActionCompleted
        or TransactionRecoveryAction.NoActionRolledBack
        or TransactionRecoveryAction.AutoRollbackCompleted;
    public bool RequiresManualRecovery => Action == TransactionRecoveryAction.ManualRequired;
}

public sealed record TransactionSessionLeaseAcquireResult(
    bool Success,
    TransactionSessionLease? Lease,
    string LockPath,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public enum TransactionStartupDisposition
{
    NotStarted,
    Completed,
    RolledBack,
    RecoveryRequired,
    SessionBusy,
    ManualRequired,
}

public enum TransactionStartupGateState
{
    Clear,
    RecoveryRequired,
    SessionBusy,
    ManualRequired,
}

public sealed record TransactionStartupCandidate(
    Guid TransactionId,
    string TransactionDirectory,
    TransactionStartupDisposition Disposition,
    TransactionRecoveryAnalysis? Analysis,
    IReadOnlyList<TransactionIssue> Issues)
{
    public bool BlocksNewTransaction => Disposition is TransactionStartupDisposition.RecoveryRequired
        or TransactionStartupDisposition.SessionBusy
        or TransactionStartupDisposition.ManualRequired;
}

public sealed record TransactionStartupDiscoveryResult(
    TransactionStartupGateState GateState,
    string TransactionsRoot,
    IReadOnlyList<TransactionStartupCandidate> Candidates,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool CanStartNewTransaction => GateState == TransactionStartupGateState.Clear;
    public int RecoveryRequiredCount => Candidates.Count(x => x.Disposition == TransactionStartupDisposition.RecoveryRequired);
    public int ManualRequiredCount => Candidates.Count(x => x.Disposition == TransactionStartupDisposition.ManualRequired);
    public int SessionBusyCount => Candidates.Count(x => x.Disposition == TransactionStartupDisposition.SessionBusy);
}

public enum TransactionStartupRecoveryCoordinatorState
{
    ClearNoAction,
    AutoRecoveryCompleted,
    BlockedSessionBusy,
    ManualRequired,
    RecoveryIncomplete,
}

public sealed record TransactionStartupRecoveryCoordinatorResult(
    TransactionStartupRecoveryCoordinatorState State,
    TransactionStartupDiscoveryResult InitialDiscovery,
    TransactionStartupDiscoveryResult FinalDiscovery,
    IReadOnlyList<TransactionRecoveryResult> RecoveryResults,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool CanStartNewTransaction => FinalDiscovery.CanStartNewTransaction;
    public int AutoRecoveredCount => RecoveryResults.Count(x => x.Action == TransactionRecoveryAction.AutoRollbackCompleted);
    public bool PerformedRecoveryMutation => RecoveryResults.Any(x => x.Rollback?.HasMutation == true);
}

public enum TransactionHistoryStatus
{
    Prepared,
    Completed,
    Undone,
    Interrupted,
    ExternallyModified,
    SessionBusy,
    ManualRequired,
}

public sealed record TransactionHistoryEntry(
    Guid TransactionId,
    string TransactionDirectory,
    DateTimeOffset? CreatedAt,
    int EntryCount,
    TransactionHistoryStatus Status,
    bool CanUndo,
    bool IsBestEffort,
    TransactionRecoveryAnalysis? Analysis,
    IReadOnlyList<TransactionIssue> Issues);

public sealed record TransactionHistoryResult(
    string TransactionsRoot,
    IReadOnlyList<TransactionHistoryEntry> Entries,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public int UndoableCount => Entries.Count(x => x.CanUndo);
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
}

public enum TransactionUndoState
{
    Completed,
    AlreadyUndone,
    NotEligible,
    SessionBusy,
    FailedNoMutation,
    RecoveryRequired,
    ManualRequired,
}

public sealed record TransactionUndoResult(
    TransactionUndoState State,
    RenamePlan? Plan,
    TransactionRecoveryAnalysis? InitialAnalysis,
    TransactionRollbackResult? Rollback,
    TransactionRecoveryAnalysis? FinalAnalysis,
    IReadOnlyList<TransactionIssue> Issues,
    TimeSpan ComputeTime)
{
    public bool Success => State is TransactionUndoState.Completed or TransactionUndoState.AlreadyUndone;
    public bool HasMutation => Rollback?.HasMutation == true;
    public bool RequiresRecovery => State == TransactionUndoState.RecoveryRequired;
    public bool RequiresManualRecovery => State == TransactionUndoState.ManualRequired;
}

