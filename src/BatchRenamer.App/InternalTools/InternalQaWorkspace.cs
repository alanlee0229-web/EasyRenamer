using System.IO;

namespace BatchRenamer.App;

public sealed class InternalQaWorkspace
{
    private const string MarkerFileName = ".easyrenamer-internal-qa-owned";
    private const string MarkerContent = "EasyRenamer.InternalQA.Workspace.v1";

    public InternalQaWorkspace()
    {
        RootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BatchRenamer", "InternalQA"));
    }

    public string RootPath { get; }
    public string MarkerPath => Path.Combine(RootPath, MarkerFileName);

    public string EnsureWorkspace()
    {
        ValidateFixedRoot();
        if (Directory.Exists(RootPath))
        {
            ValidateOwnershipMarker();
            return RootPath;
        }

        Directory.CreateDirectory(RootPath);
        File.WriteAllText(MarkerPath, MarkerContent);
        return RootPath;
    }

    public IReadOnlyList<string> CreateQuickSmokeFiles()
    {
        ResetWorkspace();
        var quickRoot = Path.Combine(RootPath, "QuickSmoke");
        Directory.CreateDirectory(quickRoot);

        var names = new[]
        {
            "IMG_2.JPG",
            "IMG_10.JPG",
            "report draft.txt",
            "Report Final.txt",
            "photo (copy).png",
            "2026-08 invoice.pdf",
            "notes.md",
            "case-test.TXT",
        };

        var paths = new List<string>(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            var path = Path.Combine(quickRoot, names[i]);
            File.WriteAllText(path, $"EasyRenamer Internal QA fixture {i + 1:D2}\n");
            paths.Add(path);
        }

        return paths;
    }

    public void ResetWorkspace()
    {
        if (Directory.Exists(RootPath)) CleanupWorkspace();
        EnsureWorkspace();
    }

    public void CleanupWorkspace()
    {
        ValidateFixedRoot();
        if (!Directory.Exists(RootPath)) return;
        ValidateOwnershipMarker();
        Directory.Delete(RootPath, recursive: true);
    }

    public bool OwnsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var prefix = RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void ValidateFixedRoot()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BatchRenamer", "InternalQA"));
        if (!string.Equals(RootPath, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Internal QA workspace root is not the fixed sandbox path.");

        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        if (!RootPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Internal QA workspace must remain under the system TEMP directory.");

        var productRoot = Path.Combine(Path.GetTempPath(), "BatchRenamer");
        RejectReparsePoint(productRoot);
        RejectReparsePoint(RootPath);
    }

    private void ValidateOwnershipMarker()
    {
        if (!File.Exists(MarkerPath)
            || !string.Equals(File.ReadAllText(MarkerPath), MarkerContent, StringComparison.Ordinal))
            throw new InvalidOperationException("Workspace ownership marker is missing or invalid; cleanup was refused.");
    }

    private static void RejectReparsePoint(string path)
    {
        if (Directory.Exists(path)
            && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Internal QA workspace cannot use a reparse point.");
    }
}
