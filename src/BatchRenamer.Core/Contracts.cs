using System.Text.Json.Serialization;

namespace BatchRenamer.Core;

/// <summary>
/// Core domain contracts frozen by Rev.A. Namespace identity (where an entry lives) and file
/// identity (which underlying object occupies that entry) are intentionally separate concepts.
/// </summary>
public sealed record RenameItem(
    Guid Id,
    string CurrentPath,
    string ParentDirectory,
    string CurrentName,
    string Stem,
    string Extension,
    bool IsDirectory,
    bool IsIncluded);

public readonly record struct NamespaceIdentity(string FullPath);

public readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex)
{
    public override string ToString() => $"{VolumeSerialNumber:X8}:{FileIndex:X16}";
}

public sealed record SequenceConfig(
    bool Enabled,
    int Start,
    int Step,
    int Padding,
    SequencePosition Position,
    string Separator);

public enum SequencePosition
{
    BeforeName,
    AfterName,
}

public sealed record RenameRuleSet(
    string BaseName,
    OriginalNameMode OriginalNameMode,
    string Prefix,
    string Suffix,
    string LiteralSearch,
    string LiteralReplacement,
    NameCaseMode CaseMode,
    SequenceConfig Sequence);

public enum OriginalNameMode
{
    None,
    BeforeBaseName,
    AfterBaseName,
}

public enum NameCaseMode
{
    Unchanged,
    Lower,
    Upper,
    TitleCaseWords,
}

public sealed record RenamePreview(
    Guid ItemId,
    string CurrentPath,
    string ProposedName,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message);

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ValidationItemResult(
    Guid ItemId,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasError => Issues.Any(x => x.Severity == ValidationSeverity.Error);
    public bool HasWarning => Issues.Any(x => x.Severity == ValidationSeverity.Warning);
    public ValidationIssue? PrimaryIssue => Issues.FirstOrDefault(x => x.Severity == ValidationSeverity.Error)
                                              ?? Issues.FirstOrDefault(x => x.Severity == ValidationSeverity.Warning)
                                              ?? Issues.FirstOrDefault();
}

public sealed record ValidationBatchResult(
    IReadOnlyList<ValidationItemResult> Items,
    int ErrorItemCount,
    int WarningItemCount,
    TimeSpan ComputeTime);

public sealed record PathSemantics(
    bool IsCaseSensitive,
    bool IsReliable,
    int? MaxComponentLength,
    int? MaxPathLength,
    string Source)
{
    public StringComparer NameComparer => IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    public StringComparison NameComparison => IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}

public enum FileSystemEntryKind
{
    Missing,
    File,
    Directory,
    Other,
}

/// <summary>Read-only filesystem surface used by ValidationEngine. No rename/delete/write APIs belong here.</summary>
public interface IReadOnlyFileSystem
{
    FileSystemEntryKind GetEntryKind(string path);
}

public interface IPathSemanticsProvider
{
    PathSemantics GetSemantics(string directoryPath);
}

public interface IFileIdentityProvider
{
    FileIdentity? TryGetIdentity(string path, bool isDirectory);
}

public sealed record ValidationInputItem(
    Guid Id,
    string CurrentPath,
    string ParentDirectory,
    string CurrentName,
    string Extension,
    string ProposedName,
    bool IsDirectory,
    bool IsIncluded,
    bool IsSynthetic,
    FileIdentity? ExpectedFileIdentity);

public sealed record RenamePlan(
    Guid TransactionId,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    IReadOnlyList<RenamePlanDirectorySemantics> DirectorySemantics,
    IReadOnlyList<RenamePlanEntry> Entries)
{
    [JsonIgnore]
    public int RenameCount => Entries.Count;
}

/// <summary>
/// Snapshot of the path-comparison semantics used when the plan was built. TransactionEngine may
/// re-read the directory before execution and reject the plan if semantics changed materially.
/// </summary>
public sealed record RenamePlanDirectorySemantics(
    string DirectoryPath,
    bool IsCaseSensitive,
    bool IsReliable,
    int? MaxComponentLength,
    int? MaxPathLength,
    string Source);

/// <summary>
/// Frozen input for TransactionEngine. It deliberately contains no RenameRuleSet or UI state:
/// execution must never reinterpret how the target name was generated.
/// </summary>
public sealed record RenamePlanEntry(
    int Ordinal,
    Guid ItemId,
    string SourcePath,
    string TemporaryPath,
    string TargetPath,
    bool IsDirectory,
    FileIdentity? ExpectedFileIdentity);

public sealed record RenamePlannerIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    Guid? ItemId = null);

public sealed record RenamePlanBuildResult(
    RenamePlan? Plan,
    ValidationBatchResult FinalValidation,
    IReadOnlyList<RenamePlannerIssue> PlannerIssues,
    TimeSpan ComputeTime)
{
    public bool Success => Plan is not null
                           && FinalValidation.ErrorItemCount == 0
                           && PlannerIssues.All(x => x.Severity != ValidationSeverity.Error);
}
