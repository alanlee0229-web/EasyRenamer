using System.Diagnostics;

namespace BatchRenamer.Core;

/// <summary>
/// Pure rename-candidate computation. It never touches the filesystem and intentionally leaves all
/// legality/conflict decisions to ValidationEngine. Safe to run on a worker thread.
/// </summary>
public static class PreviewEngine
{
    public static PreviewBatchResult Build(
        IReadOnlyList<PreviewInputItem> items,
        RenameRuleSet rules,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new PreviewItemResult[items.Count];
        var sequence = rules.Sequence.Start;

        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index];

            if (!item.IsIncluded)
            {
                results[index] = new PreviewItemResult(item.Id, index + 1, item.CurrentName, PreviewStatus.Excluded);
                continue;
            }

            var original = ApplyTextRules(item.Stem, rules);
            var core = BuildCore(original, rules);

            if (rules.Sequence.Enabled)
            {
                var number = sequence.ToString($"D{rules.Sequence.Padding}");
                core = rules.Sequence.Position == SequencePosition.BeforeName
                    ? JoinNonEmpty(number, core, rules.Sequence.Separator)
                    : JoinNonEmpty(core, number, rules.Sequence.Separator);
                sequence += rules.Sequence.Step;
            }

            core = rules.Prefix + core + rules.Suffix;
            core = ApplyCase(core, rules.CaseMode);
            var proposedName = core + item.Extension;

            results[index] = new PreviewItemResult(
                item.Id,
                index + 1,
                proposedName,
                proposedName == item.CurrentName ? PreviewStatus.Unchanged : PreviewStatus.Ready);
        }

        var changed = results.Count(x => x.Status == PreviewStatus.Ready);
        var unchanged = results.Count(x => x.Status == PreviewStatus.Unchanged);
        var included = changed + unchanged;

        stopwatch.Stop();
        return new PreviewBatchResult(results, included, changed, unchanged, stopwatch.Elapsed);
    }

    private static string BuildCore(string original, RenameRuleSet rules)
    {
        var baseName = rules.BaseName.Trim();
        return rules.OriginalNameMode switch
        {
            OriginalNameMode.BeforeBaseName => JoinNonEmpty(original, baseName, rules.Sequence.Separator),
            OriginalNameMode.AfterBaseName => JoinNonEmpty(baseName, original, rules.Sequence.Separator),
            _ => baseName,
        };
    }

    private static string ApplyTextRules(string value, RenameRuleSet rules)
    {
        if (!string.IsNullOrEmpty(rules.LiteralSearch))
            value = value.Replace(rules.LiteralSearch, rules.LiteralReplacement, StringComparison.Ordinal);
        return value;
    }

    private static string ApplyCase(string value, NameCaseMode mode) => mode switch
    {
        NameCaseMode.Lower => value.ToLowerInvariant(),
        NameCaseMode.Upper => value.ToUpperInvariant(),
        NameCaseMode.TitleCaseWords => ToTitleCaseWords(value),
        _ => value,
    };

    /// <summary>
    /// Deterministic filename-oriented title casing. Unlike TextInfo.ToTitleCase, this intentionally
    /// normalizes ALL-CAPS words first, so "HELLO WORLD" becomes "Hello World". Any non-letter/
    /// digit character (space, underscore, hyphen, dot-like punctuation, etc.) starts a new word.
    /// </summary>
    private static string ToTitleCaseWords(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var chars = value.ToLowerInvariant().ToCharArray();
        var atWordStart = true;
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (char.IsLetter(ch))
            {
                if (atWordStart) chars[i] = char.ToUpperInvariant(ch);
                atWordStart = false;
            }
            else if (char.IsDigit(ch))
            {
                atWordStart = false;
            }
            else
            {
                atWordStart = true;
            }
        }
        return new string(chars);
    }

    private static string JoinNonEmpty(string left, string right, string separator)
    {
        if (string.IsNullOrEmpty(left)) return right;
        if (string.IsNullOrEmpty(right)) return left;
        return left + separator + right;
    }
}

public sealed record PreviewInputItem(
    Guid Id,
    string ParentDirectory,
    string CurrentName,
    string Stem,
    string Extension,
    bool IsIncluded);

public sealed record PreviewItemResult(
    Guid ItemId,
    int DisplayOrder,
    string NewName,
    PreviewStatus Status);

public sealed record PreviewBatchResult(
    IReadOnlyList<PreviewItemResult> Items,
    int IncludedCount,
    int ChangedCount,
    int UnchangedCount,
    TimeSpan ComputeTime);

public enum PreviewStatus
{
    Ready,
    Unchanged,
    Excluded,
}
