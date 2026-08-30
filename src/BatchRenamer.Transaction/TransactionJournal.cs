using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BatchRenamer.Core;

namespace BatchRenamer.Transaction;

/// <summary>
/// V0.7 append-only transaction event log. Each JSON object occupies exactly one UTF-8 line.
/// The journal is evidence, never authority: crash recovery must combine it with plan.json and
/// current filesystem state before deciding what happened.
/// </summary>
public static class TransactionJournal
{
    public const int CurrentSchemaVersion = 1;
    public const string JournalFileName = "events.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static TransactionJournalAppendResult Append(
        string transactionDirectory,
        TransactionJournalEvent journalEvent)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(journalEvent);

        var directory = Path.GetFullPath(transactionDirectory);
        var journalPath = Path.Combine(directory, JournalFileName);
        if (!Directory.Exists(directory))
        {
            return Failure(journalPath, "JOURNAL_TRANSACTION_DIRECTORY_MISSING",
                "事务目录不存在，拒绝创建孤立 events.jsonl。");
        }

        var planPath = Path.Combine(directory, RenamePlanPersistence.PlanFileName);
        var planLoad = RenamePlanPersistence.Load(planPath);
        if (!planLoad.Success || planLoad.Plan is not { } plan)
        {
            var loadIssues = planLoad.Issues.Concat([
                new TransactionIssue(
                    ValidationSeverity.Error,
                    "JOURNAL_PLAN_UNAVAILABLE",
                    "无法验证冻结 plan.json，拒绝追加事务事件。",
                    Path: journalPath)
            ]).ToArray();
            return new(false, journalPath, null, loadIssues);
        }

        return AppendBound(directory, plan, journalEvent);
    }

    /// <summary>
    /// Plan-bound append used by a live transaction session. The persisted plan must have been
    /// validated once when the session was created; this avoids re-reading/deserializing a large
    /// plan.json before every INTENT/DONE event. The public Append overload above remains the safe
    /// one-shot API for callers without a bound session.
    /// </summary>
    internal static TransactionJournalAppendResult AppendBound(
        string transactionDirectory,
        RenamePlan validatedPlan,
        TransactionJournalEvent journalEvent)
        => AppendBound(transactionDirectory, validatedPlan, journalEvent, appendStream: null);

    /// <summary>
    /// Session-bound append overload. When an already-open append stream is supplied, the exact
    /// same INTENT/DONE durability rule is preserved (Flush(true) for every event) without paying
    /// an Open/Create/Close cycle for every journal line. The stream is owned by the caller.
    /// </summary>
    internal static TransactionJournalAppendResult AppendBound(
        string transactionDirectory,
        RenamePlan validatedPlan,
        TransactionJournalEvent journalEvent,
        FileStream? appendStream)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(validatedPlan);
        ArgumentNullException.ThrowIfNull(journalEvent);

        var directory = Path.GetFullPath(transactionDirectory);
        var journalPath = Path.Combine(directory, JournalFileName);
        var issues = ValidateEnvelope(directory, journalEvent);
        issues.AddRange(ValidateAgainstPlan(journalEvent, validatedPlan));
        if (issues.Any(x => x.Severity == ValidationSeverity.Error))
            return new(false, journalPath, null, issues);

        try
        {
            if (!Directory.Exists(directory))
                return Failure(journalPath, "JOURNAL_TRANSACTION_DIRECTORY_MISSING", "事务目录不存在。");

            var bytes = JsonSerializer.SerializeToUtf8Bytes(journalEvent, JsonOptions);
            if (appendStream is null)
            {
                using var stream = new FileStream(
                    journalPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    16 * 1024,
                    FileOptions.WriteThrough);
                WriteDurableEvent(stream, bytes);
            }
            else
            {
                if (!appendStream.CanWrite)
                    throw new IOException("Bound journal append stream is not writable.");
                WriteDurableEvent(appendStream, bytes);
            }

            return new(true, journalPath, journalEvent, issues);
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "JOURNAL_APPEND_FAILED",
                $"无法持久化 append-only 事务事件：{ex.GetType().Name}: {ex.Message}",
                journalEvent.Ordinal,
                journalEvent.ItemId,
                journalPath));
            return new(false, journalPath, null, issues);
        }
    }

    private static void WriteDurableEvent(FileStream stream, byte[] bytes)
    {
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        // Keep the existing V0.7.1 contract: every INTENT and DONE line is durable before control
        // returns to the mutation boundary. This hotfix optimizes stream lifetime, not durability.
        stream.Flush(flushToDisk: true);
    }

    public static TransactionJournalLoadResult Load(string transactionDirectory, RenamePlan plan)
    {
        if (string.IsNullOrWhiteSpace(transactionDirectory))
            throw new ArgumentException("Transaction directory is required.", nameof(transactionDirectory));
        ArgumentNullException.ThrowIfNull(plan);

        var directory = Path.GetFullPath(transactionDirectory);
        var journalPath = Path.Combine(directory, JournalFileName);
        var issues = new List<TransactionIssue>();
        var events = new List<TransactionJournalEvent>();

        if (!Directory.Exists(directory))
        {
            return new(false, journalPath, events, [
                new TransactionIssue(ValidationSeverity.Error, "JOURNAL_TRANSACTION_DIRECTORY_MISSING", "事务目录不存在。", Path: directory)
            ]);
        }

        if (!File.Exists(journalPath))
        {
            return new(true, journalPath, events, [
                new TransactionIssue(ValidationSeverity.Info, "JOURNAL_NOT_CREATED", "事务尚未产生 events.jsonl。", Path: journalPath)
            ]);
        }

        try
        {
            var hasTerminalNewline = EndsWithNewline(journalPath);
            var seenEventIds = new HashSet<Guid>();
            var lineNumber = 0;

            using var stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);

            while (true)
            {
                var line = reader.ReadLine();
                if (line is null) break;
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    issues.Add(new TransactionIssue(
                        ValidationSeverity.Warning,
                        "JOURNAL_EMPTY_LINE",
                        $"events.jsonl 第 {lineNumber} 行为空，已忽略。",
                        Path: journalPath));
                    continue;
                }

                try
                {
                    var parsed = JsonSerializer.Deserialize<TransactionJournalEvent>(line, JsonOptions);
                    if (parsed is null)
                        throw new JsonException("Event deserialized to null.");

                    var envelopeIssues = ValidateEnvelope(directory, parsed);
                    var planIssues = ValidateAgainstPlan(parsed, plan);
                    issues.AddRange(envelopeIssues);
                    issues.AddRange(planIssues);
                    if (envelopeIssues.Concat(planIssues).Any(x => x.Severity == ValidationSeverity.Error))
                        continue;

                    if (!seenEventIds.Add(parsed.EventId))
                    {
                        issues.Add(new TransactionIssue(
                            ValidationSeverity.Error,
                            "JOURNAL_DUPLICATE_EVENT_ID",
                            $"events.jsonl 第 {lineNumber} 行重复 EventId，日志完整性不可确认。",
                            parsed.Ordinal,
                            parsed.ItemId,
                            journalPath));
                        continue;
                    }

                    events.Add(parsed);
                }
                catch (JsonException ex)
                {
                    if (reader.EndOfStream && !hasTerminalNewline)
                    {
                        issues.Add(new TransactionIssue(
                            ValidationSeverity.Warning,
                            "JOURNAL_TRUNCATED_TAIL",
                            $"events.jsonl 最后一行疑似因崩溃被截断，已忽略该尾行：{ex.Message}",
                            Path: journalPath));
                        break;
                    }

                    issues.Add(new TransactionIssue(
                        ValidationSeverity.Error,
                        "JOURNAL_JSON_INVALID",
                        $"events.jsonl 第 {lineNumber} 行 JSON 无效：{ex.Message}",
                        Path: journalPath));
                }
            }

            ValidateEventOrdering(events, issues, journalPath);
            var success = issues.All(x => x.Severity != ValidationSeverity.Error);
            return new(success, journalPath, events.ToArray(), issues.ToArray());
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionIssue(
                ValidationSeverity.Error,
                "JOURNAL_READ_FAILED",
                $"无法读取 events.jsonl：{ex.GetType().Name}: {ex.Message}",
                Path: journalPath));
            return new(false, journalPath, events.ToArray(), issues.ToArray());
        }
    }

    public static TransactionJournalEvent Create(
        RenamePlan plan,
        RenamePlanEntry entry,
        TransactionJournalEventKind kind,
        TransactionJournalOperation operation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(entry);
        return new(
            CurrentSchemaVersion,
            Guid.NewGuid(),
            plan.TransactionId,
            DateTimeOffset.UtcNow,
            kind,
            operation,
            entry.Ordinal,
            entry.ItemId);
    }

    private static List<TransactionIssue> ValidateEnvelope(string transactionDirectory, TransactionJournalEvent journalEvent)
    {
        var issues = new List<TransactionIssue>();
        if (journalEvent.SchemaVersion != CurrentSchemaVersion)
            issues.Add(Error("JOURNAL_SCHEMA_UNSUPPORTED", $"不支持 Journal SchemaVersion={journalEvent.SchemaVersion}。", journalEvent, transactionDirectory));
        if (journalEvent.EventId == Guid.Empty)
            issues.Add(Error("JOURNAL_EVENT_ID_EMPTY", "Journal EventId 不能为空。", journalEvent, transactionDirectory));
        if (journalEvent.TransactionId == Guid.Empty)
            issues.Add(Error("JOURNAL_TRANSACTION_ID_EMPTY", "Journal TransactionId 不能为空。", journalEvent, transactionDirectory));
        if (journalEvent.ItemId == Guid.Empty)
            issues.Add(Error("JOURNAL_ITEM_ID_EMPTY", "Journal ItemId 不能为空。", journalEvent, transactionDirectory));
        if (journalEvent.Ordinal < 0)
            issues.Add(Error("JOURNAL_ORDINAL_INVALID", "Journal Ordinal 不能为负数。", journalEvent, transactionDirectory));
        if (!Enum.IsDefined(journalEvent.Kind))
            issues.Add(Error("JOURNAL_KIND_INVALID", "Journal EventKind 不在当前 Schema 定义中。", journalEvent, transactionDirectory));
        if (!Enum.IsDefined(journalEvent.Operation))
            issues.Add(Error("JOURNAL_OPERATION_INVALID", "Journal Operation 不在当前 Schema 定义中。", journalEvent, transactionDirectory));

        var directoryName = new DirectoryInfo(transactionDirectory).Name;
        if (!Guid.TryParseExact(directoryName, "N", out var directoryTransactionId)
            || directoryTransactionId != journalEvent.TransactionId)
        {
            issues.Add(Error("JOURNAL_TRANSACTION_DIRECTORY_MISMATCH",
                "Journal TransactionId 与事务目录名称不一致。", journalEvent, transactionDirectory));
        }

        return issues;
    }

    private static IReadOnlyList<TransactionIssue> ValidateAgainstPlan(TransactionJournalEvent journalEvent, RenamePlan plan)
    {
        var issues = new List<TransactionIssue>();
        if (journalEvent.TransactionId != plan.TransactionId)
            issues.Add(Error("JOURNAL_PLAN_TRANSACTION_MISMATCH", "Journal TransactionId 与 plan.json 不一致。", journalEvent));

        RenamePlanEntry? entry = null;
        if (journalEvent.Ordinal >= 0
            && journalEvent.Ordinal < plan.Entries.Count
            && plan.Entries[journalEvent.Ordinal].Ordinal == journalEvent.Ordinal)
        {
            entry = plan.Entries[journalEvent.Ordinal];
        }
        else
        {
            entry = plan.Entries.FirstOrDefault(x => x.Ordinal == journalEvent.Ordinal);
        }

        if (entry is null)
        {
            issues.Add(Error("JOURNAL_PLAN_ORDINAL_UNKNOWN", "Journal Ordinal 不存在于冻结 RenamePlan。", journalEvent));
        }
        else if (entry.ItemId != journalEvent.ItemId)
        {
            issues.Add(Error("JOURNAL_PLAN_ITEM_MISMATCH", "Journal ItemId 与冻结 RenamePlan 条目不一致。", journalEvent));
        }

        return issues;
    }

    private static void ValidateEventOrdering(
        IReadOnlyList<TransactionJournalEvent> events,
        ICollection<TransactionIssue> issues,
        string journalPath)
    {
        var intents = new Dictionary<(int Ordinal, TransactionJournalOperation Operation), int>();
        foreach (var journalEvent in events)
        {
            var key = (journalEvent.Ordinal, journalEvent.Operation);
            if (journalEvent.Kind == TransactionJournalEventKind.Intent)
            {
                intents.TryGetValue(key, out var count);
                intents[key] = count + 1;
                continue;
            }

            if (!intents.TryGetValue(key, out var available) || available <= 0)
            {
                issues.Add(new TransactionIssue(
                    ValidationSeverity.Warning,
                    "JOURNAL_DONE_WITHOUT_INTENT",
                    "发现 DONE 但此前没有对应 INTENT。Recovery 不会仅凭 Journal 判断磁盘状态。",
                    journalEvent.Ordinal,
                    journalEvent.ItemId,
                    journalPath));
                continue;
            }

            intents[key] = available - 1;
        }
    }

    private static bool EndsWithNewline(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length == 0) return true;
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == (byte)'\n';
    }

    private static TransactionIssue Error(
        string code,
        string message,
        TransactionJournalEvent journalEvent,
        string? path = null)
        => new(ValidationSeverity.Error, code, message, journalEvent.Ordinal, journalEvent.ItemId, path);

    private static TransactionJournalAppendResult Failure(string journalPath, string code, string message)
        => new(false, journalPath, null, [
            new TransactionIssue(ValidationSeverity.Error, code, message, Path: journalPath)
        ]);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
