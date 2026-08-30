using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// Live transaction journal sink bound to one already-persisted plan.json. The plan file is held
/// under a read-only lease that denies write/delete sharing for the lifetime of the live mutation
/// session. This prevents plan replacement between INTENT/DONE events while still allowing readers.
/// The plan is validated once, then each append avoids re-reading/deserializing a large plan.json.
/// </summary>
public sealed class PlanBoundTransactionJournalSink : ITransactionJournalSink, IDisposable
{
    private readonly string _transactionDirectory;
    private readonly RenamePlan _persistedPlan;
    private readonly FileStream _planLease;
    private FileStream? _journalAppendStream;
    private bool _disposed;

    public PlanBoundTransactionJournalSink(string transactionDirectory, RenamePlan expectedPlan)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(expectedPlan);

        _transactionDirectory = Path.GetFullPath(transactionDirectory);
        var planPath = Path.Combine(_transactionDirectory, RenamePlanPersistence.PlanFileName);
        FileStream? lease = null;
        try
        {
            // Open the lease before parsing so plan.json cannot be swapped between validation and the
            // first mutation. FileShare.Read permits recovery/audit readers but denies write/delete.
            lease = new FileStream(
                planPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1,
                FileOptions.SequentialScan);

            var load = RenamePlanPersistence.Load(planPath);
            if (!load.Success || load.Plan is not { } persistedPlan)
            {
                var detail = string.Join(" | ", load.Issues.Select(x => $"{x.Code}: {x.Message}"));
                throw new TransactionDurabilityException(
                    "JOURNAL_BOUND_PLAN_UNAVAILABLE",
                    null,
                    null,
                    null,
                    null,
                    string.IsNullOrWhiteSpace(detail) ? "Unable to load persisted plan.json." : detail);
            }

            if (!Equivalent(expectedPlan, persistedPlan))
            {
                throw new TransactionDurabilityException(
                    "JOURNAL_BOUND_PLAN_MISMATCH",
                    null,
                    null,
                    null,
                    null,
                    "The in-memory RenamePlan does not exactly match the persisted plan.json.");
            }

            _persistedPlan = persistedPlan;
            _planLease = lease;
            lease = null;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    public TransactionJournalAppendResult Append(string transactionDirectory, TransactionJournalEvent journalEvent)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PlanBoundTransactionJournalSink));
        var requestedDirectory = Path.GetFullPath(transactionDirectory);
        if (!string.Equals(requestedDirectory, _transactionDirectory, StringComparison.Ordinal))
        {
            var journalPath = Path.Combine(requestedDirectory, TransactionJournal.JournalFileName);
            return new(
                false,
                journalPath,
                null,
                [new TransactionIssue(
                    ValidationSeverity.Error,
                    "JOURNAL_BOUND_DIRECTORY_MISMATCH",
                    "Plan-bound journal sink cannot be reused for a different transaction directory.",
                    journalEvent.Ordinal,
                    journalEvent.ItemId,
                    journalPath)]);
        }

        try
        {
            _journalAppendStream ??= OpenJournalAppendStream();
            return TransactionJournal.AppendBound(
                _transactionDirectory,
                _persistedPlan,
                journalEvent,
                _journalAppendStream);
        }
        catch (Exception ex)
        {
            var journalPath = Path.Combine(_transactionDirectory, TransactionJournal.JournalFileName);
            return new(
                false,
                journalPath,
                null,
                [new TransactionIssue(
                    ValidationSeverity.Error,
                    "JOURNAL_APPEND_STREAM_FAILED",
                    $"Unable to open/maintain the live journal append stream: {ex.GetType().Name}: {ex.Message}",
                    journalEvent.Ordinal,
                    journalEvent.ItemId,
                    journalPath)]);
        }
    }

    private FileStream OpenJournalAppendStream()
    {
        var journalPath = Path.Combine(_transactionDirectory, TransactionJournal.JournalFileName);
        var stream = new FileStream(
            journalPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.WriteThrough | FileOptions.SequentialScan);
        stream.Seek(0, SeekOrigin.End);
        return stream;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _journalAppendStream?.Dispose();
        _planLease.Dispose();
    }

    private static bool Equivalent(RenamePlan expected, RenamePlan actual)
    {
        if (expected.TransactionId != actual.TransactionId
            || expected.CreatedAt != actual.CreatedAt
            || expected.SchemaVersion != actual.SchemaVersion
            || expected.DirectorySemantics.Count != actual.DirectorySemantics.Count
            || expected.Entries.Count != actual.Entries.Count)
            return false;

        for (var i = 0; i < expected.DirectorySemantics.Count; i++)
        {
            if (expected.DirectorySemantics[i] != actual.DirectorySemantics[i]) return false;
        }

        for (var i = 0; i < expected.Entries.Count; i++)
        {
            if (expected.Entries[i] != actual.Entries[i]) return false;
        }

        return true;
    }
}
