using System.Windows.Input;

namespace BatchRenamer.App;

public partial class MainWindow
{
    private InternalQaCenterWindow? _internalQaCenter;

    partial void InitializeInternalTools()
    {
        Title = "easy重命名 — INTERNAL TEST";
        AppTitleBar.Title = "easy重命名 — INTERNAL TEST";
        PreviewKeyDown += InternalTools_PreviewKeyDown;
    }

    private void InternalTools_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.IsTransactionBusy) return;

        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenInternalQaCenter();
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

    private void OpenInternalQaCenter()
    {
        if (_internalQaCenter is not null)
        {
            _internalQaCenter.Activate();
            return;
        }

        _internalQaCenter = new InternalQaCenterWindow(_vm)
        {
            Owner = this,
        };
        _internalQaCenter.Closed += (_, _) => _internalQaCenter = null;
        _internalQaCenter.Show();
    }
}
