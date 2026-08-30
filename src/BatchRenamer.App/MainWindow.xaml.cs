using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BatchRenamer.App.Dialogs;
using BatchRenamer.App.Models;
using BatchRenamer.App.ViewModels;
using BatchRenamer.Core;
using BatchRenamer.Transaction;
using Microsoft.Win32;

namespace BatchRenamer.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _vm = new();
    private Point _dragStart;
    private DataGridRow? _dragRow;
    private DataGridRow? _dragOverRow;
    private bool _dropAfterRow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        InitializeInternalTools();
        RefreshEmptyState();
        _vm.Items.CollectionChanged += (_, _) => RefreshEmptyState();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        TransactionStartupRecoveryCoordinatorResult startup;
        try
        {
            startup = await _vm.CoordinateStartupTransactionRecoveryAsync();
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                this,
                "事务恢复检查",
                $"启动事务恢复检查失败。为安全起见，后续真实执行入口不会放行。\n\n{ex.GetType().Name}: {ex.Message}",
                AppDialogKind.Warning);
            return;
        }

        if (startup.State == TransactionStartupRecoveryCoordinatorState.ClearNoAction
            && startup.CanStartNewTransaction)
        {
            await RefreshHistoryWithRetentionAsync();
            return;
        }

        if (startup.State == TransactionStartupRecoveryCoordinatorState.AutoRecoveryCompleted
            && startup.CanStartNewTransaction)
        {
            AppDialog.Show(
                this,
                "事务已自动恢复",
                $"检测到 {startup.AutoRecoveredCount:N0} 个上次未完成的重命名事务。" +
                "\n已根据冻结计划、Journal 与当前文件系统状态完成安全自动回滚，原始命名空间已恢复。" +
                $"\n\n事务目录：{startup.FinalDiscovery.TransactionsRoot}",
                AppDialogKind.Success);
            await RefreshHistoryWithRetentionAsync();
            return;
        }

        var gate = startup.FinalDiscovery;
        var summary = startup.State switch
        {
            TransactionStartupRecoveryCoordinatorState.BlockedSessionBusy
                => $"检测到 {gate.SessionBusyCount:N0} 个事务正在被其他 BatchRenamer session 占用；启动阶段不会自动移动任何文件。",
            TransactionStartupRecoveryCoordinatorState.ManualRequired
                => $"检测到 {gate.ManualRequiredCount:N0} 个事务无法安全自动判断，需要人工恢复/审计；启动阶段不会自动移动任何文件。",
            TransactionStartupRecoveryCoordinatorState.RecoveryIncomplete
                => $"遗留事务自动恢复未能完整结束，当前仍有 {gate.RecoveryRequiredCount:N0} 个事务需要恢复。",
            _ => "事务恢复 Gate 未清除。",
        };

        AppDialog.Show(
            this,
            "事务恢复需要处理",
            $"{summary}\n\n在恢复 Gate 清除前，真实执行入口会保持锁定。" +
            $"\n\n事务目录：{gate.TransactionsRoot}",
            AppDialogKind.Warning);
    }

    private async Task RefreshHistoryWithRetentionAsync()
    {
        try
        {
            await _vm.CleanupTransactionHistoryAsync();
        }
        catch
        {
            // Retention is metadata housekeeping only. It must never weaken or replace the startup
            // recovery gate, and a cleanup failure must not block otherwise safe normal use.
        }

        await _vm.RefreshTransactionHistoryAsync();
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加文件",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) _vm.AddPaths(dialog.FileNames);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "添加文件夹（只添加文件夹本身，不递归）",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true) _vm.AddPaths(dialog.FolderNames);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _vm.Clear();

    private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox) _vm.SetAllIncluded(checkBox.IsChecked == true);
    }

    private IReadOnlyList<RenameItemViewModel> SelectedItems()
        => FilesGrid.SelectedItems.Cast<RenameItemViewModel>().ToList();

    private void MoveUp_Click(object sender, RoutedEventArgs e) => _vm.MoveByOffset(SelectedItems(), -1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => _vm.MoveByOffset(SelectedItems(), 1);

    private void Settings_Click(object sender, RoutedEventArgs e)
        => SettingsOverlay.Visibility = Visibility.Visible;

    private void SettingsClose_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void SettingsBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseSettings();
        e.Handled = true;
    }

    private void CloseSettings() => SettingsOverlay.Visibility = Visibility.Collapsed;

    private void UndoOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.UndoOrder()) System.Media.SystemSounds.Beep.Play();
    }

    private void SortMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string tag }) return;
        var parts = tag.Split('|');
        if (parts.Length != 2) return;
        _vm.SortBy(parts[0], parts[1] == "desc");
    }

    private async void ExecuteRename_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanExecuteRename) return;

        RenamePlanBuildResult planBuild;
        try
        {
            planBuild = await _vm.BuildFinalPlanAsync();
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, "无法执行重命名", $"生成最终执行计划失败。\n\n{ex.GetType().Name}: {ex.Message}", AppDialogKind.Error);
            return;
        }

        if (!planBuild.Success || planBuild.Plan is not { } plan)
        {
            var message = planBuild.PlannerIssues.FirstOrDefault(x => x.Severity == BatchRenamer.Core.ValidationSeverity.Error)?.Message
                          ?? (planBuild.FinalValidation.ErrorItemCount > 0
                              ? $"最终校验发现 {planBuild.FinalValidation.ErrorItemCount:N0} 个错误，请先处理列表中的红色项目。"
                              : "当前没有可安全执行的重命名计划。");
            AppDialog.Show(this, "无法执行重命名", message, AppDialogKind.Warning);
            return;
        }

        var warningLine = planBuild.FinalValidation.WarningItemCount > 0
            ? $"\n其中有 {planBuild.FinalValidation.WarningItemCount:N0} 个警告，请确认后继续。"
            : string.Empty;
        var confirm = AppDialog.Confirm(
            this,
            "确认执行重命名",
            $"即将重命名 {plan.RenameCount:N0} 个项目。{warningLine}\n\n" +
            "系统会先冻结执行计划，再通过两阶段事务完成重命名。执行记录会持续写入 Journal；若中途异常，将按安全恢复协议回滚。\n\n" +
            "执行成功后可使用“撤销上次”。",
            primaryText: "确认执行",
            secondaryText: "取消");
        if (!confirm) return;

        TransactionNewExecutionResult result;
        try
        {
            result = await _vm.ExecutePreparedPlanAsync(plan);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, "重命名事务异常", $"事务执行异常。下次启动仍会执行恢复检查。\n\n{ex.GetType().Name}: {ex.Message}", AppDialogKind.Error);
            return;
        }

        if (result.Success)
        {
            AppDialog.Show(
                this,
                "重命名完成",
                $"已安全完成 {plan.RenameCount:N0} 项重命名。\n\nTransaction ID：{plan.TransactionId}\n可使用“撤销上次”恢复原名称。",
                AppDialogKind.Success);
            return;
        }

        if (result.WasSafelyRolledBack)
        {
            AppDialog.Show(this, "执行失败，已回滚", "重命名未能完整执行，但程序已根据冻结计划和 Journal 自动恢复到原始名称。没有保留半完成的批次。", AppDialogKind.Warning);
            return;
        }

        var detail = result.Issues.FirstOrDefault(x => x.Severity == BatchRenamer.Core.ValidationSeverity.Error)?.Message;
        var summary = result.State switch
        {
            TransactionNewExecutionState.CatalogBusy => "另一个 BatchRenamer 会话正在执行事务操作，本次没有修改文件。",
            TransactionNewExecutionState.StartupGateBlocked => "当前存在未清除的事务恢复 Gate，本次没有开始新的重命名。",
            TransactionNewExecutionState.PersistenceFailed => "冻结计划无法安全持久化，本次没有开始文件重命名。",
            TransactionNewExecutionState.FailedBeforeMutation => "执行前最终检查未通过，本次没有修改任何文件。",
            _ => "事务未能安全结束。执行入口将保持受恢复 Gate 保护，请不要手工处理临时文件。",
        };
        AppDialog.Show(this, "重命名未完成", string.IsNullOrWhiteSpace(detail) ? summary : $"{summary}\n\n{detail}", AppDialogKind.Warning);
    }

    private async void UndoRename_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanUndoLastTransaction) return;
        if (!AppDialog.Confirm(
                this,
                "确认撤销",
                "将按照最近一次已完成事务的冻结计划恢复原始名称。撤销仍使用两阶段安全事务，不会覆盖外部新文件。",
                primaryText: "确认撤销",
                secondaryText: "取消"))
            return;

        TransactionUserUndoCoordinatorResult? result;
        try
        {
            result = await _vm.UndoLastTransactionAsync();
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, "撤销异常", $"撤销事务异常。下次启动仍会执行恢复检查。\n\n{ex.GetType().Name}: {ex.Message}", AppDialogKind.Error);
            return;
        }

        if (result is null)
        {
            AppDialog.Show(this, "无法撤销", "当前没有仍满足安全条件的可撤销事务。", AppDialogKind.Information);
            return;
        }

        if (result.Success)
        {
            AppDialog.Show(this, "撤销完成", "上一次重命名已安全撤销。", AppDialogKind.Success);
            return;
        }

        var detail = result.Issues.FirstOrDefault(x => x.Severity == BatchRenamer.Core.ValidationSeverity.Error)?.Message;
        var summary = result.State switch
        {
            TransactionUserUndoCoordinatorState.CatalogBusy => "另一个 BatchRenamer 会话正在执行事务操作，本次没有开始撤销。",
            TransactionUserUndoCoordinatorState.StartupGateBlocked => "当前事务恢复 Gate 未清除，本次没有开始撤销。",
            TransactionUserUndoCoordinatorState.NotEligible => "该事务已不满足安全撤销条件，可能存在外部修改或目标对象已变化。",
            _ => "撤销未能安全完成。请保留事务目录并让程序在下次启动时继续恢复检查。",
        };
        AppDialog.Show(this, "撤销未完成", string.IsNullOrWhiteSpace(detail) ? summary : $"{summary}\n\n{detail}", AppDialogKind.Warning);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (_vm.IsTransactionBusy)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_vm.IsTransactionBusy) return;
        ClearDropIndicator();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _vm.AddPaths(paths);
    }

    private void FilesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FilesGrid);
        _dragRow = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
    }

    private void FilesGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragRow?.Item is not RenameItemViewModel rowItem) return;
        var current = e.GetPosition(FilesGrid);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var moving = SelectedItems().ToList();
        if (!moving.Contains(rowItem)) moving = [rowItem];
        var data = new DataObject("BatchRenamer.InternalRows", moving);
        DragDrop.DoDragDrop(FilesGrid, data, DragDropEffects.Move);
        ClearDropIndicator();
    }

    private void FilesGrid_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("BatchRenamer.InternalRows")) return;

        var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        if (!ReferenceEquals(row, _dragOverRow))
        {
            ClearDropIndicator();
            _dragOverRow = row;
        }

        if (row is not null)
        {
            _dropAfterRow = e.GetPosition(row).Y >= row.ActualHeight / 2;
            row.BorderBrush = TryFindResource("PrimaryBrush") as System.Windows.Media.Brush
                              ?? System.Windows.Media.Brushes.DodgerBlue;
            row.BorderThickness = _dropAfterRow
                ? new Thickness(0, 0, 0, 2)
                : new Thickness(0, 2, 0, 0);
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void FilesGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("BatchRenamer.InternalRows") is not List<RenameItemViewModel> moving)
        {
            ClearDropIndicator();
            return;
        }

        var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        var dropAfter = _dropAfterRow;
        var targetIndex = row?.Item is RenameItemViewModel target
            ? _vm.Items.IndexOf(target) + (dropAfter ? 1 : 0)
            : _vm.Items.Count;

        ClearDropIndicator();
        _vm.MoveItems(moving, targetIndex);
        e.Handled = true;
    }

    private void ClearDropIndicator()
    {
        if (_dragOverRow is not null)
        {
            _dragOverRow.ClearValue(DataGridRow.BorderBrushProperty);
            _dragOverRow.ClearValue(DataGridRow.BorderThicknessProperty);
        }
        _dragOverRow = null;
        _dropAfterRow = false;
    }

    private void FilesGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        _vm.RemoveItems(SelectedItems());
        e.Handled = true;
    }


    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Desktop-tool focus semantics: clicking away from a text field exits text-entry mode.
        // Do not handle the event; the clicked control must still receive its normal input.
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) return;
        if (Keyboard.FocusedElement is TextBox) Keyboard.ClearFocus();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.IsTransactionBusy)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && SettingsOverlay.Visibility != Visibility.Visible)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SettingsOverlay.Visibility == Visibility.Visible)
        {
            CloseSettings();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && Keyboard.FocusedElement is TextBox)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

    }

    partial void InitializeInternalTools();

    private void Window_Closed(object? sender, EventArgs e) => _vm.Dispose();

    private void RefreshEmptyState()
        => EmptyState.Visibility = _vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T result) return result;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
