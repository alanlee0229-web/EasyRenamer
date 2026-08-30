using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Threading;
using BatchRenamer.App.Models;
using BatchRenamer.Core;
using BatchRenamer.FileSystem;
using BatchRenamer.Transaction;

namespace BatchRenamer.App.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Stack<List<Guid>> _orderHistory = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _filterTimer;
    private CancellationTokenSource? _previewCts;
    private int _previewGeneration;
    private long _inputRevision;
    private int _bulkInclusionDepth;
    private RenamePlan? _preparedPlan;
    private TransactionStartupDiscoveryResult? _startupTransactionGate;
    private bool _isTransactionBusy;
    private string? _lastUndoableTransactionDirectory;
    private readonly IReadOnlyFileSystem _fileSystem;
    private readonly IPathSemanticsProvider _pathSemanticsProvider;
    private readonly IFileIdentityProvider _fileIdentityProvider;

    private string _baseName = "照片";
    private string _originalMode = "不保留";
    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private string _searchText = string.Empty;
    private string _replaceText = string.Empty;
    private string _caseMode = "保持不变";
    private bool _sequenceEnabled = true;
    private bool _enableFindReplace;
    private bool _enableCaseConversion;
    private int _sequenceStart = 1;
    private int _sequenceStep = 1;
    private int _sequenceDigits = 3;
    private string _sequencePosition = "名称后";
    private string _separator = "_";
    private bool _showIssuesOnly;
    private string _query = string.Empty;
    private string _sortLabel = "自定义";
    private string _statusText = "0 项";
    private string _previewLatencyText = "预览 —";
    private bool _hasErrors;
    private bool _hasWarnings;
    private bool _isPreviewBusy;
    private bool _isPreviewDirty = true;
    private bool? _allIncludedState = true;

    public MainViewModel()
    {
        _fileSystem = new WindowsReadOnlyFileSystem();
        _pathSemanticsProvider = new WindowsPathSemanticsProvider();
        _fileIdentityProvider = new WindowsFileIdentityProvider();

        Items = new BulkObservableCollection<RenameItemViewModel>();
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _previewTimer.Tick += PreviewTimer_Tick;

        _filterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(160),
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ItemsView.Refresh();
        };

        InitializeInternalTools();
    }

    partial void InitializeInternalTools();

    public BulkObservableCollection<RenameItemViewModel> Items { get; }
    public ICollectionView ItemsView { get; }

    public IReadOnlyList<string> OriginalModes { get; } = ["不保留", "放在基础名称前", "放在基础名称后"];
    public IReadOnlyList<string> SequencePositions { get; } = ["名称前", "名称后"];
    public IReadOnlyList<string> CaseModes { get; } = ["保持不变", "全部小写", "全部大写", "单词首字母大写"];

    // Rule fields only schedule preview. They never synchronously rebuild the DataGrid.
    public string BaseName { get => _baseName; set { if (SetField(ref _baseName, value)) SchedulePreview(); } }
    public string OriginalMode
    {
        get => _originalMode;
        set
        {
            if (!SetField(ref _originalMode, value)) return;
            OnPropertyChanged(nameof(UsesOriginalName));
            SchedulePreview();
        }
    }
    public bool UsesOriginalName => !string.Equals(OriginalMode, "不保留", StringComparison.Ordinal);
    public string Prefix { get => _prefix; set { if (SetField(ref _prefix, value)) SchedulePreview(); } }
    public string Suffix { get => _suffix; set { if (SetField(ref _suffix, value)) SchedulePreview(); } }
    public string SearchText { get => _searchText; set { if (SetField(ref _searchText, value)) SchedulePreview(); } }
    public string ReplaceText { get => _replaceText; set { if (SetField(ref _replaceText, value)) SchedulePreview(); } }
    public string CaseMode { get => _caseMode; set { if (SetField(ref _caseMode, value)) SchedulePreview(); } }
    // Settings only decide which optional rule modules are exposed on the main work surface.
    // Rule parameters remain edited in-context on the main panel.
    public bool EnableFindReplace
    {
        get => _enableFindReplace;
        set
        {
            if (!SetField(ref _enableFindReplace, value)) return;
            SchedulePreview();
        }
    }

    public bool EnableCaseConversion
    {
        get => _enableCaseConversion;
        set
        {
            if (!SetField(ref _enableCaseConversion, value)) return;
            SchedulePreview();
        }
    }
    public bool SequenceEnabled { get => _sequenceEnabled; set { if (SetField(ref _sequenceEnabled, value)) SchedulePreview(); } }
    public int SequenceStart { get => _sequenceStart; set { if (SetField(ref _sequenceStart, Math.Max(0, value))) SchedulePreview(); } }
    public int SequenceStep { get => _sequenceStep; set { if (SetField(ref _sequenceStep, Math.Max(1, value))) SchedulePreview(); } }
    public int SequenceDigits { get => _sequenceDigits; set { if (SetField(ref _sequenceDigits, Math.Clamp(value, 1, 12))) SchedulePreview(); } }
    public string SequencePosition { get => _sequencePosition; set { if (SetField(ref _sequencePosition, value)) SchedulePreview(); } }
    public string Separator { get => _separator; set { if (SetField(ref _separator, value)) SchedulePreview(); } }

    public string Query
    {
        get => _query;
        set
        {
            if (!SetField(ref _query, value)) return;
            ScheduleFilterRefresh();
        }
    }

    public bool ShowIssuesOnly
    {
        get => _showIssuesOnly;
        set
        {
            if (!SetField(ref _showIssuesOnly, value)) return;
            ItemsView.Refresh();
        }
    }

    public string SortLabel { get => _sortLabel; private set => SetField(ref _sortLabel, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string PreviewLatencyText { get => _previewLatencyText; private set => SetField(ref _previewLatencyText, value); }
    public bool HasErrors
    {
        get => _hasErrors;
        private set
        {
            if (!SetField(ref _hasErrors, value)) return;
            RaiseActionAvailability();
        }
    }
    public bool HasWarnings { get => _hasWarnings; private set => SetField(ref _hasWarnings, value); }
    public bool IsPreviewBusy
    {
        get => _isPreviewBusy;
        private set
        {
            if (!SetField(ref _isPreviewBusy, value)) return;
            RaiseActionAvailability();
        }
    }
    public bool? AllIncludedState { get => _allIncludedState; private set => SetField(ref _allIncludedState, value); }
    public bool IsEmpty => Items.Count == 0;
    public bool IsTransactionBusy
    {
        get => _isTransactionBusy;
        private set
        {
            if (!SetField(ref _isTransactionBusy, value)) return;
            OnPropertyChanged(nameof(IsWorkspaceEnabled));
            RaiseActionAvailability();
        }
    }
    public bool IsWorkspaceEnabled => !IsTransactionBusy;
    public bool CanExecuteRename => !IsTransactionBusy
        && !IsPreviewBusy
        && !_isPreviewDirty
        && !HasErrors
        && StartupTransactionGate?.CanStartNewTransaction == true
        && Items.Any(x => x.IsIncluded && !x.IsSynthetic && x.Preview.StatusCode == PreviewStatus.Ready)
        && !Items.Any(x => x.IsIncluded && x.IsSynthetic);
    public bool CanUndoLastTransaction => !IsTransactionBusy
        && StartupTransactionGate?.CanStartNewTransaction == true
        && !string.IsNullOrWhiteSpace(_lastUndoableTransactionDirectory);

    /// <summary>
    /// V0.5 in-memory frozen plan. Any naming/order/inclusion change invalidates it immediately.
    /// TransactionEngine will consume this contract in the next stage; V0.5 never persists or executes it.
    /// </summary>
    public RenamePlan? PreparedPlan
    {
        get => _preparedPlan;
        private set
        {
            if (ReferenceEquals(_preparedPlan, value)) return;
            _preparedPlan = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// V0.7.2 read-only startup safety snapshot. Future real Execute wiring must refuse to start a
    /// new transaction unless this gate is Clear, then re-evaluate it immediately before execution.
    /// </summary>
    public TransactionStartupDiscoveryResult? StartupTransactionGate
    {
        get => _startupTransactionGate;
        private set
        {
            if (ReferenceEquals(_startupTransactionGate, value)) return;
            _startupTransactionGate = value;
            OnPropertyChanged();
            RaiseActionAvailability();
        }
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        if (IsTransactionBusy) return;
        // Import de-duplication is namespace-path based, not FileIdentity based. Hard links at different
        // paths remain separate rename items. Case handling follows the directory semantics provider.
        var semanticsCache = new Dictionary<string, PathSemantics>(StringComparer.Ordinal);
        var existing = new HashSet<string>(Items.Select(i => BuildNamespaceImportKey(i.CurrentPath, semanticsCache)), StringComparer.Ordinal);
        var additions = new List<RenameItemViewModel>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var importKey = BuildNamespaceImportKey(path, semanticsCache);
            if (existing.Contains(importKey)) continue;
            try
            {
                var isDirectory = Directory.Exists(path);
                if (!isDirectory && !File.Exists(path)) continue;

                var info = isDirectory ? null : new FileInfo(path);
                var name = isDirectory ? new DirectoryInfo(path).Name : Path.GetFileName(path);
                var extension = isDirectory ? string.Empty : Path.GetExtension(path);
                var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(path);
                var item = new RenameItemViewModel
                {
                    CurrentPath = path,
                    ParentDirectory = Directory.GetParent(path)?.FullName ?? Path.GetDirectoryName(path) ?? string.Empty,
                    CurrentName = name,
                    Stem = stem,
                    Extension = extension,
                    IsDirectory = isDirectory,
                    IsSynthetic = false,
                    ExpectedFileIdentity = _fileIdentityProvider.TryGetIdentity(path, isDirectory),
                    SizeBytes = info?.Length,
                    ModifiedTime = isDirectory ? Directory.GetLastWriteTime(path) : info?.LastWriteTime,
                };
                additions.Add(item);
                existing.Add(importKey);
            }
            catch
            {
                // UI prototype: unreadable paths are ignored. Production ImportService will return structured issues.
            }
        }

        if (additions.Count == 0) return;

        // The frozen UI opens with synthetic demonstration rows. The first real import must replace
        // that non-executable sample set rather than mixing demo namespaces into a real transaction.
        if (Items.Count > 0 && Items.All(x => x.IsSynthetic))
        {
            CancelPreview();
            UnsubscribeAll(Items);
            Items.ReplaceAll([]);
            _orderHistory.Clear();
        }

        SubscribeAll(additions);
        Items.AddRange(additions);
        UpdateInclusionSummary();
        SortLabel = "自定义";
        OnPropertyChanged(nameof(IsEmpty));
        SchedulePreview(immediate: true);
    }

    public void Clear()
    {
        if (IsTransactionBusy) return;
        if (Items.Count == 0) return;
        RememberOrder();
        CancelPreview();
        UnsubscribeAll(Items);
        Items.ReplaceAll([]);
        UpdateInclusionSummary();
        StatusText = "0 项";
        PreviewLatencyText = "预览 —";
        _inputRevision++;
        _isPreviewDirty = false;
        InvalidatePreparedPlan();
        HasErrors = false;
        HasWarnings = false;
        OnPropertyChanged(nameof(IsEmpty));
        RaiseActionAvailability();
    }

    public void RemoveItems(IEnumerable<RenameItemViewModel> items)
    {
        if (IsTransactionBusy) return;
        var remove = items.ToHashSet();
        if (remove.Count == 0) return;
        RememberOrder();
        foreach (var item in remove) item.InclusionChanged -= Item_InclusionChanged;
        Items.ReplaceAll(Items.Where(x => !remove.Contains(x)).ToArray());
        UpdateInclusionSummary();
        SortLabel = "自定义";
        OnPropertyChanged(nameof(IsEmpty));
        SchedulePreview(immediate: true);
    }

    public void SetAllIncluded(bool included)
    {
        if (IsTransactionBusy) return;
        _bulkInclusionDepth++;
        try
        {
            foreach (var item in Items) item.IsIncluded = included;
        }
        finally
        {
            _bulkInclusionDepth--;
        }
        UpdateInclusionSummary();
        SchedulePreview(immediate: true);
    }

    public void MoveItems(IReadOnlyList<RenameItemViewModel> moving, int targetIndex)
    {
        if (IsTransactionBusy) return;
        if (moving.Count == 0) return;
        RememberOrder();
        var movingSet = moving.ToHashSet();
        var source = Items.ToList();
        var orderedMoving = source.Where(movingSet.Contains).ToList();

        // targetIndex is expressed against the original visual list. Removing selected rows that
        // were above the drop slot must shift that slot upward before insertion.
        targetIndex = Math.Clamp(targetIndex, 0, source.Count);
        var removedBeforeTarget = source.Take(targetIndex).Count(movingSet.Contains);
        source.RemoveAll(movingSet.Contains);
        targetIndex = Math.Clamp(targetIndex - removedBeforeTarget, 0, source.Count);
        source.InsertRange(targetIndex, orderedMoving);
        Items.ReplaceAll(source);
        SortLabel = "自定义";
        SchedulePreview(immediate: true);
    }

    public void MoveByOffset(IReadOnlyList<RenameItemViewModel> selected, int offset)
    {
        if (IsTransactionBusy) return;
        if (selected.Count == 0 || offset == 0) return;
        RememberOrder();
        var selectedSet = selected.ToHashSet();
        var list = Items.ToList();

        if (offset < 0)
        {
            for (var i = 1; i < list.Count; i++)
            {
                if (selectedSet.Contains(list[i]) && !selectedSet.Contains(list[i - 1]))
                    (list[i - 1], list[i]) = (list[i], list[i - 1]);
            }
        }
        else
        {
            for (var i = list.Count - 2; i >= 0; i--)
            {
                if (selectedSet.Contains(list[i]) && !selectedSet.Contains(list[i + 1]))
                    (list[i + 1], list[i]) = (list[i], list[i + 1]);
            }
        }

        Items.ReplaceAll(list);
        SortLabel = "自定义";
        SchedulePreview(immediate: true);
    }

    public void MoveToEdge(IReadOnlyList<RenameItemViewModel> selected, bool top)
    {
        if (IsTransactionBusy) return;
        if (selected.Count == 0) return;
        RememberOrder();
        var selectedSet = selected.ToHashSet();
        var moving = Items.Where(selectedSet.Contains).ToList();
        var remaining = Items.Where(x => !selectedSet.Contains(x)).ToList();
        Items.ReplaceAll(top ? moving.Concat(remaining) : remaining.Concat(moving));
        SortLabel = "自定义";
        SchedulePreview(immediate: true);
    }

    public void SortBy(string mode, bool descending = false)
    {
        if (IsTransactionBusy) return;
        RememberOrder();
        IEnumerable<RenameItemViewModel> sorted = mode switch
        {
            "名称" => Items.OrderBy(x => x.CurrentName, NaturalStringComparer.Instance),
            "扩展名" => Items.OrderBy(x => x.Extension, StringComparer.OrdinalIgnoreCase)
                              .ThenBy(x => x.CurrentName, NaturalStringComparer.Instance),
            "修改时间" => Items.OrderBy(x => x.ModifiedTime),
            "大小" => Items.OrderBy(x => x.SizeBytes ?? -1),
            _ => Items,
        };
        if (descending) sorted = sorted.Reverse();
        Items.ReplaceAll(sorted.ToArray());
        SortLabel = $"{mode} {(descending ? "↓" : "↑")}";
        SchedulePreview(immediate: true);
    }

    public bool UndoOrder()
    {
        if (IsTransactionBusy) return false;
        if (_orderHistory.Count == 0) return false;
        var order = _orderHistory.Pop();
        var map = Items.ToDictionary(x => x.Id);
        var restored = order.Where(map.ContainsKey).Select(id => map[id]).ToList();
        var orderSet = order.ToHashSet();
        restored.AddRange(Items.Where(x => !orderSet.Contains(x.Id)));
        Items.ReplaceAll(restored);
        SortLabel = "自定义";
        SchedulePreview(immediate: true);
        return true;
    }

    /// <summary>
    /// Public compatibility hook for the prototype. It schedules instead of synchronously refreshing the grid.
    /// </summary>
    public void RebuildPreview() => SchedulePreview(immediate: true);


    /// <summary>
    /// V0.7.2 app-start discovery. This may read BatchRenamer transaction metadata and current
    /// filesystem identity/path semantics, but it never invokes any rename mutation executor.
    /// </summary>
    public async Task<TransactionStartupDiscoveryResult> EvaluateStartupTransactionGateAsync()
    {
        var transactionsRoot = TransactionStoragePaths.GetDefaultTransactionsRoot();
        var result = await Task.Run(() => TransactionStartupDiscovery.Scan(
            transactionsRoot,
            _fileSystem,
            _pathSemanticsProvider,
            _fileIdentityProvider,
            new SystemExactNamespaceInspector()));
        StartupTransactionGate = result;
        return result;
    }

    /// <summary>
    /// V0.7.3 startup coordinator. Only a catalog containing exclusively RecoveryRequired blockers is
    /// eligible for automatic rollback. ManualRequired/SessionBusy fail closed with zero startup
    /// mutation. The final discovery snapshot is always stored as the authoritative Execute gate.
    /// </summary>
    public async Task<TransactionStartupRecoveryCoordinatorResult> CoordinateStartupTransactionRecoveryAsync()
    {
        var transactionsRoot = TransactionStoragePaths.GetDefaultTransactionsRoot();
        var result = await Task.Run(() => TransactionStartupRecoveryCoordinator.Run(
            transactionsRoot,
            _fileSystem,
            _pathSemanticsProvider,
            _fileIdentityProvider,
            new SystemRenameMutationFileSystem(),
            new SystemExactNamespaceInspector()));
        StartupTransactionGate = result.FinalDiscovery;
        return result;
    }

    /// <summary>
    /// V0.5 dry-run plan gate. It first forces the latest Preview + Validation to complete, then
    /// invokes RenamePlanner which performs its own fresh filesystem validation before freezing
    /// Source/Temp/Target paths. No disk mutation or journal write occurs here.
    /// </summary>
    public async Task<RenamePlanBuildResult> BuildFinalPlanAsync()
    {
        if (IsTransactionBusy) throw new InvalidOperationException("事务操作进行中，不能生成新的执行计划。");
        _previewTimer.Stop();
        _previewGeneration++; // invalidate any older preview task/finalizer before the plan gate starts
        CancelPreview();
        PreparedPlan = null;

        // Freeze a UI-owned snapshot first. Preview generation and final filesystem validation then run
        // against the same immutable input. This closes the 120 ms debounce window completely.
        var revision = _inputRevision;
        var previewInputs = Items.Select(x => new PreviewInputItem(
            x.Id,
            x.ParentDirectory,
            x.CurrentName,
            x.Stem,
            x.Extension,
            x.IsIncluded)).ToArray();
        var validationSeeds = Items.Select(x => new ValidationSeed(
            x.Id,
            x.CurrentPath,
            x.ParentDirectory,
            x.CurrentName,
            x.Extension,
            x.IsDirectory,
            x.IsIncluded,
            x.IsSynthetic,
            x.ExpectedFileIdentity)).ToArray();
        var rules = CaptureRuleSet();

        IsPreviewBusy = true;
        try
        {
            var pipeline = await Task.Run(() =>
            {
                var preview = PreviewEngine.Build(previewInputs, rules);
                var proposed = preview.Items.ToDictionary(x => x.ItemId, x => x.NewName);
                var validationInputs = validationSeeds.Select(x => new ValidationInputItem(
                    x.Id,
                    x.CurrentPath,
                    x.ParentDirectory,
                    x.CurrentName,
                    x.Extension,
                    proposed[x.Id],
                    x.IsDirectory,
                    x.IsIncluded,
                    x.IsSynthetic,
                    x.ExpectedFileIdentity)).ToArray();

                var planResult = RenamePlanner.BuildFinalPlan(
                    validationInputs,
                    _fileSystem,
                    _pathSemanticsProvider,
                    _fileIdentityProvider);
                return (Preview: preview, PlanResult: planResult);
            });

            if (revision != _inputRevision)
            {
                var issues = pipeline.PlanResult.PlannerIssues.Concat([new RenamePlannerIssue(
                    ValidationSeverity.Error,
                    "INPUT_CHANGED_DURING_PLANNING",
                    "生成执行计划期间列表或命名规则发生了变化，请重新生成。")]).ToArray();
                return new RenamePlanBuildResult(
                    null,
                    pipeline.PlanResult.FinalValidation,
                    issues,
                    pipeline.PlanResult.ComputeTime);
            }

            // Keep the visible preview synchronized with the exact names that were sent to Planner.
            var previewMap = pipeline.Preview.Items.ToDictionary(x => x.ItemId);
            var validationMap = pipeline.PlanResult.FinalValidation.Items.ToDictionary(x => x.ItemId);
            foreach (var item in Items)
            {
                if (previewMap.TryGetValue(item.Id, out var preview))
                    item.ApplyPreview(preview, validationMap.GetValueOrDefault(item.Id));
            }
            HasErrors = pipeline.PlanResult.FinalValidation.ErrorItemCount > 0;
            HasWarnings = pipeline.PlanResult.FinalValidation.WarningItemCount > 0;
            var warningPart = pipeline.PlanResult.FinalValidation.WarningItemCount > 0
                ? $" · {pipeline.PlanResult.FinalValidation.WarningItemCount:N0} 警告"
                : string.Empty;
            StatusText = $"{Items.Count:N0} 项 · {pipeline.Preview.ChangedCount:N0} 项待重命名 · {pipeline.Preview.UnchangedCount:N0} 项不变 · {pipeline.PlanResult.FinalValidation.ErrorItemCount:N0} 错误{warningPart}";
            if (ShowIssuesOnly || !string.IsNullOrWhiteSpace(Query)) ItemsView.Refresh();

            _isPreviewDirty = false;
            RaiseActionAvailability();
            if (pipeline.PlanResult.Success) PreparedPlan = pipeline.PlanResult.Plan;
            return pipeline.PlanResult;
        }
        finally
        {
            IsPreviewBusy = false;
        }
    }

    /// <summary>Read-only V0.8 transaction history refresh used to expose only the newest safe Undo.</summary>
    public async Task<TransactionHistoryResult> RefreshTransactionHistoryAsync()
    {
        var transactionsRoot = TransactionStoragePaths.GetDefaultTransactionsRoot();
        var history = await Task.Run(() => TransactionHistoryService.Scan(
            transactionsRoot,
            _fileSystem,
            _pathSemanticsProvider,
            _fileIdentityProvider,
            new SystemExactNamespaceInspector()));
        _lastUndoableTransactionDirectory = history.Entries.FirstOrDefault(x => x.CanUndo)?.TransactionDirectory;
        RaiseActionAvailability();
        return history;
    }

    /// <summary>
    /// V0.10 bounded transaction metadata retention. The service never touches user Source/Temp/Target
    /// namespace and never removes unresolved/manual transaction directories. Cleanup failure is
    /// non-fatal to the active workspace; safety-critical startup/execute gates remain separate.
    /// </summary>
    public async Task<TransactionRetentionResult> CleanupTransactionHistoryAsync()
    {
        var transactionsRoot = TransactionStoragePaths.GetDefaultTransactionsRoot();
        return await Task.Run(() => TransactionRetentionService.Cleanup(
            transactionsRoot,
            _fileSystem,
            _pathSemanticsProvider,
            _fileIdentityProvider,
            new SystemExactNamespaceInspector()));
    }

    /// <summary>
    /// V0.9 real Execute entry used by the frozen UI. The caller must first obtain a fresh
    /// BuildFinalPlanAsync result and explicit user confirmation. The transaction coordinator then
    /// re-checks the global startup gate under a cross-process catalog lease before persisting/moving.
    /// </summary>
    public async Task<TransactionNewExecutionResult> ExecutePreparedPlanAsync(RenamePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(PreparedPlan, plan))
            throw new InvalidOperationException("当前 RenamePlan 已失效，请重新生成预览后再执行。");
        if (IsTransactionBusy)
            throw new InvalidOperationException("已有事务操作正在进行。");

        IsTransactionBusy = true;
        StatusText = $"正在安全执行 {plan.RenameCount:N0} 项重命名…";
        try
        {
            var transactionsRoot = TransactionStoragePaths.GetDefaultTransactionsRoot();
            var result = await Task.Run(() => TransactionNewExecutionCoordinator.Execute(
                plan,
                transactionsRoot,
                _fileSystem,
                _pathSemanticsProvider,
                _fileIdentityProvider,
                new SystemRenameMutationFileSystem(),
                new SystemExactNamespaceInspector()));

            if (result.FinalDiscovery is not null)
                StartupTransactionGate = result.FinalDiscovery;

            if (result.Success)
            {
                ApplyPlanNamespaceToItems(plan, useTarget: true, markCommittedUnchanged: true);
                PreparedPlan = null;
                _inputRevision++;
                HasErrors = false;
                HasWarnings = result.Issues.Any(x => x.Severity == ValidationSeverity.Warning);
                _isPreviewDirty = false;
                var warningPart = HasWarnings ? " · 有事务警告" : string.Empty;
                StatusText = $"{Items.Count:N0} 项 · 已完成 {plan.RenameCount:N0} 项重命名 · 0 错误{warningPart}";
                PreviewLatencyText = $"执行 {result.ComputeTime.TotalMilliseconds:N0} ms";
            }
            else if (result.WasSafelyRolledBack)
            {
                PreparedPlan = null;
                StatusText = "执行未完成，已安全回滚到原始名称";
                SchedulePreview(immediate: true);
            }
            else
            {
                PreparedPlan = null;
                StatusText = result.State == TransactionNewExecutionState.FailedBeforeMutation
                    ? "执行前检查失败，未修改任何文件"
                    : "事务未完成，执行入口已安全锁定";
                SchedulePreview(immediate: true);
            }

            await CleanupTransactionHistoryAsync();
            await RefreshTransactionHistoryAsync();
            return result;
        }
        finally
        {
            IsTransactionBusy = false;
        }
    }

    /// <summary>Undo the newest history entry that is still proven safe by V0.8 history reconciliation.</summary>
    public async Task<TransactionUserUndoCoordinatorResult?> UndoLastTransactionAsync()
    {
        if (IsTransactionBusy) return null;
        await RefreshTransactionHistoryAsync();
        var transactionDirectory = _lastUndoableTransactionDirectory;
        if (string.IsNullOrWhiteSpace(transactionDirectory)) return null;

        IsTransactionBusy = true;
        StatusText = "正在安全撤销上一次重命名…";
        try
        {
            var transactionsRoot = TransactionStoragePaths.GetDefaultTransactionsRoot();
            var result = await Task.Run(() => TransactionUserUndoCoordinator.Undo(
                transactionDirectory,
                transactionsRoot,
                _fileSystem,
                _pathSemanticsProvider,
                _fileIdentityProvider,
                new SystemRenameMutationFileSystem(),
                new SystemExactNamespaceInspector()));

            if (result.FinalDiscovery is not null)
                StartupTransactionGate = result.FinalDiscovery;

            var plan = result.Undo?.Plan;
            if (result.Success && plan is not null)
            {
                ApplyPlanNamespaceToItems(plan, useTarget: false, markCommittedUnchanged: false);
                PreparedPlan = null;
                _inputRevision++;
                StatusText = $"已撤销 {plan.RenameCount:N0} 项重命名";
                SchedulePreview(immediate: true);
            }
            else
            {
                StatusText = result.State == TransactionUserUndoCoordinatorState.NotEligible
                    ? "上一次事务已不满足安全撤销条件"
                    : "撤销未完成，事务入口已安全锁定";
            }

            await CleanupTransactionHistoryAsync();
            await RefreshTransactionHistoryAsync();
            return result;
        }
        finally
        {
            IsTransactionBusy = false;
        }
    }

    private void ApplyPlanNamespaceToItems(RenamePlan plan, bool useTarget, bool markCommittedUnchanged)
    {
        var byId = Items.ToDictionary(x => x.Id);
        var consumed = new HashSet<RenameItemViewModel>();
        foreach (var entry in plan.Entries)
        {
            RenameItemViewModel? item = null;
            if (byId.TryGetValue(entry.ItemId, out var stableItem))
            {
                item = stableItem;
            }
            else
            {
                // After an app restart, imported rows have fresh UI ItemIds even though the persisted
                // transaction still owns the same namespace object. Fall back to the exact namespace
                // that must exist immediately before this reconciliation (Source before Execute,
                // Target before Undo), never to FileIdentity-only matching because hard links are
                // separate renameable namespace entries.
                var expectedCurrentPath = useTarget ? entry.SourcePath : entry.TargetPath;
                item = Items.FirstOrDefault(x => !consumed.Contains(x)
                    && x.IsDirectory == entry.IsDirectory
                    && NamespacePathEquals(x.CurrentPath, expectedCurrentPath));
            }

            if (item is null) continue;
            consumed.Add(item);
            var path = useTarget ? entry.TargetPath : entry.SourcePath;
            var identity = _fileIdentityProvider.TryGetIdentity(path, entry.IsDirectory);
            long? size = null;
            DateTime? modified = null;
            try
            {
                if (entry.IsDirectory)
                {
                    modified = Directory.GetLastWriteTime(path);
                }
                else if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    size = info.Length;
                    modified = info.LastWriteTime;
                }
            }
            catch
            {
                // Identity/path state was already proven by the transaction result. Optional display
                // metadata is best-effort and must never reinterpret transaction success.
            }

            item.ApplyNamespaceSnapshot(path, identity, size, modified);
            if (markCommittedUnchanged)
                item.MarkCommittedUnchanged(Items.IndexOf(item) + 1);
        }

        if (ShowIssuesOnly || !string.IsNullOrWhiteSpace(Query)) ItemsView.Refresh();
        RaiseActionAvailability();
    }

    private bool NamespacePathEquals(string left, string right)
    {
        try
        {
            var fullLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(fullRight) ?? string.Empty;
            var semantics = _pathSemanticsProvider.GetSemantics(parent);
            return string.Equals(fullLeft, fullRight, semantics.NameComparison);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RaiseActionAvailability()
    {
        OnPropertyChanged(nameof(CanExecuteRename));
        OnPropertyChanged(nameof(CanUndoLastTransaction));
    }

    public void Dispose()
    {
        _previewTimer.Stop();
        _filterTimer.Stop();
        CancelPreview();
        UnsubscribeAll(Items);
    }

    private void SchedulePreview(bool immediate = false)
    {
        _inputRevision++;
        _isPreviewDirty = true;
        InvalidatePreparedPlan();
        RaiseActionAvailability();
        _previewTimer.Stop();
        _previewTimer.Interval = immediate ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(120);
        _previewTimer.Start();
    }

    private async void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        await StartPreviewAsync();
    }

    private async Task StartPreviewAsync()
    {
        CancelPreview();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        var generation = ++_previewGeneration;

        // Snapshot UI-owned mutable state before leaving the UI thread. Real filesystem validation
        // runs only against immutable records, never against live WPF objects.
        var previewInputs = Items.Select(x => new PreviewInputItem(
            x.Id,
            x.ParentDirectory,
            x.CurrentName,
            x.Stem,
            x.Extension,
            x.IsIncluded)).ToArray();
        var validationSeeds = Items.Select(x => new ValidationSeed(
            x.Id,
            x.CurrentPath,
            x.ParentDirectory,
            x.CurrentName,
            x.Extension,
            x.IsDirectory,
            x.IsIncluded,
            x.IsSynthetic,
            x.ExpectedFileIdentity)).ToArray();
        var rules = CaptureRuleSet();

        IsPreviewBusy = true;
        try
        {
            var wall = Stopwatch.StartNew();
            var pipeline = await Task.Run(() =>
            {
                var preview = PreviewEngine.Build(previewInputs, rules, cts.Token);
                var proposed = preview.Items.ToDictionary(x => x.ItemId, x => x.NewName);
                var validationInputs = validationSeeds.Select(x => new ValidationInputItem(
                    x.Id,
                    x.CurrentPath,
                    x.ParentDirectory,
                    x.CurrentName,
                    x.Extension,
                    proposed[x.Id],
                    x.IsDirectory,
                    x.IsIncluded,
                    x.IsSynthetic,
                    x.ExpectedFileIdentity)).ToArray();
                var validation = ValidationEngine.Validate(
                    validationInputs,
                    _fileSystem,
                    _pathSemanticsProvider,
                    _fileIdentityProvider,
                    cts.Token);
                return (Preview: preview, Validation: validation);
            }, cts.Token);
            wall.Stop();

            if (cts.IsCancellationRequested || generation != _previewGeneration) return;
            var previewMap = pipeline.Preview.Items.ToDictionary(x => x.ItemId);
            var validationMap = pipeline.Validation.Items.ToDictionary(x => x.ItemId);
            foreach (var item in Items)
            {
                if (previewMap.TryGetValue(item.Id, out var preview))
                    item.ApplyPreview(preview, validationMap.GetValueOrDefault(item.Id));
            }

            HasErrors = pipeline.Validation.ErrorItemCount > 0;
            HasWarnings = pipeline.Validation.WarningItemCount > 0;
            var warningPart = pipeline.Validation.WarningItemCount > 0
                ? $" · {pipeline.Validation.WarningItemCount:N0} 警告"
                : string.Empty;
            StatusText = $"{Items.Count:N0} 项 · {pipeline.Preview.ChangedCount:N0} 项待重命名 · {pipeline.Preview.UnchangedCount:N0} 项不变 · {pipeline.Validation.ErrorItemCount:N0} 错误{warningPart}";
            PreviewLatencyText = Items.Count >= 1000
                ? $"预览+校验 {wall.Elapsed.TotalMilliseconds:N0} ms"
                : $"预览+校验 {wall.Elapsed.TotalMilliseconds:N1} ms";
            _isPreviewDirty = false;
            RaiseActionAvailability();

            // Refresh only when an active filter depends on preview/validation fields.
            if (ShowIssuesOnly || !string.IsNullOrWhiteSpace(Query)) ItemsView.Refresh();
        }
        catch (OperationCanceledException)
        {
            // Superseded generation: expected during fast typing.
        }
        finally
        {
            if (generation == _previewGeneration) IsPreviewBusy = false;
        }
    }

    private void InvalidatePreparedPlan()
    {
        if (PreparedPlan is not null) PreparedPlan = null;
    }

    private RenameRuleSet CaptureRuleSet()
    {
        var originalMode = OriginalMode switch
        {
            "放在基础名称前" => OriginalNameMode.BeforeBaseName,
            "放在基础名称后" => OriginalNameMode.AfterBaseName,
            _ => OriginalNameMode.None,
        };
        var caseMode = EnableCaseConversion
            ? CaseMode switch
            {
                "全部小写" => NameCaseMode.Lower,
                "全部大写" => NameCaseMode.Upper,
                "单词首字母大写" => NameCaseMode.TitleCaseWords,
                _ => NameCaseMode.Unchanged,
            }
            : NameCaseMode.Unchanged;
        var position = SequencePosition == "名称前"
            ? BatchRenamer.Core.SequencePosition.BeforeName
            : BatchRenamer.Core.SequencePosition.AfterName;

        return new RenameRuleSet(
            BaseName,
            originalMode,
            Prefix,
            Suffix,
            EnableFindReplace ? SearchText : string.Empty,
            EnableFindReplace ? ReplaceText : string.Empty,
            caseMode,
            new SequenceConfig(
                SequenceEnabled,
                SequenceStart,
                SequenceStep,
                SequenceDigits,
                position,
                Separator));
    }

    private void ScheduleFilterRefresh()
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private bool FilterItem(object obj)
    {
        if (obj is not RenameItemViewModel item) return false;
        if (ShowIssuesOnly && !item.Preview.HasIssue) return false;
        if (string.IsNullOrWhiteSpace(Query)) return true;
        var q = Query.Trim();
        return item.CurrentName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || item.Preview.NewName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || item.ParentDirectory.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Item_InclusionChanged(object? sender, EventArgs e)
    {
        if (_bulkInclusionDepth > 0) return;
        UpdateInclusionSummary();
        SchedulePreview();
    }

    private void UpdateInclusionSummary()
    {
        if (Items.Count == 0)
        {
            AllIncludedState = false;
            return;
        }

        var included = 0;
        foreach (var item in Items)
        {
            if (item.IsIncluded) included++;
        }

        AllIncludedState = included == 0 ? false : included == Items.Count ? true : null;
    }

    private void SubscribeAll(IEnumerable<RenameItemViewModel> items)
    {
        foreach (var item in items) item.InclusionChanged += Item_InclusionChanged;
    }

    private void UnsubscribeAll(IEnumerable<RenameItemViewModel> items)
    {
        foreach (var item in items) item.InclusionChanged -= Item_InclusionChanged;
    }

    private string BuildNamespaceImportKey(string path, Dictionary<string, PathSemantics> semanticsCache)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmed)) trimmed = full;
            var parent = Path.GetDirectoryName(trimmed) ?? string.Empty;
            var name = Path.GetFileName(trimmed);
            if (!semanticsCache.TryGetValue(parent, out var semantics))
            {
                semantics = _pathSemanticsProvider.GetSemantics(parent);
                semanticsCache[parent] = semantics;
            }
            return semantics.IsCaseSensitive
                ? parent + "\u001F" + name
                : parent.ToUpperInvariant() + "\u001F" + name.ToUpperInvariant();
        }
        catch
        {
            return path;
        }
    }

    private RenameItemViewModel CreateSyntheticItem(string path, int index)
    {
        var extension = Path.GetExtension(path);
        var name = Path.GetFileName(path);
        return new RenameItemViewModel
        {
            CurrentPath = path,
            ParentDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            CurrentName = name,
            Stem = Path.GetFileNameWithoutExtension(path),
            Extension = extension,
            IsSynthetic = true,
            ExpectedFileIdentity = null,
            ModifiedTime = DateTime.Now.AddSeconds(-index * 17),
            SizeBytes = 1_000_000 + (index % 400) * 24_500,
        };
    }

    private void CancelPreview()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private void RememberOrder()
    {
        _orderHistory.Push(Items.Select(x => x.Id).ToList());
        if (_orderHistory.Count <= 50) return;
        var newest = _orderHistory.Take(50).Reverse().ToArray();
        _orderHistory.Clear();
        foreach (var snapshot in newest) _orderHistory.Push(snapshot);
    }

    private sealed record ValidationSeed(
        Guid Id,
        string CurrentPath,
        string ParentDirectory,
        string CurrentName,
        string Extension,
        bool IsDirectory,
        bool IsIncluded,
        bool IsSynthetic,
        FileIdentity? ExpectedFileIdentity);

    public event PropertyChangedEventHandler? PropertyChanged;

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

internal sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;
        var ix = 0;
        var iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                var sx = ix;
                var sy = iy;
                while (sx < x.Length && x[sx] == '0') sx++;
                while (sy < y.Length && y[sy] == '0') sy++;
                var ex = sx;
                var ey = sy;
                while (ex < x.Length && char.IsDigit(x[ex])) ex++;
                while (ey < y.Length && char.IsDigit(y[ey])) ey++;
                var lenX = ex - sx;
                var lenY = ey - sy;
                if (lenX != lenY) return lenX.CompareTo(lenY);
                var numeric = string.Compare(x, sx, y, sy, lenX, StringComparison.Ordinal);
                if (numeric != 0) return numeric;
                var zeroCountX = sx - ix;
                var zeroCountY = sy - iy;
                if (zeroCountX != zeroCountY) return zeroCountX.CompareTo(zeroCountY);
                ix = ex;
                iy = ey;
                continue;
            }
            var cx = char.ToUpperInvariant(x[ix]);
            var cy = char.ToUpperInvariant(y[iy]);
            if (cx != cy) return cx.CompareTo(cy);
            ix++;
            iy++;
        }
        return x.Length.CompareTo(y.Length);
    }
}
