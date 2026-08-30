using System.IO;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.9 process-wide transaction catalog lease. Per-transaction leases prevent two sessions from
/// mutating the same TransactionId; this root lease additionally prevents two BatchRenamer instances
/// from starting/undoing different real transactions at the same time.
/// </summary>
public sealed class TransactionCatalogLease : IDisposable
{
    public const string LockFileName = ".catalog.lock";

    private FileStream? _stream;

    private TransactionCatalogLease(string transactionsRoot, string lockPath, FileStream stream)
    {
        TransactionsRoot = transactionsRoot;
        LockPath = lockPath;
        _stream = stream;
    }

    public string TransactionsRoot { get; }
    public string LockPath { get; }

    public static TransactionCatalogLeaseAcquireResult TryAcquire(string transactionsRoot)
    {
        if (string.IsNullOrWhiteSpace(transactionsRoot))
            throw new ArgumentException("Transaction root is required.", nameof(transactionsRoot));

        var root = Path.GetFullPath(transactionsRoot);
        var lockPath = Path.Combine(root, LockFileName);
        try
        {
            Directory.CreateDirectory(root);
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.WriteThrough);
            return new(true, new TransactionCatalogLease(root, lockPath, stream), Array.Empty<TransactionIssue>());
        }
        catch (IOException ex)
        {
            return new(false, null,
            [
                new TransactionIssue(
                    ValidationSeverity.Error,
                    "TRANSACTION_CATALOG_BUSY",
                    $"另一个 BatchRenamer 会话正在执行事务操作：{ex.Message}",
                    Path: lockPath),
            ]);
        }
        catch (Exception ex)
        {
            return new(false, null,
            [
                new TransactionIssue(
                    ValidationSeverity.Error,
                    "TRANSACTION_CATALOG_LOCK_FAILED",
                    $"无法获取事务目录全局锁：{ex.GetType().Name}: {ex.Message}",
                    Path: lockPath),
            ]);
        }
    }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        stream?.Dispose();
    }
}

public sealed record TransactionCatalogLeaseAcquireResult(
    bool Success,
    TransactionCatalogLease? Lease,
    IReadOnlyList<TransactionIssue> Issues);
