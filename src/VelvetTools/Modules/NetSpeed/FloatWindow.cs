using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;

namespace VelvetTools.Modules.NetSpeed;

/// <summary>
/// 桌面网速悬浮窗：置顶玻璃小胶囊，显示上下行网速与内存占用。
/// </summary>
public sealed class FloatWindow : GlassWindow
{
    private readonly TextBlock _downText;
    private readonly TextBlock _upText;
    private readonly TextBlock _cpuText;
    private readonly TextBlock _memText;

    public FloatWindow()
    {
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        DragMoveEnabled = true;
        EscapeAction = EscAction.None;
        ShowActivated = false;

        FrameworkElement Make(IconKind icon, Brush brush, out TextBlock value)
        {
            value = new TextBlock
            {
                Foreground = brush,
                FontSize = 12,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
                Text = "--",
            };
            return AppIconFactory.Create(icon, 11, brush);
        }

        // 颜色一律取主题资源：写死近白文字会让浅色主题下的悬浮窗内容完全看不见
        var accent = (Brush)Application.Current.FindResource("AccentLightBrush");
        var green = (Brush)Application.Current.FindResource("SuccessBrush");
        var white = (Brush)Application.Current.FindResource("TextPrimaryBrush");

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 6, 12, 6),
        };
        panel.Children.Add(Make(IconKind.ArrowDownload, accent, out _downText));
        panel.Children.Add(_downText);
        panel.Children.Add(new TextBlock { Text = " ", Width = 6 });
        panel.Children.Add(Make(IconKind.ArrowUpload, green, out _upText));
        panel.Children.Add(_upText);
        panel.Children.Add(new TextBlock
        {
            Text = "│",
            Foreground = (Brush)Application.Current.FindResource("TextTertiaryBrush"),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _cpuText = new TextBlock
        {
            Foreground = white,
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        panel.Children.Add(_cpuText);
        _memText = new TextBlock
        {
            Foreground = white,
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(_memText);
        Content = panel;

        MouseDoubleClick += (_, _) => App.Services.ToggleDashboard();

        var menu = new ContextMenu();
        var hideItem = new MenuItem { Header = "隐藏悬浮窗" };
        hideItem.Click += (_, _) =>
        {
            App.Services.Settings.General.ShowFloatWindow = false;
            App.Services.Settings.Save();
            Hide();
        };
        menu.Items.Add(hideItem);
        ContextMenu = menu;

        Loaded += (_, _) =>
        {
            var g = App.Services.Settings.General;
            var wa = SystemParameters.WorkArea;
            if (g.FloatX is double fx && g.FloatY is double fy &&
                fx > wa.Left - 50 && fx < wa.Right && fy > wa.Top - 20 && fy < wa.Bottom)
            {
                Left = fx;
                Top = fy;
            }
            else
            {
                Left = wa.Right - ActualWidth - 24;
                Top = wa.Top + 12;
            }
        };

        LocationChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            App.Services.Settings.General.FloatX = Left;
            App.Services.Settings.General.FloatY = Top;
        };

        App.Services.NetSpeed.Sampled += OnSampled;
    }

    private void OnSampled(SystemSample s)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _downText.Text = NetSpeedService.FormatSpeed(s.DownBps) + "/s";
            _upText.Text = NetSpeedService.FormatSpeed(s.UpBps) + "/s";
            _cpuText.Text = $"CPU {s.CpuPercent:0}%";
            _memText.Text = $"内存 {s.MemPercent:0}%";
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        App.Services.NetSpeed.Sampled -= OnSampled;
        App.Services.Settings.Save();
    }
}
