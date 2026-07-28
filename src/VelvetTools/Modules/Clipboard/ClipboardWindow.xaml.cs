using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.Clipboard;

public partial class ClipboardWindow : GlassWindow
{
    private IntPtr _targetWindow;

    public ClipboardWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Hide;
        AutoHideOnDeactivate = true;
        HideInsteadOfClose = true;
        App.Services.Clipboard.Changed += () => Dispatcher.BeginInvoke(RefreshList);
    }

    /// <summary>热键唤起：记录当前前台窗口，用于回贴。</summary>
    public void ShowAtCenter()
    {
        _targetWindow = Native.GetForegroundWindow();
        SearchBox.Text = "";
        Show();               // 先显示，RefreshList 才会真正重建列表
        RefreshList();
        Activate();
        SearchBox.Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // 输入框里按上下键 → 移动列表选择
        if (e.Key is Key.Down or Key.Up && SearchBox.IsKeyboardFocusWithin && List.Items.Count > 0)
        {
            int idx = List.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            List.SelectedIndex = Math.Clamp(idx, 0, List.Items.Count - 1);
            List.ScrollIntoView(List.SelectedItem);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            PasteSelected();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnSearchChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) RefreshList();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        App.Services.Clipboard.Clear(keepPinned: true);
        Toast.Show("已清空剪贴板历史（保留置顶）");
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e) => PasteSelected();

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (SelectedEntry() is not { } entry) return;
        switch (e.Key)
        {
            case Key.Delete:
                App.Services.Clipboard.Delete(entry);
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                App.Services.Clipboard.SetClipboard(entry);
                Toast.Show("已复制到剪贴板");
                e.Handled = true;
                break;
        }
    }

    private ClipEntry? SelectedEntry() => (List.SelectedItem as ListBoxItem)?.Tag as ClipEntry;

    private void PasteSelected()
    {
        if (SelectedEntry() is not { } entry) return;
        Hide();
        App.Services.Clipboard.Paste(entry, _targetWindow);
    }

    private void RefreshList()
    {
        // 窗口隐藏时不重建列表：否则用户每复制一次东西，后台就把
        // 300 项（含图片解码）的可视化树重建一遍
        if (!IsVisible) return;

        string query = SearchBox.Text.Trim();
        int filter = FilterBox.SelectedIndex; // 0 全部 1 文本 2 图片 3 文件

        var source = App.Services.Clipboard.Entries
            .Where(entry => filter switch
            {
                1 => entry.Type == ClipType.Text,
                2 => entry.Type == ClipType.Image,
                3 => entry.Type == ClipType.Files,
                _ => true,
            })
            .Where(entry => query.Length == 0 ||
                            entry.Preview.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Pinned)
            .ThenByDescending(entry => entry.Time)
            .Take(300)
            .ToList();

        List.Items.Clear();
        foreach (var entry in source)
            List.Items.Add(BuildItem(entry));
        if (List.Items.Count > 0) List.SelectedIndex = 0;
    }

    private ListBoxItem BuildItem(ClipEntry entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 类型图标
        var icon = AppIconFactory.Create(
            entry.Type switch
            {
                ClipType.Image => IconKind.Image,
                ClipType.Files => IconKind.Folder,
                _ => IconKind.DocumentText,
            },
            14,
            (Brush)FindResource("TextSecondaryBrush"));
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 10, 0);
        grid.Children.Add(icon);

        FrameworkElement body;
        if (entry.Type == ClipType.Image && entry.ImageFile is not null && File.Exists(entry.ImageFile))
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = 220;
            img.UriSource = new Uri(entry.ImageFile);
            img.EndInit();
            img.Freeze();
            body = new Image
            {
                Source = img,
                MaxHeight = 72,
                HorizontalAlignment = HorizontalAlignment.Left,
                Stretch = Stretch.Uniform,
            };
        }
        else
        {
            body = new TextBlock
            {
                Text = entry.Preview.Replace('\n', ' ').Replace("\r", ""),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };
        }
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (entry.Pinned)
        {
            var pin = AppIconFactory.Create(IconKind.Pin, 12, (Brush)FindResource("AccentLightBrush"));
            pin.VerticalAlignment = VerticalAlignment.Center;
            pin.Margin = new Thickness(0, 0, 6, 0);
            right.Children.Add(pin);
        }
        right.Children.Add(new TextBlock
        {
            Text = entry.Time.ToString(entry.Time.Date == DateTime.Today ? "HH:mm" : "MM-dd"),
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var item = new ListBoxItem { Content = grid, Tag = entry };

        var menu = new ContextMenu();
        AddMenu(menu, entry.Pinned ? "取消置顶" : "置顶", () => App.Services.Clipboard.TogglePin(entry));
        AddMenu(menu, "仅复制", () => { App.Services.Clipboard.SetClipboard(entry); Toast.Show("已复制到剪贴板"); });
        AddMenu(menu, "删除", () => App.Services.Clipboard.Delete(entry));
        item.ContextMenu = menu;
        return item;

        static void AddMenu(ContextMenu menu, string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
    }
}
