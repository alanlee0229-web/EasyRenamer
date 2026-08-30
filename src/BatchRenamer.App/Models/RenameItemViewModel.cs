using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BatchRenamer.Core;

namespace BatchRenamer.App.Models;

public sealed class RenameItemViewModel : INotifyPropertyChanged
{
    private bool _isIncluded = true;
    private PreviewRowState _preview = PreviewRowState.Initial;

    public Guid Id { get; } = Guid.NewGuid();
    public string CurrentPath { get; internal set; } = string.Empty;
    public string ParentDirectory { get; internal set; } = string.Empty;
    public string CurrentName { get; internal set; } = string.Empty;
    public string Stem { get; internal set; } = string.Empty;
    public string Extension { get; internal set; } = string.Empty;
    public bool IsDirectory { get; init; }
    public bool IsSynthetic { get; init; }
    public FileIdentity? ExpectedFileIdentity { get; internal set; }
    public long? SizeBytes { get; internal set; }
    public DateTime? ModifiedTime { get; internal set; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!SetField(ref _isIncluded, value)) return;
            InclusionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Preview is replaced atomically: one PropertyChanged per row.</summary>
    public PreviewRowState Preview
    {
        get => _preview;
        private set => SetField(ref _preview, value);
    }

    public string KindGlyph => IsDirectory ? "▣" : "▤";
    public string SourceFolderDisplay => string.IsNullOrWhiteSpace(ParentDirectory) ? "—" : ParentDirectory;

    /// <summary>
    /// Updates the UI-owned namespace snapshot after a committed Execute/Undo without replacing the
    /// row or its stable ItemId. The transaction layer remains the authority for whether the move
    /// actually completed; this method only reconciles the already-proven result back into WPF state.
    /// </summary>
    public void ApplyNamespaceSnapshot(
        string path,
        FileIdentity? expectedFileIdentity,
        long? sizeBytes,
        DateTime? modifiedTime)
    {
        CurrentPath = path;
        ParentDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        CurrentName = Path.GetFileName(path) ?? string.Empty;
        Extension = IsDirectory ? string.Empty : (Path.GetExtension(path) ?? string.Empty);
        Stem = IsDirectory ? CurrentName : (Path.GetFileNameWithoutExtension(path) ?? string.Empty);
        ExpectedFileIdentity = expectedFileIdentity;
        SizeBytes = sizeBytes;
        ModifiedTime = modifiedTime;

        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(ParentDirectory));
        OnPropertyChanged(nameof(CurrentName));
        OnPropertyChanged(nameof(Stem));
        OnPropertyChanged(nameof(Extension));
        OnPropertyChanged(nameof(ExpectedFileIdentity));
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(ModifiedTime));
        OnPropertyChanged(nameof(SourceFolderDisplay));
    }

    public void MarkCommittedUnchanged(int displayOrder)
    {
        Preview = new PreviewRowState(
            displayOrder,
            CurrentName,
            IsIncluded ? "无变化" : "未参与",
            false,
            false,
            false,
            null,
            string.Empty,
            IsIncluded ? PreviewStatus.Unchanged : PreviewStatus.Excluded);
    }

    public void ApplyPreview(PreviewItemResult result, ValidationItemResult? validation)
    {
        var issue = validation?.PrimaryIssue;
        var hasError = validation?.HasError == true;
        var hasWarning = validation?.HasWarning == true;
        var status = ResolveStatus(result.Status, issue);

        Preview = new PreviewRowState(
            result.DisplayOrder,
            result.NewName,
            status,
            hasError || hasWarning,
            hasError,
            hasWarning,
            issue?.Code,
            issue?.Message ?? string.Empty,
            result.Status);
    }

    public event EventHandler? InclusionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private static string ResolveStatus(PreviewStatus previewStatus, ValidationIssue? issue)
    {
        if (previewStatus == PreviewStatus.Excluded) return "未参与";
        if (issue is not null)
        {
            return issue.Code switch
            {
                "DUPLICATE_TARGET" => "目标重名",
                "TARGET_EXISTS" => "目标已存在",
                "SOURCE_MISSING" => "源已丢失",
                "SOURCE_IDENTITY_CHANGED" => "源已变化",
                "SOURCE_KIND_CHANGED" => "类型已变化",
                "PARENT_CHILD_CONFLICT" => "父子冲突",
                "FILESYSTEM_SEMANTICS_UNKNOWN" => "需确认",
                "PERMISSION_ERROR" => "权限错误",
                "PATH_ERROR" => "路径错误",
                "INVALID_CHARACTER" => "含非法字符",
                "RESERVED_NAME" => "保留名称",
                "EMPTY_NAME" => "名称为空",
                "TRAILING_SPACE" => "末尾空格",
                "TRAILING_DOT" => "末尾句点",
                "NAME_TOO_LONG" => "名称过长",
                "PATH_TOO_LONG" => "路径过长",
                _ => "名称无效",
            };
        }

        return previewStatus == PreviewStatus.Unchanged ? "无变化" : "将改名";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record PreviewRowState(
    int DisplayOrder,
    string NewName,
    string Status,
    bool HasIssue,
    bool HasError,
    bool HasWarning,
    string? IssueCode,
    string IssueMessage,
    PreviewStatus StatusCode)
{
    public bool IsExcluded => StatusCode == PreviewStatus.Excluded;
    public string DisplayNewName => IsExcluded ? "—" : NewName;
    public string StatusToolTip => string.IsNullOrWhiteSpace(IssueMessage) ? Status : $"{Status} · {IssueMessage}";

    public static PreviewRowState Initial { get; } = new(
        0, string.Empty, "就绪", false, false, false, null, string.Empty, PreviewStatus.Ready);
}
