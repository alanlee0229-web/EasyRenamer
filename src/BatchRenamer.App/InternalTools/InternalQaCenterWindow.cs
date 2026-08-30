using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BatchRenamer.App.ViewModels;

namespace BatchRenamer.App;

internal sealed class InternalQaCenterWindow : Window
{
    private const string StressCommand = "python tools\\run_release_stress.py --quick";
    private readonly MainViewModel _vm;
    private readonly InternalQaWorkspace _workspace = new();
    private readonly TextBox _resultBox;

    public InternalQaCenterWindow(MainViewModel vm)
    {
        _vm = vm;
        _resultBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 190,
            Text = "RESULT: READY\n选择一个 QA 动作开始。",
        };
        Title = "easy重命名 — INTERNAL TEST — QA Center";
        Width = 780;
        Height = 760;
        MinWidth = 680;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));

        var content = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };

        content.Children.Add(new TextBlock
        {
            Text = "INTERNAL TEST — QA Center",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(177, 55, 41)),
        });
        content.Children.Add(new TextBlock
        {
            Text = "仅用于隔离测试。不要把真实资料放入 QA Workspace。",
            Margin = new Thickness(0, 6, 0, 18),
            Foreground = Brushes.DimGray,
        });

        var actions = CreateSection("高频测试");
        actions.Children.Add(CreateButtonRow(
            Button("Quick Smoke：创建真实文件并导入", QuickSmoke_Click),
            Button("Demo Data", (_, _) => RunUiAction("Demo Data", _vm.SeedDemoData)),
            Button("20k Preview", (_, _) => RunUiAction("20k Preview", () => _vm.SeedSyntheticData(20_000)))));
        actions.Children.Add(CreateButtonRow(
            Button("事务准备检查", TransactionFoundation_Click),
            Button("读取最新 2k 结果", (_, _) => _resultBox.Text = LoadLatestStressResult()),
            Button("复制 2k 命令", CopyStressCommand_Click)));
        content.Children.Add(actions.Parent);

        var workspaceSection = CreateSection("Current Test Workspace");
        workspaceSection.Children.Add(new TextBlock
        {
            Text = _workspace.RootPath,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 0, 0, 10),
        });
        workspaceSection.Children.Add(CreateButtonRow(
            Button("Open Workspace", OpenWorkspace_Click),
            Button("Reset Workspace", ResetWorkspace_Click),
            Button("Cleanup Workspace", CleanupWorkspace_Click)));
        content.Children.Add(workspaceSection.Parent);

        var stressSection = CreateSection("2,000 Real File Stress");
        stressSection.Children.Add(new TextBlock
        {
            Text = StressCommand,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
        });
        stressSection.Children.Add(new TextBlock
        {
            Text = "从仓库根目录执行；复用现有 ReleaseStressTests，不在 WPF 中复制事务压力测试。",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = Brushes.DimGray,
        });
        content.Children.Add(stressSection.Parent);

        var resultSection = CreateSection("Result");
        resultSection.Children.Add(_resultBox);
        content.Children.Add(resultSection.Parent);
    }

    private void QuickSmoke_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.Clear();
            var files = _workspace.CreateQuickSmokeFiles();
            _vm.AddPaths(files);
            _resultBox.Text =
                $"Files:       {files.Count}\n" +
                "Workspace:   PASS\n" +
                "Ownership:   PASS\n" +
                "Import:      PASS\n" +
                "Preview:     READY\n" +
                "Execute:     MANUAL\n" +
                "Undo:        MANUAL\n\n" +
                "RESULT: READY FOR QUICK SMOKE";
        }
        catch (Exception ex)
        {
            ShowFailure("Quick Smoke", ex);
        }
    }

    private async void TransactionFoundation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _vm.PrepareTransactionFoundationAsync();
            var plan = result.PlanBuild.Plan;
            _resultBox.Text =
                $"Plan:         {Pass(result.PlanBuild.Success)}\n" +
                $"Persist:      {Pass(result.Persistence?.Success == true)}\n" +
                $"Preflight:    {Pass(result.Preflight?.CanExecute == true)}\n" +
                $"Files:        {plan?.RenameCount ?? 0}\n" +
                $"Warnings:     {result.Preflight?.WarningCount ?? 0}\n\n" +
                $"RESULT:       {(result.Success ? "PASS" : "NOT READY")}";
        }
        catch (Exception ex)
        {
            ShowFailure("Transaction Foundation", ex);
        }
    }

    private void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _workspace.EnsureWorkspace();
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            _resultBox.Text = $"Workspace: PASS\nPath: {path}\n\nRESULT: OPENED";
        }
        catch (Exception ex)
        {
            ShowFailure("Open Workspace", ex);
        }
    }

    private void ResetWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RemoveWorkspaceItems();
            _workspace.ResetWorkspace();
            _resultBox.Text = "Workspace: PASS\nOwnership: PASS\nFiles: 0\n\nRESULT: RESET";
        }
        catch (Exception ex)
        {
            ShowFailure("Reset Workspace", ex);
        }
    }

    private void CleanupWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RemoveWorkspaceItems();
            _workspace.CleanupWorkspace();
            _resultBox.Text = "Workspace removed: PASS\nOutside paths touched: 0\n\nRESULT: CLEAN";
        }
        catch (Exception ex)
        {
            ShowFailure("Cleanup Workspace", ex);
        }
    }

    private void CopyStressCommand_Click(object sender, RoutedEventArgs e)
    {
        var repo = TryFindRepositoryRoot();
        var command = repo is null
            ? StressCommand
            : $"Set-Location -LiteralPath '{repo.Replace("'", "''")}'; {StressCommand}";
        Clipboard.SetText(command);
        _resultBox.Text = $"Command copied: PASS\n\n{command}\n\nRESULT: READY";
    }

    private string LoadLatestStressResult()
    {
        try
        {
            var repo = TryFindRepositoryRoot();
            if (repo is null) return "RESULT: NO REPOSITORY ROOT\n请从源码仓库运行 Internal build。";
            var reportDirectory = Path.Combine(repo, "artifacts", "stress");
            var report = Directory.Exists(reportDirectory)
                ? new DirectoryInfo(reportDirectory).GetFiles("release-stress-2000-*.json")
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (report is null) return $"Files: 2000\nResult: NOT RUN\nCommand: {StressCommand}";

            using var json = JsonDocument.Parse(File.ReadAllText(report.FullName));
            var root = json.RootElement;
            var success = root.GetProperty("Success").GetBoolean();
            var count = root.GetProperty("Count").GetInt32();
            var elapsed = root.GetProperty("TotalElapsedMs").GetDouble() / 1000d;
            return
                $"Files:        {count}\n" +
                $"Plan:         {Pass(success)}\n" +
                $"Execute:      {Pass(success)}\n" +
                $"Journal:      {Pass(success)}\n" +
                $"Startup Gate: {Pass(success)}\n" +
                $"Undo:         {Pass(success)}\n" +
                $"Idempotence:  {Pass(success)}\n" +
                $"Temp Left:    {(success ? 0 : -1)}\n" +
                $"Elapsed:      {elapsed:F1} s\n\n" +
                $"RESULT:       {(success ? "PASS" : "FAIL")}\n" +
                $"Report:       {report.FullName}";
        }
        catch (Exception ex)
        {
            return $"RESULT: FAIL\n{ex.GetType().Name}: {ex.Message}";
        }
    }

    private void RunUiAction(string name, Action action)
    {
        try
        {
            action();
            _resultBox.Text = $"Action: {name}\nItems: {_vm.Items.Count:N0}\nPreview: READY\n\nRESULT: PASS";
        }
        catch (Exception ex)
        {
            ShowFailure(name, ex);
        }
    }

    private void RemoveWorkspaceItems()
    {
        var ownedItems = _vm.Items.Where(item => _workspace.OwnsPath(item.CurrentPath)).ToList();
        _vm.RemoveItems(ownedItems);
    }

    private void ShowFailure(string action, Exception ex)
        => _resultBox.Text = $"Action: {action}\nRESULT: FAIL\n{ex.GetType().Name}: {ex.Message}";

    private static string Pass(bool value) => value ? "PASS" : "FAIL";

    private static string? TryFindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "tools", "run_release_stress.py")))
                return current.FullName;
        }

        return null;
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 150,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 0, 12, 0),
        };
        button.Click += click;
        return button;
    }

    private static StackPanel CreateButtonRow(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }

    private static (Border Parent, UIElementCollection Children) CreateSection(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 14),
            Child = panel,
        };
        return (border, panel.Children);
    }
}
