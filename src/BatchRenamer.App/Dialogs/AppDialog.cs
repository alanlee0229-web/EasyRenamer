using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BatchRenamer.App.Dialogs;

public enum AppDialogKind
{
    Information,
    Success,
    Warning,
    Error,
    Question,
}

public static class AppDialog
{
    public static void Show(
        Window owner,
        string title,
        string message,
        AppDialogKind kind = AppDialogKind.Information,
        string primaryText = "确定")
    {
        var dialog = new AppDialogWindow(title, message, kind, primaryText, null)
        {
            Owner = owner,
        };
        _ = dialog.ShowDialog();
    }

    public static bool Confirm(
        Window owner,
        string title,
        string message,
        string primaryText = "确认执行",
        string secondaryText = "取消",
        AppDialogKind kind = AppDialogKind.Question)
    {
        var dialog = new AppDialogWindow(title, message, kind, primaryText, secondaryText)
        {
            Owner = owner,
        };
        return dialog.ShowDialog() == true;
    }
}

internal sealed class AppDialogWindow : Window
{
    public AppDialogWindow(
        string title,
        string message,
        AppDialogKind kind,
        string primaryText,
        string? secondaryText)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinHeight = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;

        var panelBrush = ResourceBrush("PanelBrush", Colors.White);
        var borderBrush = ResourceBrush("HairlineBrush", Color.FromRgb(230, 234, 240));
        var strongText = ResourceBrush("StrongTextBrush", Color.FromRgb(24, 33, 47));
        var bodyText = ResourceBrush("BodyTextBrush", Color.FromRgb(55, 65, 81));
        var mutedText = ResourceBrush("MutedTextBrush", Color.FromRgb(124, 135, 151));
        var (iconGlyph, iconBackground, iconForeground) = KindVisual(kind);

        var shell = new Border
        {
            Background = panelBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = 0.16,
                Color = Color.FromRgb(15, 23, 42),
            },
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.Child = root;

        var header = new Grid { Margin = new Thickness(24, 22, 20, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = iconBackground,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = iconGlyph,
                Foreground = iconForeground,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        header.Children.Add(icon);

        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = strongText,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 6, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(titleBlock, 1);
        header.Children.Add(titleBlock);

        var close = new Button
        {
            Content = "×",
            Width = 30,
            Height = 30,
            FontSize = 18,
            Foreground = mutedText,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            ToolTip = "关闭",
        };
        close.Click += (_, _) =>
        {
            DialogResult = false;
        };
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new TextBlock
        {
            Text = message,
            Foreground = bodyText,
            FontSize = 13,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(76, 14, 28, 24),
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Border
        {
            Background = ResourceBrush("AppBackgroundBrush", Color.FromRgb(245, 247, 250)),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 14, 20, 14),
            CornerRadius = new CornerRadius(0, 0, 12, 12),
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        footer.Child = actions;

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            var secondary = CreateButton(secondaryText!, "BaseButtonStyle");
            secondary.MinWidth = 92;
            secondary.Margin = new Thickness(0, 0, 10, 0);
            secondary.IsCancel = true;
            secondary.Click += (_, _) =>
            {
                DialogResult = false;
            };
            actions.Children.Add(secondary);
        }

        var primary = CreateButton(primaryText, "PrimaryButtonStyle");
        primary.MinWidth = string.IsNullOrWhiteSpace(secondaryText) ? 88 : 108;
        primary.IsDefault = true;
        primary.Click += (_, _) =>
        {
            DialogResult = true;
        };
        actions.Children.Add(primary);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = shell;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            DialogResult = false;
            e.Handled = true;
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        };
    }

    private static Button CreateButton(string text, string resourceKey)
    {
        var button = new Button
        {
            Content = text,
            Height = 36,
            Cursor = Cursors.Hand,
        };
        if (Application.Current?.TryFindResource(resourceKey) is Style style)
            button.Style = style;
        return button;
    }

    private static SolidColorBrush ResourceBrush(string key, Color fallback)
        => Application.Current?.TryFindResource(key) as SolidColorBrush ?? new SolidColorBrush(fallback);

    private static (string Glyph, Brush Background, Brush Foreground) KindVisual(AppDialogKind kind)
        => kind switch
        {
            AppDialogKind.Success => ("✓", ResourceBrush("SuccessSoftBrush", Color.FromRgb(236, 253, 243)), ResourceBrush("SuccessTextBrush", Color.FromRgb(2, 122, 72))),
            AppDialogKind.Warning => ("!", ResourceBrush("WarningSoftBrush", Color.FromRgb(255, 247, 230)), ResourceBrush("WarningTextBrush", Color.FromRgb(181, 71, 8))),
            AppDialogKind.Error => ("×", ResourceBrush("ErrorSoftBrush", Color.FromRgb(254, 242, 242)), ResourceBrush("ErrorTextBrush", Color.FromRgb(180, 35, 24))),
            AppDialogKind.Question => ("?", ResourceBrush("PrimarySoftBrush", Color.FromRgb(239, 246, 255)), ResourceBrush("PrimaryBrush", Color.FromRgb(37, 99, 235))),
            _ => ("i", ResourceBrush("PrimarySoftBrush", Color.FromRgb(239, 246, 255)), ResourceBrush("PrimaryBrush", Color.FromRgb(37, 99, 235))),
        };
}
