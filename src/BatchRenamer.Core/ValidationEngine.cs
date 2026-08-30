using System.Diagnostics;

namespace BatchRenamer.Core;

/// <summary>
/// Rev.A V1 validation engine. It is read-only: it may inspect the filesystem but never mutates it.
/// UI code must consume structured issue codes instead of implementing conflict rules itself.
/// </summary>
public static class ValidationEngine
{
    private static readonly char[] WindowsInvalidFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> ReservedDeviceNames = BuildReservedDeviceNames();

    public static ValidationBatchResult Validate(
        IReadOnlyList<ValidationInputItem> items,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var issuesById = items.ToDictionary(x => x.Id, _ => new List<ValidationIssue>());
        var semanticsCache = new Dictionary<string, PathSemantics>(StringComparer.Ordinal);

        PathSemantics Semantics(string directory)
        {
            if (semanticsCache.TryGetValue(directory, out var cached)) return cached;
            var value = semanticsProvider.GetSemantics(directory);
            semanticsCache[directory] = value;
            return value;
        }

        // Pass 1: per-item filename/source validation.
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsIncluded) continue;
            var list = issuesById[item.Id];
            var semantics = Semantics(item.ParentDirectory);

            if (!semantics.IsReliable && !item.IsSynthetic)
            {
                list.Add(Warning("FILESYSTEM_SEMANTICS_UNKNOWN", "无法完全确认该目录的大小写/文件系统语义，当前预览采用保守判断。"));
            }

            ValidateName(item, semantics, list);

