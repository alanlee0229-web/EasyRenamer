using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// Cross-process single-writer lease for one transaction directory. The zero-byte lock file may
/// remain on disk after disposal; ownership is represented only by the live FileShare.None handle,
/// so a process crash automatically releases the lease without requiring stale-lock cleanup.
/// </summary>
public sealed class TransactionSessionLease : IDisposable
{
    public const string LockFileName = "session.lock";

    private readonly FileStream _stream;
    private bool _disposed;

    private TransactionSessionLease(string lockPath, FileStream stream)
    {
        LockPath = lockPath;
        _stream = stream;
    }

    public string LockPath { get; }

    public static TransactionSessionLeaseAcquireResult TryAcquire(string transactionDirectory)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));

        var directory = Path.GetFullPath(transactionDirectory);
        var lockPath = Path.Combine(directory, LockFileName);
        if (!Directory.Exists(directory))
        {
            return new(
                false,
                null,
                lockPath,
                [new TransactionIssue(
                    ValidationSeverity.Error,
                    "TRANSACTION_SESSION_DIRECTORY_MISSING",
                    "事务目录不存在，无法取得执行 lease。",
                    Path: directory)]);
        }

        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
            return new(true, new TransactionSessionLease(lockPath, stream), lockPath, Array.Empty<TransactionIssue>());
        }
        catch (IOException ex)
        {
            return new(
                false,
                null,
                lockPath,
                [new TransactionIssue(
                    ValidationSeverity.Error,
                    "TRANSACTION_SESSION_BUSY",
                    $"同一事务正在被另一个执行/恢复 session 占用：{ex.Message}",
                    Path: lockPath)]);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(
                false,
                null,
                lockPath,
                [new TransactionIssue(
                    ValidationSeverity.Error,
                    "TRANSACTION_SESSION_LOCK_DENIED",
                    $"无法创建/打开事务 session lease：{ex.Message}",
                    Path: lockPath)]);
        }
        catch (Exception ex)
        {
            return new(
                false,
                null,
                lockPath,
                [new TransactionIssue(
                    ValidationSeverity.Error,
                    "TRANSACTION_SESSION_LOCK_FAILED",
                    $"无法取得事务 session lease：{ex.GetType().Name}: {ex.Message}",
                    Path: lockPath)]);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
    }
}
