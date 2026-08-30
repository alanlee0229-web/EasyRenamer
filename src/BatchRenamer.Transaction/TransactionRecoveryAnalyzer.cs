using System.Diagnostics;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.7 read-only crash-recovery analysis. No namespace mutation exists in this type.
/// Classification is based on Frozen Plan + advisory Journal/Checkpoint + current filesystem state.
/// Journal and state.json may explain history, but never override contradictory filesystem evidence.
/// </summary>
public static class TransactionRecoveryAnalyzer
{
    public static TransactionRecoveryAnalysis Analyze(
        string transactionDirectory,
        IReadOnlyFileSystem fileSystem,
        IPathSemanticsProvider semanticsProvider,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(semanticsProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(exactNamespaceInspector);

        var stopwatch = Stopwatch.StartNew();
        var directory = Path.GetFullPath(transactionDirectory);
        var planLoad = RenamePlanPersistence.Load(Path.Combine(directory, RenamePlanPersistence.PlanFileName));
        var issues = planLoad.Issues.ToList();
        if (!planLoad.Success || planLoad.Plan is null)
        {
            stopwatch.Stop();
            return new(
                TransactionRecoveryState.Ambiguous,
                null,
                null,
                null,
                Array.Empty<TransactionRecoveryEntry>(),
                issues,
                stopwatch.Elapsed);
        }

        var plan = planLoad.Plan;
        var journal = TransactionJournal.Load(directory, plan);
        var state = TransactionStateStore.Load(directory, plan.TransactionId);
        issues.AddRange(journal.Issues);
        issues.AddRange(state.Issues);

        if (!journal.Success)
            issues.Add(new TransactionIssue(ValidationSeverity.Warning, "RECOVERY_JOURNAL_UNTRUSTED", "Journal 存在错误；Recovery 将以真实文件系统状态为主。", Path: journal.JournalPath));
        if (!state.Success)
            issues.Add(new TransactionIssue(ValidationSeverity.Warning, "RECOVERY_CHECKPOINT_UNTRUSTED", "state.json 存在错误；Recovery 不会把它视为权威状态。", Path: state.StatePath));

        var frozenSemantics = plan.DirectorySemantics.ToDictionary(
            x => RenamePlanIntegrity.NormalizeFullPath(x.DirectoryPath),
            StringComparer.Ordinal);

        foreach (var pair in frozenSemantics)
        {
            var current = semanticsProvider.GetSemantics(pair.Key);
            if (current.IsCaseSensitive != pair.Value.IsCaseSensitive)
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Error,
                    "RECOVERY_PATH_SEMANTICS_CHANGED",
                    "Recovery 时目录大小写语义与冻结计划不一致，拒绝自动推断。",
                    Path: pair.Key));
            }
            else if (pair.Value.IsReliable && !current.IsReliable)
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Error,
                    "RECOVERY_PATH_SEMANTICS_UNVERIFIABLE",
                    "冻结计划使用可靠 PathSemantics，但 Recovery 时已无法可靠确认。",
                    Path: pair.Key));
            }
            else if (!current.IsReliable)
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Warning,
                    "RECOVERY_PATH_SEMANTICS_BEST_EFFORT",
                    "当前文件系统只能提供 Best-effort Recovery 语义。",
                    Path: pair.Key));
            }
        }

        if (issues.Any(x => x.Severity == ValidationSeverity.Error && x.Code.StartsWith("RECOVERY_PATH_", StringComparison.Ordinal)))
        {
            stopwatch.Stop();
            return new(
                TransactionRecoveryState.Ambiguous,
                plan,
                journal,
                state,
                Array.Empty<TransactionRecoveryEntry>(),
                issues,
                stopwatch.Elapsed);
        }

        var observations = new List<TransactionRecoveryEntry>(plan.Entries.Count);
        foreach (var entry in plan.Entries.OrderBy(x => x.Ordinal))
        {
            var sourceDirectory = RenamePlanIntegrity.NormalizeFullPath(Path.GetDirectoryName(entry.SourcePath) ?? string.Empty);
            if (!frozenSemantics.TryGetValue(sourceDirectory, out var semantics))
            {
                var issue = new TransactionIssue(
                    ValidationSeverity.Error,
                    "RECOVERY_DIRECTORY_SEMANTICS_MISSING",
                    "冻结计划缺少该 Source 目录的 PathSemantics。",
                    entry.Ordinal,
                    entry.ItemId,
                    sourceDirectory);
                observations.Add(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Ambiguous, null, issue.Code));
                issues.Add(issue);
                continue;
            }

            var observation = InspectEntry(entry, semantics, plan, fileSystem, identityProvider, exactNamespaceInspector);
            observations.Add(observation.Entry);
            issues.AddRange(observation.Issues);
        }

        var overall = ClassifyOverall(observations, journal, state);
        stopwatch.Stop();
        return new(overall, plan, journal, state, observations.ToArray(), issues.ToArray(), stopwatch.Elapsed);
    }

    private static EntryInspection InspectEntry(
        RenamePlanEntry entry,
        RenamePlanDirectorySemantics semantics,
        RenamePlan plan,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        if (!semantics.IsCaseSensitive && IsCaseOnlyRename(entry))
            return InspectCaseOnly(entry, fileSystem, identityProvider, exactNamespaceInspector);

        var issues = new List<TransactionIssue>();
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var candidates = new[]
        {
            (State: RecoveryEntryState.NotStarted, Path: entry.SourcePath),
            (State: RecoveryEntryState.Phase1Applied, Path: entry.TemporaryPath),
            (State: RecoveryEntryState.Phase2Applied, Path: entry.TargetPath),
        };

        if (entry.ExpectedFileIdentity is { } expectedIdentity)
        {
            var ownedLocations = new List<(RecoveryEntryState State, string Path)>();
            var externalConflict = false;
            var identityUnverifiable = false;

            foreach (var candidate in candidates)
            {
                var kind = fileSystem.GetEntryKind(candidate.Path);
                if (kind == FileSystemEntryKind.Missing) continue;
                if (kind == FileSystemEntryKind.Other)
                {
                    identityUnverifiable = true;
                    issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_NAMESPACE_UNVERIFIABLE", "无法可靠读取计划 namespace。", entry, candidate.Path));
                    continue;
                }
                if (kind != expectedKind)
                {
                    externalConflict = true;
                    issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_EXTERNAL_KIND_CONFLICT", "计划 namespace 被不同类型的外部对象占用。", entry, candidate.Path));
                    continue;
                }

                var actualIdentity = identityProvider.TryGetIdentity(candidate.Path, entry.IsDirectory);
                if (actualIdentity is null)
                {
                    identityUnverifiable = true;
                    issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_IDENTITY_UNVERIFIABLE", "无法读取当前对象 FileIdentity。", entry, candidate.Path));
                    continue;
                }

                if (actualIdentity.Value == expectedIdentity)
                {
                    ownedLocations.Add(candidate);
                }
                else if (!IsInternalPlanOccupant(candidate.Path, actualIdentity.Value, entry, plan, semantics))
                {
                    externalConflict = true;
                    issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_EXTERNAL_OBJECT", "计划 namespace 出现无法由其他冻结 Source/Target 解释的外部对象。", entry, candidate.Path));
                }
            }

            if (identityUnverifiable)
                return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Ambiguous, null, "RECOVERY_IDENTITY_UNVERIFIABLE"), issues);
            if (ownedLocations.Count > 1)
            {
                issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_OBJECT_MULTIPLE_LOCATIONS", "同一冻结对象同时匹配多个 Source/Temp/Target namespace。", entry, entry.SourcePath));
                return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Ambiguous, null, "RECOVERY_OBJECT_MULTIPLE_LOCATIONS"), issues);
            }
            if (ownedLocations.Count == 0)
            {
                issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_OBJECT_MISSING", "无法在 Source/Temp/Target 中定位冻结对象；可能已被外部删除或替换。", entry, entry.SourcePath));
                return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.ExternallyModified, null, "RECOVERY_OBJECT_MISSING"), issues);
            }
            if (externalConflict)
            {
                return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.ExternallyModified, ownedLocations[0].Path, "RECOVERY_EXTERNAL_OBJECT"), issues);
            }

            return new(new(entry.Ordinal, entry.ItemId, ownedLocations[0].State, ownedLocations[0].Path, null), issues);
        }

        // Best-effort mode for filesystems where FileIdentity was not frozen. Recovery may classify
        // only when exactly one candidate contains the expected object kind.
        var present = new List<(RecoveryEntryState State, string Path)>();
        foreach (var candidate in candidates)
        {
            var kind = fileSystem.GetEntryKind(candidate.Path);
            if (kind == FileSystemEntryKind.Other)
            {
                issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_NAMESPACE_UNVERIFIABLE", "无法可靠读取计划 namespace。", entry, candidate.Path));
                return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Ambiguous, null, "RECOVERY_NAMESPACE_UNVERIFIABLE"), issues);
            }
            if (kind == expectedKind) present.Add(candidate);
            else if (kind != FileSystemEntryKind.Missing)
            {
                issues.Add(Issue(ValidationSeverity.Error, "RECOVERY_EXTERNAL_KIND_CONFLICT", "计划 namespace 被不同类型对象占用。", entry, candidate.Path));
                return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.ExternallyModified, null, "RECOVERY_EXTERNAL_KIND_CONFLICT"), issues);
            }
        }

        issues.Add(Issue(ValidationSeverity.Warning, "RECOVERY_IDENTITY_BEST_EFFORT", "该计划项没有冻结 FileIdentity，只能 Best-effort 推断。", entry, entry.SourcePath));
        if (present.Count == 1)
            return new(new(entry.Ordinal, entry.ItemId, present[0].State, present[0].Path, null), issues);

        var code = present.Count == 0 ? "RECOVERY_BEST_EFFORT_OBJECT_MISSING" : "RECOVERY_BEST_EFFORT_AMBIGUOUS";
        issues.Add(Issue(ValidationSeverity.Error, code,
            present.Count == 0 ? "Best-effort 模式下无法定位计划对象。" : "Best-effort 模式下多个 namespace 同时存在，无法唯一定位对象。",
            entry,
            entry.SourcePath));
        return new(new(entry.Ordinal, entry.ItemId, present.Count == 0 ? RecoveryEntryState.ExternallyModified : RecoveryEntryState.Ambiguous, null, code), issues);
    }

    private static EntryInspection InspectCaseOnly(
        RenamePlanEntry entry,
        IReadOnlyFileSystem fileSystem,
        IFileIdentityProvider identityProvider,
        IExactNamespaceInspector exactNamespaceInspector)
    {
        var issues = new List<TransactionIssue>();
        var expectedKind = entry.IsDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File;
        var tempKind = fileSystem.GetEntryKind(entry.TemporaryPath);
        if (tempKind == FileSystemEntryKind.Other)
        {
            var issue = Issue(ValidationSeverity.Error, "RECOVERY_CASE_TEMP_UNVERIFIABLE", "无法可靠读取 case-only Temp namespace。", entry, entry.TemporaryPath);
            return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Ambiguous, null, issue.Code), [issue]);
        }

        if (tempKind == expectedKind)
        {
            var identityState = CheckIdentity(entry, entry.TemporaryPath, identityProvider);
            if (identityState is not null)
            {
                issues.Add(identityState);
                return new(new(entry.Ordinal, entry.ItemId,
                    identityState.Code == "RECOVERY_IDENTITY_CHANGED" ? RecoveryEntryState.ExternallyModified : RecoveryEntryState.Ambiguous,
                    entry.TemporaryPath,
                    identityState.Code), issues);
            }
            return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Phase1Applied, entry.TemporaryPath, null), issues);
        }
        if (tempKind != FileSystemEntryKind.Missing)
        {
            var issue = Issue(ValidationSeverity.Error, "RECOVERY_EXTERNAL_KIND_CONFLICT", "case-only Temp 被不同类型对象占用。", entry, entry.TemporaryPath);
            return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.ExternallyModified, null, issue.Code), [issue]);
        }

        var actualPath = exactNamespaceInspector.TryGetActualPath(entry.SourcePath, entry.IsDirectory, caseSensitive: false);
        if (actualPath is null)
        {
            var issue = Issue(ValidationSeverity.Error, "RECOVERY_CASE_OBJECT_MISSING", "无法确认 case-only 对象的当前实际拼写。", entry, entry.SourcePath);
            return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.ExternallyModified, null, issue.Code), [issue]);
        }

        var identityIssue = CheckIdentity(entry, actualPath, identityProvider);
        if (identityIssue is not null)
        {
            issues.Add(identityIssue);
            return new(new(entry.Ordinal, entry.ItemId,
                identityIssue.Code == "RECOVERY_IDENTITY_CHANGED" ? RecoveryEntryState.ExternallyModified : RecoveryEntryState.Ambiguous,
                actualPath,
                identityIssue.Code), issues);
        }

        var actualName = Path.GetFileName(actualPath);
        if (string.Equals(actualName, Path.GetFileName(entry.SourcePath), StringComparison.Ordinal))
            return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.NotStarted, actualPath, null), issues);
        if (string.Equals(actualName, Path.GetFileName(entry.TargetPath), StringComparison.Ordinal))
            return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Phase2Applied, actualPath, null), issues);

        var spellingIssue = Issue(ValidationSeverity.Error, "RECOVERY_CASE_SPELLING_AMBIGUOUS", "case-only 对象实际拼写既不等于冻结 Source 也不等于冻结 Target。", entry, actualPath);
        issues.Add(spellingIssue);
        return new(new(entry.Ordinal, entry.ItemId, RecoveryEntryState.Ambiguous, actualPath, spellingIssue.Code), issues);
    }

    private static TransactionIssue? CheckIdentity(
        RenamePlanEntry entry,
        string path,
        IFileIdentityProvider identityProvider)
    {
        if (entry.ExpectedFileIdentity is not { } expectedIdentity)
            return null;
        var actual = identityProvider.TryGetIdentity(path, entry.IsDirectory);
        if (actual is null)
            return Issue(ValidationSeverity.Error, "RECOVERY_IDENTITY_UNVERIFIABLE", "无法读取当前对象 FileIdentity。", entry, path);
        if (actual.Value != expectedIdentity)
            return Issue(ValidationSeverity.Error, "RECOVERY_IDENTITY_CHANGED", "当前对象 FileIdentity 与冻结计划不一致。", entry, path);
        return null;
    }

    private static TransactionRecoveryState ClassifyOverall(
        IReadOnlyList<TransactionRecoveryEntry> entries,
        TransactionJournalLoadResult journal,
        TransactionStateLoadResult state)
    {
        if (entries.Count == 0) return TransactionRecoveryState.Ambiguous;
        if (entries.Any(x => x.State == RecoveryEntryState.Ambiguous)) return TransactionRecoveryState.Ambiguous;
        if (entries.Any(x => x.State == RecoveryEntryState.ExternallyModified)) return TransactionRecoveryState.ExternallyModified;

        var source = entries.Count(x => x.State == RecoveryEntryState.NotStarted);
        var temp = entries.Count(x => x.State == RecoveryEntryState.Phase1Applied);
        var target = entries.Count(x => x.State == RecoveryEntryState.Phase2Applied);
        var hasRollbackEvidence = journal.Events.Any(x =>
            x.Operation == TransactionJournalOperation.RollbackTargetToTemp
            || x.Operation == TransactionJournalOperation.RollbackTempToSource);
        var checkpointPhase = state.Checkpoint?.Phase;
        var hasAppliedEvidence = journal.Events.Any(x => x.Kind == TransactionJournalEventKind.Done);

        if (source == entries.Count)
        {
            if (hasRollbackEvidence || checkpointPhase == TransactionCheckpointPhase.RolledBack)
                return TransactionRecoveryState.RolledBack;
            return hasAppliedEvidence ? TransactionRecoveryState.RolledBack : TransactionRecoveryState.NotStarted;
        }

        // Only durable rollback Journal evidence is allowed to change the interpretation of a mixed
        // namespace state. state.json is advisory and cannot, by itself, trigger automatic rollback.
        if (hasRollbackEvidence)
            return TransactionRecoveryState.RollbackInProgress;

        if (target == entries.Count) return TransactionRecoveryState.Completed;
        if (temp == entries.Count) return TransactionRecoveryState.Phase1Applied;
        if (source + temp == entries.Count) return TransactionRecoveryState.Phase1InProgress;
        if (temp + target == entries.Count) return TransactionRecoveryState.Phase2InProgress;
        return TransactionRecoveryState.Ambiguous;
    }


    private static bool IsInternalPlanOccupant(
        string path,
        FileIdentity actualIdentity,
        RenamePlanEntry currentEntry,
        RenamePlan plan,
        RenamePlanDirectorySemantics semantics)
    {
        var comparison = semantics.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var other in plan.Entries)
        {
            if (other.ItemId == currentEntry.ItemId || other.ExpectedFileIdentity is not { } otherIdentity || otherIdentity != actualIdentity)
                continue;

            if (string.Equals(path, other.SourcePath, comparison)
                || string.Equals(path, other.TargetPath, comparison))
                return true;
        }

        return false;
    }

    private static bool IsCaseOnlyRename(RenamePlanEntry entry)
        => string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.OrdinalIgnoreCase)
           && !string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.Ordinal);

    private static TransactionIssue Issue(
        ValidationSeverity severity,
        string code,
        string message,
        RenamePlanEntry entry,
        string path)
        => new(severity, code, message, entry.Ordinal, entry.ItemId, path);

    private sealed record EntryInspection(
        TransactionRecoveryEntry Entry,
        IReadOnlyList<TransactionIssue> Issues);
}
