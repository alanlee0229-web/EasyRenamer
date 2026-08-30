namespace BatchRenamer.Transaction;

/// <summary>
/// Production namespace mutation adapter. It intentionally exposes no overwrite and no delete API.
/// The protocol decides whether the move is Source -> Temp, Temp -> Target, Target -> Temp, or
/// Temp -> Source; this adapter only performs the requested no-overwrite same-volume namespace move.
/// </summary>
public sealed class SystemRenameMutationFileSystem : IRenameMutationFileSystem
{
    public void MoveFileNoOverwrite(string sourcePath, string destinationPath)
        => File.Move(sourcePath, destinationPath, overwrite: false);

    public void MoveDirectoryNoOverwrite(string sourcePath, string destinationPath)
        => Directory.Move(sourcePath, destinationPath);
}