            if (!item.IsSynthetic)
            {
                var sourceKind = fileSystem.GetEntryKind(item.CurrentPath);
                if (sourceKind == FileSystemEntryKind.Missing)
                {
                    list.Add(Error("SOURCE_MISSING", "源文件或文件夹已不存在。"));
                }
                else if (sourceKind == FileSystemEntryKind.Other)
                {
                    list.Add(Error("PERMISSION_ERROR", "无法可靠访问源对象，请检查权限或路径状态。"));
                }
                else
                {
                    var expectedKind = item.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
                    if (sourceKind != expectedKind)
                    {
                        list.Add(Error("SOURCE_KIND_CHANGED", "源路径当前的对象类型已变化（文件/文件夹不一致），请重新导入。"));
                    }
                    else
                    {
                        var actualIdentity = identityProvider.TryGetIdentity(item.CurrentPath, item.IsDirectory);
                        if (item.ExpectedFileIdentity is { } expected)
                        {
                            if (actualIdentity is null)
                                list.Add(Error("SOURCE_IDENTITY_UNVERIFIABLE", "此前已记录源对象身份，但当前无法重新确认 FileIdentity；为避免降级执行，请刷新后重试。"));
                            else if (expected != actualIdentity.Value)
                                list.Add(Error("SOURCE_IDENTITY_CHANGED", "源路径当前指向的对象已发生变化，请重新导入或刷新。"));
                        }
                    }
                }
            }
        }

        // Only syntactically valid, included, changed rows can vacate their current namespace in Phase 1.
        // Namespace keys are normalized according to the provider-reported directory semantics; this
        // keeps the common 20k-item path O(n) instead of pairwise O(n²).
        var vacatingSourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!item.IsIncluded || HasError(issuesById[item.Id])) continue;
            var semantics = Semantics(item.ParentDirectory);
            if (!string.Equals(item.CurrentName, item.ProposedName, StringComparison.Ordinal))
                vacatingSourceKeys.Add(NamespaceKey(item.ParentDirectory, item.CurrentName, semantics));
        }

        // Pass 2: duplicate targets using each target directory's actual name semantics.
        var targetGroups = new Dictionary<string, List<ValidationInputItem>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsIncluded || HasError(issuesById[item.Id])) continue;
            var semantics = Semantics(item.ParentDirectory);
            var key = NamespaceKey(item.ParentDirectory, item.ProposedName, semantics);
            if (!targetGroups.TryGetValue(key, out var group)) targetGroups[key] = group = [];
            group.Add(item);
        }

        foreach (var group in targetGroups.Values.Where(x => x.Count > 1))
        {
            foreach (var item in group)
                AddUnique(issuesById[item.Id], Error("DUPLICATE_TARGET", $"多个项目将生成同一目标名称“{item.ProposedName}”。"));
        }

        // Pass 3: target occupancy. A↔B / cycles are allowed only when the occupant is in VacatingSourceSet.
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsIncluded || HasError(issuesById[item.Id])) continue;
            if (string.Equals(item.CurrentName, item.ProposedName, StringComparison.Ordinal)) continue;

            var semantics = Semantics(item.ParentDirectory);
            var targetPath = Path.Combine(item.ParentDirectory, item.ProposedName);

            // Case-only rename in a case-insensitive directory is self-occupancy, not an external conflict.
            if (!semantics.IsCaseSensitive && semantics.NameComparer.Equals(item.CurrentName, item.ProposedName))
                continue;

            if (item.IsSynthetic) continue;
            var targetKind = fileSystem.GetEntryKind(targetPath);
            if (targetKind == FileSystemEntryKind.Missing) continue;
            if (targetKind == FileSystemEntryKind.Other)
            {
                issuesById[item.Id].Add(Error("PERMISSION_ERROR", "无法可靠检查目标路径占用状态，请检查权限或路径状态。"));
                continue;
            }

            var occupantWillVacate = vacatingSourceKeys.Contains(
                NamespaceKey(item.ParentDirectory, item.ProposedName, semantics));

            if (!occupantWillVacate)
                issuesById[item.Id].Add(Error("TARGET_EXISTS", $"目标“{item.ProposedName}”已被不会由本次操作腾空的对象占用。"));
        }

        // Pass 4: V1 parent/child restriction for folder renames.
        foreach (var parent in items.Where(x => x.IsDirectory && x.IsIncluded))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(parent.CurrentName, parent.ProposedName, StringComparison.Ordinal)) continue;
            var parentFull = NormalizePath(parent.CurrentPath);
            var prefix = parentFull.EndsWith(Path.DirectorySeparatorChar)
                ? parentFull
                : parentFull + Path.DirectorySeparatorChar;

            foreach (var child in items)
            {
                if (child.Id == parent.Id) continue;
                var childFull = NormalizePath(child.CurrentPath);
                if (!childFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                AddUnique(issuesById[parent.Id], Error("PARENT_CHILD_CONFLICT", "待改名文件夹内部仍有其他已导入对象；V1 暂不执行父子路径联动重命名。"));
                AddUnique(issuesById[child.Id], Error("PARENT_CHILD_CONFLICT", "该对象位于一个同时待改名的已导入文件夹内部；V1 暂不执行父子路径联动重命名。"));
            }
        }

        var results = items.Select(item => new ValidationItemResult(item.Id, issuesById[item.Id].ToArray())).ToArray();
        stopwatch.Stop();
        return new ValidationBatchResult(
            results,
            results.Count(x => x.HasError),
            results.Count(x => !x.HasError && x.HasWarning),
            stopwatch.Elapsed);
    }

    private static void ValidateName(ValidationInputItem item, PathSemantics semantics, List<ValidationIssue> issues)
    {
        var name = item.ProposedName;
        var mainName = item.IsDirectory
            ? name
            : (!string.IsNullOrEmpty(item.Extension) && name.EndsWith(item.Extension, StringComparison.OrdinalIgnoreCase)
                ? name[..^item.Extension.Length]
                : Path.GetFileNameWithoutExtension(name));
        if (string.IsNullOrWhiteSpace(mainName))
        {
            issues.Add(Error("EMPTY_NAME", "目标主名称不能为空。扩展名本身不能作为完整名称。"));
            return;
        }

        if (name.Any(ch => ch < 32) || name.IndexOfAny(WindowsInvalidFileNameChars) >= 0)
            issues.Add(Error("INVALID_CHARACTER", "目标名称包含 Windows 不允许的字符。"));

        if (name.EndsWith(' ')) issues.Add(Error("TRAILING_SPACE", "Windows 文件名不能以空格结尾。"));
        if (name.EndsWith('.')) issues.Add(Error("TRAILING_DOT", "Windows 文件名不能以句点结尾。"));

        var deviceStem = name.Split('.')[0].TrimEnd(' ', '.');
        if (ReservedDeviceNames.Contains(deviceStem))
            issues.Add(Error("RESERVED_NAME", $"“{deviceStem}”是 Windows 保留设备名称。"));

        if (semantics.MaxComponentLength is { } maxComponent && name.Length > maxComponent)
            issues.Add(Error("NAME_TOO_LONG", $"目标名称长度超过当前文件系统允许的 {maxComponent} 个字符。"));

        var targetPath = Path.Combine(item.ParentDirectory, name);
        if (semantics.MaxPathLength is { } maxPath && targetPath.Length > maxPath)
            issues.Add(Error("PATH_TOO_LONG", $"目标完整路径超过当前环境允许的 {maxPath} 个字符。"));
    }

    private static string NamespaceKey(string parentDirectory, string name, PathSemantics semantics)
    {
        var parent = NormalizePath(parentDirectory);
        if (!semantics.IsCaseSensitive)
        {
            parent = parent.ToUpperInvariant();
            name = name.ToUpperInvariant();
        }
        return parent + "\u001F" + name;
    }

    private static bool SameDirectory(string a, string b, PathSemantics semantics)
        => string.Equals(NormalizePath(a), NormalizePath(b), semantics.NameComparison);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool HasError(List<ValidationIssue> issues)
        => issues.Any(x => x.Severity == ValidationSeverity.Error);

    private static void AddUnique(List<ValidationIssue> issues, ValidationIssue issue)
    {
        if (!issues.Any(x => x.Code == issue.Code)) issues.Add(issue);
    }

    private static ValidationIssue Error(string code, string message) => new(ValidationSeverity.Error, code, message);
    private static ValidationIssue Warning(string code, string message) => new(ValidationSeverity.Warning, code, message);

    private static HashSet<string> BuildReservedDeviceNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
        for (var i = 1; i <= 9; i++)
        {
            result.Add($"COM{i}");
            result.Add($"LPT{i}");
        }
        // Windows also recognizes superscript 1/2/3 in COM/LPT device names.
        foreach (var digit in new[] { '¹', '²', '³' })
        {
            result.Add($"COM{digit}");
            result.Add($"LPT{digit}");
        }
        return result;
    }
}
