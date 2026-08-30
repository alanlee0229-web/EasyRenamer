using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// Durable mutation boundary for V0.7.1. Every namespace mutation is wrapped by a persisted
/// INTENT event before the move and a persisted DONE event after the move returns successfully.
/// If INTENT cannot be durably appended, the underlying filesystem mutation is never called.
/// If DONE cannot be durably appended after a successful move, an exception is raised so the
/// existing executor reconciles the real filesystem state and requires recovery.
/// </summary>
public sealed class JournaledRenameMutationFileSystem : IRenameMutationFileSystem, IDisposable
{
    private readonly RenamePlan _plan;
    private readonly string _transactionDirectory;
    private readonly IRenameMutationFileSystem _inner;
    private readonly ITransactionJournalSink _journalSink;
    private readonly IDisposable? _ownedJournalSink;
    private readonly IReadOnlyDictionary<string, RenamePlanDirectorySemantics> _semanticsByDirectory;
    private readonly IReadOnlyDictionary<TransitionKey, FrozenTransition> _exactTransitions;
    private readonly ISet<TransitionKey> _ambiguousExactTransitions;
    private readonly IReadOnlyDictionary<TransitionKey, FrozenTransition> _semanticTransitions;
    private readonly ISet<TransitionKey> _ambiguousSemanticTransitions;
    private bool _disposed;

    public JournaledRenameMutationFileSystem(
        RenamePlan plan,
        string transactionDirectory,
        IRenameMutationFileSystem inner,
        ITransactionJournalSink? journalSink = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(inner);

        var integrity = RenamePlanIntegrity.Validate(plan);
        if (integrity.Any(x => x.Severity == ValidationSeverity.Error))
            throw new ArgumentException("RenamePlan integrity validation failed.", nameof(plan));

        _plan = plan;
        _transactionDirectory = Path.GetFullPath(transactionDirectory);
        _inner = inner;
        if (journalSink is null)
        {
            var bound = new PlanBoundTransactionJournalSink(_transactionDirectory, plan);
            _journalSink = bound;
            _ownedJournalSink = bound;
        }
        else
        {
            _journalSink = journalSink;
        }
        _semanticsByDirectory = plan.DirectorySemantics.ToDictionary(
            x => RenamePlanIntegrity.NormalizeFullPath(x.DirectoryPath),
            StringComparer.Ordinal);

        // Build the transition lookup exactly once. The previous implementation scanned all
        // 4 * N frozen transitions for every single namespace move. At 20,000 items that turns
        // 40,000 Execute moves into billions of transition comparisons/allocations and makes the
        // release stress gate appear hung. These immutable indexes preserve the same exact-first,
        // semantics-fallback rules while making normal lookup O(1).
        var indexes = BuildTransitionIndexes();
        _exactTransitions = indexes.Exact;
        _ambiguousExactTransitions = indexes.AmbiguousExact;
        _semanticTransitions = indexes.Semantic;
        _ambiguousSemanticTransitions = indexes.AmbiguousSemantic;
    }

    public void MoveFileNoOverwrite(string sourcePath, string destinationPath)
        => Move(sourcePath, destinationPath, isDirectory: false);

    public void MoveDirectoryNoOverwrite(string sourcePath, string destinationPath)
        => Move(sourcePath, destinationPath, isDirectory: true);

