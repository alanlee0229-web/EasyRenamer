using BatchRenamer.App.Models;
using BatchRenamer.Core;
using BatchRenamer.Transaction;

namespace BatchRenamer.App.ViewModels;

public sealed partial class MainViewModel
{
    partial void InitializeInternalTools() => SeedDemoData();

    internal void SeedDemoData() => SeedSyntheticData(7, realisticNames: true);

    internal void SeedSyntheticData(int count, bool realisticNames = false)
    {
        if (IsTransactionBusy) return;
        CancelPreview();
        UnsubscribeAll(Items);

        var list = new List<RenameItemViewModel>(count);
        if (realisticNames && count == 7)
        {
            var demo = new[]
            {
                @"D:\旅行\IMG_8891.JPG",
                @"D:\旅行\IMG_8872.JPG",
                @"D:\旅行\IMG_9003.JPG",
                @"D:\旅行\IMG_9012.JPG",
                @"D:\项目资料\封面.png",
                @"D:\项目资料\结果图 2.png",
                @"D:\项目资料\结果图 10.png",
            };
            for (var i = 0; i < demo.Length; i++) list.Add(CreateSyntheticItem(demo[i], i));
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var folder = i % 5 == 0 ? @"D:\项目资料" : @"D:\旅行";
                var ext = i % 11 == 0 ? ".png" : ".JPG";
                var path = $@"{folder}\IMG_{i + 1:D6}{ext}";
                list.Add(CreateSyntheticItem(path, i));
            }
        }

        SubscribeAll(list);
        Items.ReplaceAll(list);
        UpdateInclusionSummary();
        _orderHistory.Clear();
        SortLabel = "自定义";
        OnPropertyChanged(nameof(IsEmpty));
        RaiseActionAvailability();
        SchedulePreview(immediate: true);
    }

    internal async Task<TransactionFoundationDiagnosticResult> PrepareTransactionFoundationAsync()
    {
        var planBuild = await BuildFinalPlanAsync();
        if (!planBuild.Success || planBuild.Plan is null)
            return new TransactionFoundationDiagnosticResult(planBuild, null, null);

        var transactionsRoot = TransactionStoragePaths.GetDefaultTransactionsRoot();
        var persistence = await Task.Run(() =>
            RenamePlanPersistence.PersistNew(planBuild.Plan, transactionsRoot));
        if (!persistence.Success || persistence.PersistedPlan is null)
            return new TransactionFoundationDiagnosticResult(planBuild, persistence, null);

        var preflight = await Task.Run(() => TransactionPreflight.Validate(
            persistence.PersistedPlan,
            _fileSystem,
            _pathSemanticsProvider,
            _fileIdentityProvider));

        return new TransactionFoundationDiagnosticResult(planBuild, persistence, preflight);
    }
}

internal sealed record TransactionFoundationDiagnosticResult(
    RenamePlanBuildResult PlanBuild,
    RenamePlanPersistenceResult? Persistence,
    TransactionPreflightResult? Preflight)
{
    public bool Success => PlanBuild.Success
                           && Persistence?.Success == true
                           && Preflight?.CanExecute == true;
}
