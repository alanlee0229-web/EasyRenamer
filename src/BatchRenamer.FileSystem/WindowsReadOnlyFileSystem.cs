using BatchRenamer.Core;

namespace BatchRenamer.FileSystem;

/// <summary>
/// Read-only namespace probe used by ValidationEngine / RenamePlanner.
/// File.Exists/Directory.Exists intentionally swallow several access errors and return false, which
/// would misclassify "cannot inspect" as "missing". GetAttributes lets us preserve that distinction.
/// </summary>
public sealed class WindowsReadOnlyFileSystem : IReadOnlyFileSystem
{
    public FileSystemEntryKind GetEntryKind(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Directory)
                ? FileSystemEntryKind.Directory
                : FileSystemEntryKind.File;
        }
        catch (FileNotFoundException)
        {
            return FileSystemEntryKind.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return FileSystemEntryKind.Missing;
        }
        catch (UnauthorizedAccessException)
        {
            return FileSystemEntryKind.Other;
        }
        catch (IOException)
        {
            // Sharing, provider, network or other namespace errors are not equivalent to "missing".
            return FileSystemEntryKind.Other;
        }
        catch
        {
            return FileSystemEntryKind.Other;
        }
    }
}
