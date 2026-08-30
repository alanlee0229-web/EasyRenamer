using System.Windows;
using System.Windows.Input;
using BatchRenamer.Core;

namespace BatchRenamer.App;

public partial class MainWindow
{
    partial void InitializeInternalTools()
    {
        Title = "easy重命名 — INTERNAL TEST";
        AppTitleBar.Title = "easy重命名 — INTERNAL TEST";
        PreviewKeyDown += InternalTools_PreviewKeyDown;
    }

    private async void InternalTools_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.IsTransactionBusy) return;

        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            var result = await _vm.PrepareTransactionFoundationAsync();
            if (result.Success
                && result.PlanBuild.Plan is { } plan
                && result.Persistence is { } persistence
                && result.Preflight is { } preflight)
            {
                var warningText = preflight.WarningCount > 0
                    ? $"\nPreflight 警告：{preflight.WarningCount:N0}"
                    : string.Empty;
                MessageBox.Show(
                    this,
                    $"RenamePlan 已完成安全准备。\n\n" +
                    $"计划项：{plan.RenameCount:N0}\n" +
                    $"Transaction ID：{plan.TransactionId}\n" +
                    $"Schema：{plan.SchemaVersion}\n" +
                    $"SHA256：{persistence.Sha256}\n" +
                    $"plan.json：{persistence.PlanPath}\n" +
                    $"Preflight：PASS{warningText}\n\n" +
                    "该入口只写事务元数据，不修改用户文件名。",
                    "Internal QA — Transaction Foundation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var plannerMessage = result.PlanBuild.PlannerIssues
                    .FirstOrDefault(x => x.Severity == ValidationSeverity.Error)?.Message;
                var persistenceMessage = result.Persistence?.Issues
                    .FirstOrDefault(x => x.Severity == ValidationSeverity.Error)?.Message;
                var preflightMessage = result.Preflight?.Issues
                    .FirstOrDefault(x => x.Severity == ValidationSeverity.Error)?.Message;
                var message = preflightMessage
                              ?? persistenceMessage
                              ?? plannerMessage
                              ?? (result.PlanBuild.FinalValidation.ErrorItemCount > 0
                                  ? $"最终校验存在 {result.PlanBuild.FinalValidation.ErrorItemCount} 个错误。"
                                  : "当前事务计划未通过安全准备。");
                MessageBox.Show(this, message, "Internal QA — Transaction Foundation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.T && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.SeedSyntheticData(20_000);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.SeedDemoData();
            e.Handled = true;
        }
    }
}
