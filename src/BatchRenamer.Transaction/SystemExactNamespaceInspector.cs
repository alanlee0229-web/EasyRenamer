namespace BatchRenamer.Transaction;

/// <summary>
/// Reads the actual directory entry spelling without mutating the filesystem. This is required for
/// idempotent rollback of case-only renames on case-insensitive Windows directories, where ordinary
/// File.Exists/Directory.Exists calls make Source and Target aliases look simultaneously present.
/// </summary>
public sealed class SystemExactNamespaceInspector : IExactNamespaceInspector
{
    public string? TryGetActualPath(string requestedPath, bool isDirectory, bool caseSensitive)
    {
        try
        {
            var full = Path.GetFullPath(requestedPath);
            var parent = Path.GetDirectoryName(full);
            var requestedName = Path.GetFileName(full);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(requestedName) || !Directory.Exists(parent))
                return null;

            string? ignoreCaseMatch = null;
            foreach (var candidate in Directory.EnumerateFileSystemEntries(parent))
            {
                var name = Path.GetFileName(candidate);
                if (string.Equals(name, requestedName, StringComparison.Ordinal))
                {
                    if (MatchesKind(candidate, isDirectory)) return Path.GetFullPath(candidate);
                    return null;
                }

                if (!caseSensitive
                    && ignoreCaseMatch is null
                    && string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase)
                    && MatchesKind(candidate, isDirectory))
                {
                    ignoreCaseMatch = Path.GetFullPath(candidate);
                }
            }

            return caseSensitive ? null : ignoreCaseMatch;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesKind(string path, bool isDirectory)
        => isDirectory ? Directory.Exists(path) : File.Exists(path);
}