    private void Move(string sourcePath, string destinationPath, bool isDirectory)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JournaledRenameMutationFileSystem));
        var transition = ResolveTransition(sourcePath, destinationPath, isDirectory);
        AppendOrThrow(transition.Entry, TransactionJournalEventKind.Intent, transition.Operation, "JOURNAL_INTENT_NOT_DURABLE");

        if (isDirectory)
            _inner.MoveDirectoryNoOverwrite(sourcePath, destinationPath);
        else
            _inner.MoveFileNoOverwrite(sourcePath, destinationPath);

        AppendOrThrow(transition.Entry, TransactionJournalEventKind.Done, transition.Operation, "JOURNAL_DONE_NOT_DURABLE");
    }

    private void AppendOrThrow(
        RenamePlanEntry entry,
        TransactionJournalEventKind kind,
        TransactionJournalOperation operation,
        string code)
    {
        var journalEvent = TransactionJournal.Create(_plan, entry, kind, operation);
        var result = _journalSink.Append(_transactionDirectory, journalEvent);
        if (result.Success) return;

        var detail = string.Join(" | ", result.Issues.Select(x => $"{x.Code}: {x.Message}"));
        throw new TransactionDurabilityException(
            code,
            kind,
            operation,
            entry.Ordinal,
            entry.ItemId,
            string.IsNullOrWhiteSpace(detail) ? "事务日志写入失败。" : detail);
    }

    private FrozenTransition ResolveTransition(string sourcePath, string destinationPath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            throw new TransactionDurabilityException(
                "JOURNAL_TRANSITION_PATH_INVALID",
                null,
                null,
                null,
                null,
                "Mutation source/destination path cannot be empty.");

        var from = RenamePlanIntegrity.NormalizeFullPath(sourcePath);
        var to = RenamePlanIntegrity.NormalizeFullPath(destinationPath);
        var exactKey = new TransitionKey(isDirectory, from, to);

        // Exact-path matching comes first. This is essential for case-only rename where Source and
        // Target are equal under IgnoreCase but represent different intended namespace spellings.
        if (_ambiguousExactTransitions.Contains(exactKey))
            throw TransitionAmbiguous(sourcePath, destinationPath);
        if (_exactTransitions.TryGetValue(exactKey, out var exact))
            return exact;

        var directory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(from) ?? string.Empty);
        if (!_semanticsByDirectory.TryGetValue(directory, out var semantics))
        {
            throw new TransactionDurabilityException(
                "JOURNAL_TRANSITION_NOT_IN_PLAN",
                null,
                null,
                null,
                null,
                $"Mutation is not a frozen RenamePlan transition: '{sourcePath}' -> '{destinationPath}'.");
        }

        var semanticKey = new TransitionKey(
            isDirectory,
            SemanticPathKey(from, semantics),
            SemanticPathKey(to, semantics));
        if (_ambiguousSemanticTransitions.Contains(semanticKey))
            throw TransitionAmbiguous(sourcePath, destinationPath);
        if (_semanticTransitions.TryGetValue(semanticKey, out var semantic))
            return semantic;

        throw new TransactionDurabilityException(
            "JOURNAL_TRANSITION_NOT_IN_PLAN",
            null,
            null,
            null,
            null,
            $"Mutation is not a frozen RenamePlan transition: '{sourcePath}' -> '{destinationPath}'.");
    }

    private (
        IReadOnlyDictionary<TransitionKey, FrozenTransition> Exact,
        ISet<TransitionKey> AmbiguousExact,
        IReadOnlyDictionary<TransitionKey, FrozenTransition> Semantic,
        ISet<TransitionKey> AmbiguousSemantic)
        BuildTransitionIndexes()
    {
        var exact = new Dictionary<TransitionKey, FrozenTransition>();
        var ambiguousExact = new HashSet<TransitionKey>();
        var semantic = new Dictionary<TransitionKey, FrozenTransition>();
        var ambiguousSemantic = new HashSet<TransitionKey>();

        foreach (var transition in EnumerateTransitions())
        {
            AddIndexedTransition(
                exact,
                ambiguousExact,
                new TransitionKey(transition.Entry.IsDirectory, transition.FromPath, transition.ToPath),
                transition);

            var sourceDirectory = RenamePlanIntegrity.NormalizeFullPath(
                Path.GetDirectoryName(transition.Entry.SourcePath) ?? string.Empty);
            if (!_semanticsByDirectory.TryGetValue(sourceDirectory, out var semantics))
                continue;

            // Preserve the historical safety rule: IgnoreCase fallback is never used for any
            // transition belonging to a case-only rename. Exact spelling must identify those moves.
            var isCaseOnly = !semantics.IsCaseSensitive
                             && string.Equals(
                                 transition.Entry.SourcePath,
                                 transition.Entry.TargetPath,
                                 StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(
                                 transition.Entry.SourcePath,
                                 transition.Entry.TargetPath,
                                 StringComparison.Ordinal);
            if (isCaseOnly) continue;

            AddIndexedTransition(
                semantic,
                ambiguousSemantic,
                new TransitionKey(
                    transition.Entry.IsDirectory,
                    SemanticPathKey(transition.FromPath, semantics),
                    SemanticPathKey(transition.ToPath, semantics)),
                transition);
        }

        return (exact, ambiguousExact, semantic, ambiguousSemantic);
    }

    private IEnumerable<FrozenTransition> EnumerateTransitions()
    {
        foreach (var entry in _plan.Entries)
        {
            yield return new(
                entry,
                RenamePlanIntegrity.NormalizeFullPath(entry.SourcePath),
                RenamePlanIntegrity.NormalizeFullPath(entry.TemporaryPath),
                TransactionJournalOperation.Phase1SourceToTemp);
            yield return new(
                entry,
                RenamePlanIntegrity.NormalizeFullPath(entry.TemporaryPath),
                RenamePlanIntegrity.NormalizeFullPath(entry.TargetPath),
                TransactionJournalOperation.Phase2TempToTarget);
            yield return new(
                entry,
                RenamePlanIntegrity.NormalizeFullPath(entry.TargetPath),
                RenamePlanIntegrity.NormalizeFullPath(entry.TemporaryPath),
                TransactionJournalOperation.RollbackTargetToTemp);
            yield return new(
                entry,
                RenamePlanIntegrity.NormalizeFullPath(entry.TemporaryPath),
                RenamePlanIntegrity.NormalizeFullPath(entry.SourcePath),
                TransactionJournalOperation.RollbackTempToSource);
        }
    }

    private static void AddIndexedTransition(
        IDictionary<TransitionKey, FrozenTransition> index,
        ISet<TransitionKey> ambiguous,
        TransitionKey key,
        FrozenTransition transition)
    {
        if (ambiguous.Contains(key)) return;
        if (index.TryAdd(key, transition)) return;
        index.Remove(key);
        ambiguous.Add(key);
    }

    private static string SemanticPathKey(string normalizedPath, RenamePlanDirectorySemantics semantics)
        => semantics.IsCaseSensitive ? normalizedPath : normalizedPath.ToUpperInvariant();

    private static TransactionDurabilityException TransitionAmbiguous(string sourcePath, string destinationPath)
        => new(
            "JOURNAL_TRANSITION_AMBIGUOUS",
            null,
            null,
            null,
            null,
            $"Mutation maps to multiple frozen RenamePlan transitions: '{sourcePath}' -> '{destinationPath}'.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownedJournalSink?.Dispose();
    }

    private readonly record struct TransitionKey(bool IsDirectory, string FromPath, string ToPath);

    private sealed record FrozenTransition(
        RenamePlanEntry Entry,
        string FromPath,
        string ToPath,
        TransactionJournalOperation Operation);
}
