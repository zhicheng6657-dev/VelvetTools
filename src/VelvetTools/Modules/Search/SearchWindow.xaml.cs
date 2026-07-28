using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;

namespace VelvetTools.Modules.Search;

public partial class SearchWindow : GlassWindow
{
    private readonly EverythingClient _client = new();
    private readonly DispatcherTimer _debounce;
    private List<SearchHit> _hits = new();
    private CancellationTokenSource? _cts;

    public SearchWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Hide;

        var s = App.Services.Settings.Search;
        CaseCheck.IsChecked = s.MatchCase;
        WordCheck.IsChecked = s.MatchWholeWord;
        RegexCheck.IsChecked = s.RegexMode;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RunSearchAsync(); };

        QueryBox.TextChanged += (_, _) =>
            QueryHint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public async void ShowSearch()
    {
        Show();
        Activate();
        QueryBox.SelectAll();
        QueryBox.Focus();

        // 引擎随包自带：没在跑就静默拉起，不打扰用户
        if (!EverythingClient.IsUsable)
            await EnsureEngineAsync();
        else
            SetEngineReady(true);
    }

    private async Task EnsureEngineAsync()
    {
        SetEngineReady(false);
        MissingTitle.Text = "正在准备索引…";
        MissingDesc.Text = "首次使用需要扫描磁盘建立索引，通常几秒完成";
        RetryBtn.Visibility = Visibility.Collapsed;

        bool ok = await EverythingBootstrap.EnsureRunningAsync();
        if (ok)
        {
            SetEngineReady(true);
            if (QueryBox.Text.Trim().Length > 0) await RunSearchAsync();
        }
        else
        {
            MissingTitle.Text = "索引引擎启动失败";
            MissingDesc.Text = "可能被安全软件拦截，或需要管理员权限扫描磁盘。可稍后重试。";
            RetryBtn.Visibility = Visibility.Visible;
        }
    }

    private void SetEngineReady(bool ready)
    {
        MissingPanel.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        ResultList.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== 搜索 ====================
    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        var s = App.Services.Settings.Search;
        s.MatchCase = CaseCheck.IsChecked == true;
        s.MatchWholeWord = WordCheck.IsChecked == true;
        s.RegexMode = RegexCheck.IsChecked == true;
        App.Services.Settings.Save();
        _ = RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        string query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            _hits = new List<SearchHit>();
            RenderResults();
            StatusText.Text = "回车打开 · Ctrl+Enter 打开所在文件夹 · Ctrl+C 复制路径";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var s = App.Services.Settings.Search;

        try
        {
            var result = await _client.SearchAsync(query, s.MaxResults,
                s.MatchCase, s.MatchWholeWord, s.RegexMode, _cts.Token);

            if (result is null)
            {
                await EnsureEngineAsync();
                return;
            }

            MissingPanel.Visibility = Visibility.Collapsed;
            ResultList.Visibility = Visibility.Visible;
            _hits = result;
            RenderResults();
            StatusText.Text = _hits.Count == 0
                ? "没有匹配的文件"
                : $"{_hits.Count} 个结果 · 回车打开 · Ctrl+Enter 打开所在文件夹 · Ctrl+C 复制路径";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("Everything 搜索失败", ex);
            StatusText.Text = "搜索失败：" + ex.Message;
        }
    }

    private void RenderResults()
    {
        ResultList.Items.Clear();
        foreach (var hit in _hits)
            ResultList.Items.Add(BuildItem(hit));
        if (ResultList.Items.Count > 0) ResultList.SelectedIndex = 0;
    }

    private ListBoxItem BuildItem(SearchHit hit)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(AppIconFactory.Create(
            hit.IsFolder ? IconKind.Folder : IconKind.Document,
            15,
            (Brush)FindResource(hit.IsFolder ? "AccentLightBrush" : "TextSecondaryBrush")));

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = hit.Name,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontSize = 13.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = hit.Path,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        // 文件大小/修改时间（本地 stat，失败就留空）
        var meta = new TextBlock
        {
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0),
        };
        try
        {
            if (!hit.IsFolder && File.Exists(hit.FullPath))
            {
                var info = new FileInfo(hit.FullPath);
                meta.Text = $"{FormatSize(info.Length)}   {info.LastWriteTime:yyyy-MM-dd}";
            }
        }
        catch { }
        Grid.SetColumn(meta, 2);
        grid.Children.Add(meta);

        var item = new ListBoxItem { Content = grid, Tag = hit };

        var menu = new ContextMenu();
        AddMenu(menu, "打开", () => Open(hit));
        AddMenu(menu, "打开所在文件夹", () => Reveal(hit));
        AddMenu(menu, "复制完整路径", () =>
        {
            try { System.Windows.Clipboard.SetText(hit.FullPath); Toast.Show("已复制路径"); } catch { }
        });
        AddMenu(menu, "复制文件名", () =>
        {
            try { System.Windows.Clipboard.SetText(hit.Name); Toast.Show("已复制文件名"); } catch { }
        });
        item.ContextMenu = menu;
        return item;

        static void AddMenu(ContextMenu menu, string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };

    // ==================== 交互 ====================
    private SearchHit? Selected() => (ResultList.SelectedItem as ListBoxItem)?.Tag as SearchHit;

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up && ResultList.Items.Count > 0)
        {
            int idx = ResultList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            ResultList.SelectedIndex = Math.Clamp(idx, 0, ResultList.Items.Count - 1);
            ResultList.ScrollIntoView(ResultList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ActivateSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (Selected() is not { } hit) return;
        if (e.Key == Key.Enter)
        {
            ActivateSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            try { System.Windows.Clipboard.SetText(hit.FullPath); Toast.Show("已复制路径"); } catch { }
            e.Handled = true;
        }
    }

    private void OnItemActivated(object sender, MouseButtonEventArgs e)
        => ActivateSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

    private void ActivateSelected(bool reveal)
    {
        if (Selected() is not { } hit) return;
        if (reveal) Reveal(hit);
        else Open(hit);
    }

    private void Open(SearchHit hit)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = hit.FullPath, UseShellExecute = true });
            Hide();
        }
        catch (Exception ex)
        {
            Toast.Show("打开失败：" + ex.Message);
        }
    }

    private void Reveal(SearchHit hit)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{hit.FullPath}\"",
                UseShellExecute = true,
            });
            Hide();
        }
        catch (Exception ex)
        {
            Toast.Show("打开文件夹失败：" + ex.Message);
        }
    }



    private void OnRecheckClick(object sender, RoutedEventArgs e) => _ = EnsureEngineAsync();

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
