using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BatchRenamer.Core;
using BatchRenamer.FileSystem;
using BatchRenamer.Transaction;

namespace BatchRenamer.ReleaseStressTests;

internal static class Program
{
    private const int DefaultCount = 20_000;
    private const int MaxCount = 50_000;

    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("ERROR: Release stress audit must run on Windows.");
            return 2;
        }

        var options = Options.Parse(args);
        if (options.Count < 1 || options.Count > MaxCount)
        {
            Console.Error.WriteLine($"ERROR: --count must be between 1 and {MaxCount:N0}.");
            return 2;
        }

        var runId = Guid.NewGuid().ToString("N");
        var sandbox = Path.Combine(Path.GetTempPath(), "BatchRenamer.ReleaseStress", runId);
        var dataDirectory = Path.Combine(sandbox, "data");
        var transactionsRoot = Path.Combine(sandbox, "transactions");
        var reportPath = Path.GetFullPath(options.ReportPath ?? Path.Combine(
            Directory.GetCurrentDirectory(), "artifacts", "stress", $"release-stress-{options.Count}-{runId}.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(transactionsRoot);

        var report = new StressReport
        {
            RunId = runId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Count = options.Count,
            Sandbox = sandbox,
            ReportPath = reportPath,
            OsVersion = Environment.OSVersion.VersionString,
            DotnetVersion = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount,
        };

        var overall = Stopwatch.StartNew();
        var success = false;
        try
        {
            Console.WriteLine("BatchRenamer V1 release stress audit (V0.11.1 performance hotfix)");
            Console.WriteLine($"  items:   {options.Count:N0}");
            Console.WriteLine($"  sandbox: {sandbox}");
            Console.WriteLine("  safety:  this tool creates and renames files ONLY inside the sandbox above");
            Console.WriteLine();

            var fileSystem = new WindowsReadOnlyFileSystem();
            var semanticsProvider = new WindowsPathSemanticsProvider();
            var identityProvider = new WindowsFileIdentityProvider();
            var exactNamespaceInspector = new SystemExactNamespaceInspector();

            Phase(report, "create_files", () => CreateFiles(dataDirectory, options.Count));
            Ensure(Directory.EnumerateFiles(dataDirectory).Count() == options.Count,
                "CREATE_COUNT_MISMATCH", "Created file count does not match requested count.");

            IReadOnlyList<ValidationInputItem>? items = null;
            Phase(report, "capture_inputs", () =>
            {
                items = BuildInputs(dataDirectory, options.Count, identityProvider);
            });
            Ensure(items is not null && items.Count == options.Count,
                "INPUT_COUNT_MISMATCH", "Validation input count mismatch.");

            RenamePlanBuildResult? planBuild = null;
            Phase(report, "planner_final_validation", () =>
            {
                planBuild = RenamePlanner.BuildFinalPlan(
                    items!, fileSystem, semanticsProvider, identityProvider);
            });
            Ensure(planBuild?.Success == true && planBuild.Plan is not null,
                "PLANNER_FAILED", DescribePlannerFailure(planBuild));
            var plan = planBuild!.Plan!;
            report.TransactionId = plan.TransactionId;
            report.PlannerComputeMs = planBuild.ComputeTime.TotalMilliseconds;
            Console.WriteLine($"[ok] planner produced {plan.Entries.Count:N0} frozen entries in {planBuild.ComputeTime.TotalSeconds:F2}s");

            var executeMutation = new ProgressRenameMutationFileSystem(
                new SystemRenameMutationFileSystem(), options.Count * 2, "execute");
            TransactionNewExecutionResult? execute = null;
            Phase(report, "durable_execute", () =>
            {
                execute = TransactionNewExecutionCoordinator.Execute(
                    plan,
                    transactionsRoot,
                    fileSystem,
                    semanticsProvider,
                    identityProvider,
                    executeMutation,
                    exactNamespaceInspector);
            });
            Ensure(execute?.Success == true,
                "EXECUTE_FAILED", DescribeIssues(execute?.Issues));
            var transactionDirectory = execute!.Persistence?.TransactionDirectory;
            Ensure(!string.IsNullOrWhiteSpace(transactionDirectory),
                "TRANSACTION_DIRECTORY_MISSING", "Completed execution did not return a transaction directory.");
            transactionDirectory = Path.GetFullPath(transactionDirectory!);
            report.TransactionDirectory = transactionDirectory;
            report.ExecuteComputeMs = execute.ComputeTime.TotalMilliseconds;
            report.ExecuteMoveCount = executeMutation.MoveCount;
            Ensure(executeMutation.MoveCount == options.Count * 2L,
                "EXECUTE_MOVE_COUNT_MISMATCH", $"Expected {options.Count * 2:N0} execute moves, observed {executeMutation.MoveCount:N0}.");

            Phase(report, "verify_completed_namespace", () =>
            {
                VerifyCompleted(dataDirectory, options.Count);
                VerifySampleContents(dataDirectory, options.Count, targetNames: true);
            });

            CaptureMetadataMetrics(report, transactionDirectory, plan);

            TransactionStartupDiscoveryResult? completedScan = null;
            Phase(report, "startup_scan_completed", () =>
            {
                completedScan = TransactionStartupDiscovery.Scan(
                    transactionsRoot,
                    fileSystem,
                    semanticsProvider,
                    identityProvider,
                    exactNamespaceInspector);
            });
            Ensure(completedScan?.GateState == TransactionStartupGateState.Clear,
                "COMPLETED_STARTUP_GATE_NOT_CLEAR", DescribeIssues(completedScan?.Issues));
            report.CompletedStartupScanMs = completedScan!.ComputeTime.TotalMilliseconds;

            var undoMutation = new ProgressRenameMutationFileSystem(
                new SystemRenameMutationFileSystem(), options.Count * 2, "undo");
            TransactionUserUndoCoordinatorResult? undo = null;
            Phase(report, "durable_undo", () =>
            {
                undo = TransactionUserUndoCoordinator.Undo(
                    transactionDirectory,
                    transactionsRoot,
                    fileSystem,
                    semanticsProvider,
                    identityProvider,
                    undoMutation,
                    exactNamespaceInspector);
            });
            Ensure(undo?.Success == true,
                "UNDO_FAILED", DescribeIssues(undo?.Issues));
            report.UndoComputeMs = undo!.ComputeTime.TotalMilliseconds;
            report.UndoMoveCount = undoMutation.MoveCount;
            Ensure(undoMutation.MoveCount == options.Count * 2L,
                "UNDO_MOVE_COUNT_MISMATCH", $"Expected {options.Count * 2:N0} undo moves, observed {undoMutation.MoveCount:N0}.");

            Phase(report, "verify_rolled_back_namespace", () =>
            {
                VerifyRolledBack(dataDirectory, options.Count);
                VerifySampleContents(dataDirectory, options.Count, targetNames: false);
            });

            TransactionStartupDiscoveryResult? rolledBackScan = null;
            Phase(report, "startup_scan_rolled_back", () =>
            {
                rolledBackScan = TransactionStartupDiscovery.Scan(
                    transactionsRoot,
                    fileSystem,
                    semanticsProvider,
                    identityProvider,
                    exactNamespaceInspector);
            });
            Ensure(rolledBackScan?.GateState == TransactionStartupGateState.Clear,
                "ROLLED_BACK_STARTUP_GATE_NOT_CLEAR", DescribeIssues(rolledBackScan?.Issues));
            report.RolledBackStartupScanMs = rolledBackScan!.ComputeTime.TotalMilliseconds;

            Phase(report, "idempotent_second_undo", () =>
            {
                var secondMutation = new ProgressRenameMutationFileSystem(
                    new SystemRenameMutationFileSystem(), 0, "second-undo", quiet: true);
                var secondUndo = TransactionUserUndoCoordinator.Undo(
                    transactionDirectory,
                    transactionsRoot,
                    fileSystem,
                    semanticsProvider,
                    identityProvider,
                    secondMutation,
                    exactNamespaceInspector);
                Ensure(secondUndo.Success, "SECOND_UNDO_FAILED", DescribeIssues(secondUndo.Issues));
                Ensure(secondMutation.MoveCount == 0,
                    "SECOND_UNDO_MUTATED", $"Idempotent second Undo performed {secondMutation.MoveCount} namespace moves.");
            });

            success = true;
            report.Success = true;
            Console.WriteLine();
            Console.WriteLine("PASS: release stress audit completed with exact namespace restoration and zero Temp residue.");
        }
        catch (StressFailure ex)
        {
            report.Success = false;
            report.FailureCode = ex.Code;
            report.FailureMessage = ex.Message;
            Console.Error.WriteLine();
            Console.Error.WriteLine($"FAIL [{ex.Code}]: {ex.Message}");
        }
        catch (Exception ex)
        {
            report.Success = false;
            report.FailureCode = "UNHANDLED_EXCEPTION";
            report.FailureMessage = $"{ex.GetType().Name}: {ex.Message}";
            report.Exception = ex.ToString();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"FAIL [UNHANDLED_EXCEPTION]: {ex}");
        }
        finally
        {
            overall.Stop();
            report.TotalElapsedMs = overall.Elapsed.TotalMilliseconds;
            report.PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
            report.CompletedAtUtc = DateTimeOffset.UtcNow;
            WriteReport(reportPath, report);
            Console.WriteLine($"[report] {reportPath}");
            Console.WriteLine($"[metrics] total={overall.Elapsed.TotalSeconds:F2}s, peak-working-set={FormatBytes(report.PeakWorkingSetBytes)}");

            if (success && !options.KeepSandbox)
            {
                try
                {
                    Directory.Delete(sandbox, recursive: true);
                    Console.WriteLine("[cleanup] sandbox removed after successful audit.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[warning] sandbox cleanup failed: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"[warning] inspect and remove manually when convenient: {sandbox}");
                }
            }
            else
            {
                Console.WriteLine($"[preserved] sandbox retained for inspection: {sandbox}");
            }
        }

        return success ? 0 : 1;
    }

    private static IReadOnlyList<ValidationInputItem> BuildInputs(
        string dataDirectory,
        int count,
        IFileIdentityProvider identityProvider)
    {
        var items = new List<ValidationInputItem>(count);
        for (var i = 0; i < count; i++)
        {
            if ((i + 1) % 2000 == 0)
                Console.WriteLine($"[input] {i + 1:N0}/{count:N0}");

            var currentName = SourceName(i);
            var sourcePath = Path.Combine(dataDirectory, currentName);
            var identity = identityProvider.TryGetIdentity(sourcePath, isDirectory: false);
            Ensure(identity is not null,
                "FILE_IDENTITY_UNAVAILABLE",
                $"Strong local FileIdentity was unavailable for stress item {currentName}.");

            items.Add(new ValidationInputItem(
                Guid.NewGuid(),
                sourcePath,
                dataDirectory,
                currentName,
                ".txt",
                TargetName(i),
                IsDirectory: false,
                IsIncluded: true,
                IsSynthetic: false,
                ExpectedFileIdentity: identity));
        }
        return items;
    }

    private static void CreateFiles(string directory, int count)
    {
        var encoding = new UTF8Encoding(false);
        for (var i = 0; i < count; i++)
        {
            var path = Path.Combine(directory, SourceName(i));
            File.WriteAllText(path, Payload(i), encoding);
            if ((i + 1) % 2000 == 0)
                Console.WriteLine($"[create] {i + 1:N0}/{count:N0}");
        }
    }

    private static void VerifyCompleted(string directory, int count)
    {
        var entries = Directory.EnumerateFiles(directory).Select(x => Path.GetFileName(x)).ToArray();
        Ensure(entries.Length == count,
            "COMPLETED_FILE_COUNT_MISMATCH", $"Expected {count:N0} files after Execute, found {entries.Length:N0}.");
        Ensure(!entries.Any(x => x is not null && x.StartsWith(".~br-", StringComparison.Ordinal)),
            "TEMP_RESIDUE_AFTER_EXECUTE", "Temporary .~br-* entries remain after completed Execute.");
        Ensure(!entries.Any(x => x is not null && x.StartsWith("SRC_", StringComparison.OrdinalIgnoreCase)),
            "SOURCE_RESIDUE_AFTER_EXECUTE", "Source namespace remains after completed Execute.");
        Ensure(entries.All(x => x is not null && x.StartsWith("REN_", StringComparison.OrdinalIgnoreCase)),
            "UNEXPECTED_NAMESPACE_AFTER_EXECUTE", "Unexpected filename exists after completed Execute.");
    }

    private static void VerifyRolledBack(string directory, int count)
    {
        var entries = Directory.EnumerateFiles(directory).Select(x => Path.GetFileName(x)).ToArray();
        Ensure(entries.Length == count,
            "ROLLBACK_FILE_COUNT_MISMATCH", $"Expected {count:N0} files after Undo, found {entries.Length:N0}.");
        Ensure(!entries.Any(x => x is not null && x.StartsWith(".~br-", StringComparison.Ordinal)),
            "TEMP_RESIDUE_AFTER_UNDO", "Temporary .~br-* entries remain after Undo.");
        Ensure(!entries.Any(x => x is not null && x.StartsWith("REN_", StringComparison.OrdinalIgnoreCase)),
            "TARGET_RESIDUE_AFTER_UNDO", "Target namespace remains after Undo.");
        Ensure(entries.All(x => x is not null && x.StartsWith("SRC_", StringComparison.OrdinalIgnoreCase)),
            "UNEXPECTED_NAMESPACE_AFTER_UNDO", "Unexpected filename exists after Undo.");
    }

    private static void VerifySampleContents(string directory, int count, bool targetNames)
    {
        var sampleIndexes = new HashSet<int> { 0, count / 2, count - 1 };
        foreach (var i in sampleIndexes)
        {
            var name = targetNames ? TargetName(i) : SourceName(i);
            var path = Path.Combine(directory, name);
            Ensure(File.Exists(path), "SAMPLE_FILE_MISSING", $"Sample file missing: {path}");
            var content = File.ReadAllText(path, Encoding.UTF8);
            Ensure(string.Equals(content, Payload(i), StringComparison.Ordinal),
                "CONTENT_CHANGED", $"Content changed for sample item {i:N0}.");
        }
    }

    private static void CaptureMetadataMetrics(StressReport report, string transactionDirectory, RenamePlan plan)
    {
        var planPath = Path.Combine(transactionDirectory, RenamePlanPersistence.PlanFileName);
        var journalPath = Path.Combine(transactionDirectory, TransactionJournal.JournalFileName);
        var statePath = Path.Combine(transactionDirectory, TransactionStateStore.StateFileName);
        report.PlanBytes = File.Exists(planPath) ? new FileInfo(planPath).Length : 0;
        report.JournalBytesAfterExecute = File.Exists(journalPath) ? new FileInfo(journalPath).Length : 0;
        report.StateBytesAfterExecute = File.Exists(statePath) ? new FileInfo(statePath).Length : 0;

        var journal = TransactionJournal.Load(transactionDirectory, plan);
        Ensure(journal.Success, "JOURNAL_LOAD_FAILED_AFTER_EXECUTE", DescribeIssues(journal.Issues));
        report.JournalEventsAfterExecute = journal.Events.Count;
        Ensure(journal.Events.Count == report.Count * 4,
            "JOURNAL_EVENT_COUNT_MISMATCH",
            $"Expected {report.Count * 4:N0} Execute journal events, found {journal.Events.Count:N0}.");

        Console.WriteLine($"[metadata] plan={FormatBytes(report.PlanBytes)}, journal={FormatBytes(report.JournalBytesAfterExecute)}, events={report.JournalEventsAfterExecute:N0}");
    }

    private static string DescribePlannerFailure(RenamePlanBuildResult? result)
    {
        if (result is null) return "Planner returned no result.";
        var parts = result.PlannerIssues.Select(x => $"{x.Code}: {x.Message}")
            .Concat(result.FinalValidation.Items.SelectMany(x => x.Issues).Select(x => $"{x.Code}: {x.Message}"))
            .Take(12);
        return string.Join(" | ", parts);
    }

    private static string DescribeIssues(IReadOnlyList<TransactionIssue>? issues)
    {
        if (issues is null || issues.Count == 0) return "No issue detail was returned.";
        return string.Join(" | ", issues.Where(x => x.Severity != ValidationSeverity.Info)
            .Take(12).Select(x => $"{x.Code}: {x.Message}"));
    }

    private static void Phase(StressReport report, string name, Action action)
    {
        Console.WriteLine($"[phase] {name} ...");
        var sw = Stopwatch.StartNew();
        using var heartbeat = new Timer(
            _ => Console.WriteLine($"[alive] {name}: elapsed {sw.Elapsed.TotalSeconds:F0}s"),
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15));
        action();
        heartbeat.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        sw.Stop();
        report.Phases[name] = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[ok] {name}: {sw.Elapsed.TotalSeconds:F2}s");
    }

    private static void Ensure(bool condition, string code, string message)
    {
        if (!condition) throw new StressFailure(code, message);
    }

    private static void WriteReport(string reportPath, StressReport report)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(report, options);
            File.WriteAllText(reportPath, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: could not write stress report: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string SourceName(int index) => $"SRC_{index + 1:D6}.txt";
    private static string TargetName(int index) => $"REN_{index + 1:D6}.txt";
    private static string Payload(int index) => $"batchrenamer-release-stress-{index + 1:D6}";

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        double scaled = value;
        var unit = 0;
        while (scaled >= 1024 && unit < units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }
        return $"{scaled:F1} {units[unit]}";
    }

    private sealed class ProgressRenameMutationFileSystem : IRenameMutationFileSystem
    {
        private readonly IRenameMutationFileSystem _inner;
        private readonly long _expectedMoves;
        private readonly string _label;
        private readonly bool _quiet;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _moveCount;

        public ProgressRenameMutationFileSystem(
            IRenameMutationFileSystem inner,
            long expectedMoves,
            string label,
            bool quiet = false)
        {
            _inner = inner;
            _expectedMoves = expectedMoves;
            _label = label;
            _quiet = quiet;
        }

        public long MoveCount => Interlocked.Read(ref _moveCount);

        public void MoveFileNoOverwrite(string sourcePath, string destinationPath)
        {
            _inner.MoveFileNoOverwrite(sourcePath, destinationPath);
            OnMoved();
        }

        public void MoveDirectoryNoOverwrite(string sourcePath, string destinationPath)
        {
            _inner.MoveDirectoryNoOverwrite(sourcePath, destinationPath);
            OnMoved();
        }

        private void OnMoved()
        {
            var value = Interlocked.Increment(ref _moveCount);
            if (_quiet) return;
            if (value % 1000 == 0 || value == _expectedMoves)
            {
                var rate = value / Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001);
                Console.WriteLine($"[{_label}] moves {value:N0}/{_expectedMoves:N0}, elapsed {_stopwatch.Elapsed.TotalSeconds:F1}s, {rate:F1} moves/s");
            }
        }
    }

    private sealed class StressFailure : Exception
    {
        public StressFailure(string code, string message) : base(message) => Code = code;
        public string Code { get; }
    }

    private sealed class Options
    {
        public int Count { get; init; } = DefaultCount;
        public bool KeepSandbox { get; init; }
        public string? ReportPath { get; init; }

        public static Options Parse(string[] args)
        {
            var count = DefaultCount;
            var keep = false;
            string? report = null;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--count" when i + 1 < args.Length && int.TryParse(args[++i], out var parsed):
                        count = parsed;
                        break;
                    case "--quick":
                        count = 2_000;
                        break;
                    case "--keep":
                        keep = true;
                        break;
                    case "--report" when i + 1 < args.Length:
                        report = args[++i];
                        break;
                    default:
                        throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
                }
            }
            return new Options { Count = count, KeepSandbox = keep, ReportPath = report };
        }
    }

    private sealed class StressReport
    {
        public string RunId { get; set; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset CompletedAtUtc { get; set; }
        public int Count { get; set; }
        public bool Success { get; set; }
        public string? FailureCode { get; set; }
        public string? FailureMessage { get; set; }
        public string? Exception { get; set; }
        public Guid? TransactionId { get; set; }
        public string? TransactionDirectory { get; set; }
        public string Sandbox { get; set; } = string.Empty;
        public string ReportPath { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string DotnetVersion { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }
        public double TotalElapsedMs { get; set; }
        public long PeakWorkingSetBytes { get; set; }
        public double PlannerComputeMs { get; set; }
        public double ExecuteComputeMs { get; set; }
        public double UndoComputeMs { get; set; }
        public double CompletedStartupScanMs { get; set; }
        public double RolledBackStartupScanMs { get; set; }
        public long ExecuteMoveCount { get; set; }
        public long UndoMoveCount { get; set; }
        public long PlanBytes { get; set; }
        public long JournalBytesAfterExecute { get; set; }
        public long StateBytesAfterExecute { get; set; }
        public int JournalEventsAfterExecute { get; set; }
        public Dictionary<string, double> Phases { get; } = new(StringComparer.Ordinal);
    }
}
