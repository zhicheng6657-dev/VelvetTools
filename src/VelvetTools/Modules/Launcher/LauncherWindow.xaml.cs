using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;

namespace VelvetTools.Modules.Launcher;

public partial class LauncherWindow : GlassWindow
{
    private sealed record ResultItem(string Title, string? Subtitle, ImageSource? Image, IconKind Glyph, Action<bool> Run);

    private readonly List<ResultItem> _results = new();

    public LauncherWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Hide;
        AutoHideOnDeactivate = true;
        HideInsteadOfClose = true;
    }

    public void ShowLauncher()
    {
        QueryBox.Text = "";
        UpdateResults();
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + wa.Height * 0.22;
        Show();
        Activate();
        QueryBox.Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up && List.Items.Count > 0)
        {
            int idx = List.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            List.SelectedIndex = ((idx % List.Items.Count) + List.Items.Count) % List.Items.Count;
            List.ScrollIntoView(List.SelectedItem);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            RunSelected(admin: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e) => UpdateResults();

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && List.SelectedIndex >= 0)
            RunSelected(admin: false);
    }

    private void RunSelected(bool admin)
    {
        int idx = List.SelectedIndex;
        if (idx < 0 || idx >= _results.Count) return;
        var item = _results[idx];
        Hide();
        try { item.Run(admin); }
        catch (Exception ex)
        {
            Logger.Error("启动失败", ex);
            Toast.Show("启动失败：" + ex.Message);
        }
    }

    private void UpdateResults()
    {
        string query = QueryBox.Text.Trim();
        _results.Clear();

        // 1) 内置命令
        foreach (var (names, title, glyph, action) in BuiltinCommands())
        {
            if (query.Length == 0) continue;
            string q = query.ToLowerInvariant();
            if (names.Any(n => n.Contains(q, StringComparison.OrdinalIgnoreCase)))
                _results.Add(new ResultItem(title, "Velvet Tools 命令", null, glyph, _ => action()));
        }

        // 2) 应用
        if (query.Length > 0)
        {
            string q = query.ToLowerInvariant();
            var scored = App.Services.AppIndex.Apps
                .Select(a => (App: a, Score: FuzzyMatcher.Score(a.SearchKey, q)))
                .Where(t => t.Score > 0)
                .OrderByDescending(t => t.Score)
                .Take(12);

            foreach (var (app, _) in scored)
            {
                _results.Add(new ResultItem(app.Name, app.Description, app.Icon, IconKind.Apps, admin =>
                {
                    var psi = new ProcessStartInfo { FileName = app.Path, UseShellExecute = true };
                    if (admin) psi.Verb = "runas";
                    Process.Start(psi);
                }));
            }

            // 3) 路径直达
            string expanded = Environment.ExpandEnvironmentVariables(query);
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                _results.Add(new ResultItem($"打开 {expanded}", "路径", null, IconKind.FolderOpen, _ =>
                    Process.Start(new ProcessStartInfo { FileName = expanded, UseShellExecute = true })));
            }

            // 4) 网页搜索兜底
            _results.Add(new ResultItem($"网页搜索：{query}", "必应", null, IconKind.GlobeSearch, _ =>
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.bing.com/search?q=" + Uri.EscapeDataString(query),
                    UseShellExecute = true,
                })));
        }

        RenderResults();
    }

    private IEnumerable<(string[] names, string title, IconKind glyph, Action action)> BuiltinCommands()
    {
        yield return (new[] { "截图", "screenshot", "jt", "jietu" }, "区域截图", IconKind.Screenshot,
            () => _ = App.Services.Screenshot.CaptureRegionAsync());
        yield return (new[] { "ocr", "识别", "文字识别" }, "截图 OCR 识别文字", IconKind.ScanText,
            () => _ = App.Services.Screenshot.CaptureRegionAsync(Screenshot.CaptureAction.Ocr));
        yield return (new[] { "翻译", "translate", "fy", "fanyi" }, "截图翻译", IconKind.Translate,
            () => _ = App.Services.Screenshot.CaptureRegionAsync(Screenshot.CaptureAction.Translate));
        yield return (new[] { "取色", "颜色", "color", "qs" }, "屏幕取色器", IconKind.Eyedropper,
            () => _ = App.Services.ColorPicker.PickAsync());
        yield return (new[] { "剪贴板", "clipboard", "jtb" }, "剪贴板历史", IconKind.Clipboard,
            () => App.Services.ShowClipboardWindow());
        yield return (new[] { "设置", "settings", "sz" }, "Velvet Tools 设置", IconKind.Settings,
            () => App.Services.ShowSettingsWindow());
        yield return (new[] { "退出", "exit", "quit" }, "退出 Velvet Tools", IconKind.Power,
            () => Application.Current.Shutdown());
    }

    private void RenderResults()
    {
        List.Items.Clear();
        foreach (var r in _results)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (r.Image is not null)
            {
                grid.Children.Add(new Image
                {
                    Source = r.Image, Width = 24, Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            else
            {
                grid.Children.Add(AppIconFactory.Create(
                    r.Glyph,
                    17,
                    (Brush)FindResource("AccentLightBrush")));
            }

            var stack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = r.Title,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrWhiteSpace(r.Subtitle))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = r.Subtitle,
                    Foreground = (Brush)FindResource("TextTertiaryBrush"),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            List.Items.Add(new ListBoxItem { Content = grid });
        }

        List.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_results.Count > 0) List.SelectedIndex = 0;
    }
}
