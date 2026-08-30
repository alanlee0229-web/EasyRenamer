namespace BatchRenamer.Transaction;

/// <summary>
/// Centralized transaction metadata location. User namespace entries are never stored here.
/// </summary>
public static class TransactionStoragePaths
{
    public static string GetDefaultTransactionsRoot()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BatchRenamer",
            "transactions");
}
