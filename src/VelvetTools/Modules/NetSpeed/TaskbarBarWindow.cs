using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VelvetTools.Common;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.NetSpeed;

/// <summary>
/// 任务栏详细信息栏。
///
/// Windows 11 的任务栏内容由 XAML/DirectComposition 合成，外部进程创建的 WS_CHILD
/// 会被该合成层覆盖：句柄、位置、可见样式都正常，但 WPF 和 GDI 内容都看不见。
///
/// 最终使用由 Shell_TrayWnd 拥有（owner）的透明顶层工具窗口。owned popup 按 Win32
/// 规则始终位于 owner 任务栏之上，但不进入全局 Topmost 队列，因此不会在拖动任务栏
/// 图标、点击托盘或切换普通窗口时与 Explorer 反复争抢 z 序。
/// </summary>
public sealed class TaskbarBarWindow : Window
{
    private readonly Border _root;
    private readonly Grid _grid;
    private readonly DispatcherTimer _tracker;
    private readonly List<TextBlock> _labels = new();

    private TextBlock? _downText;
    private TextBlock? _upText;
    private TextBlock? _cpuText;
    private TextBlock? _memText;
    private TextBlock? _cpuTempText;
    private TextBlock? _gpuTempText;
    private TextBlock? _diskTempText;

    private IntPtr _hwnd;
    private IntPtr _taskbarOwner;
    private bool _darkTaskbar = true;
    private bool _barVisible = true;
    private int _lastX = -1, _lastY = -1, _lastW = -1, _lastH = -1;
    private int _ownerAttachCount;
    private int _positionCount;

    public TaskbarBarWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        // Windows 11 的任务栏本身位于 Topmost band；透明分层窗口也必须进入同一 band
        // 才会参与最终合成。owner 关系负责相对顺序，不再运行定时 z 序“保活”。
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = -10000;
        Top = -10000;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        _grid = new Grid();
        _root = new Border { Background = Brushes.Transparent, Child = _grid };
        Content = _root;

        BuildContent();
        MouseLeftButtonUp += (_, _) => App.Services.ToggleDashboard();
        ContextMenu = BuildMenu();

        _tracker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _tracker.Tick += (_, _) =>
        {
            ApplyTheme();
            Track();
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE,
                exStyle | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);

            AttachToTaskbarOwner();
            Track();
            _tracker.Start();
        };

        App.Services.NetSpeed.Sampled += OnSampled;
        App.Services.Hardware.Sampled += OnTempSampled;
    }

    // ==================== 内容 ====================

    public void BuildContent()
    {
        var settings = App.Services.Settings.General;

        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        _labels.Clear();
        _downText = _upText = _cpuText = _memText = _cpuTempText = _gpuTempText = _diskTempText = null;

        _grid.Margin = new Thickness(10, 1, 10, 1);
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        int column = 0;
        if (settings.TaskbarShowNet)
            AddGroup(ref column, "↑:", text => _upText = text, "↓:", text => _downText = text, 72);

        // 其余监控项逐项开关，按开启顺序两两一列（上下两行），与网速组的紧凑布局保持一致。
        var singles = new List<(string Label, Action<TextBlock> Assign, double Width)>();
        if (settings.TaskbarShowCpu == true)
            singles.Add(("CPU:", text => _cpuText = text, 40));
        if (settings.TaskbarShowMem == true)
            singles.Add(("内存:", text => _memText = text, 40));
        if (App.Services.Hardware.IsAvailable)
        {
            if (settings.TaskbarShowCpuTemp == true)
                singles.Add(("温度:", text => _cpuTempText = text, 46));
            if (settings.TaskbarShowGpuTemp == true)
                singles.Add(("显卡:", text => _gpuTempText = text, 46));
            if (settings.TaskbarShowDiskTemp == true)
                singles.Add(("硬盘:", text => _diskTempText = text, 46));
        }

        for (int i = 0; i < singles.Count; i += 2)
        {
            if (column > 0) AddGap(ref column);
            if (i + 1 < singles.Count)
            {
                AddGroup(ref column, singles[i].Label, singles[i].Assign,
                    singles[i + 1].Label, singles[i + 1].Assign,
                    Math.Max(singles[i].Width, singles[i + 1].Width));
            }
            else
            {
                AddSingle(ref column, singles[i].Label, singles[i].Assign, singles[i].Width);
            }
        }

        if (column == 0)
        {
            settings.TaskbarShowNet = true;
            AddGroup(ref column, "↑:", text => _upText = text, "↓:", text => _downText = text, 72);
        }

        ApplyTheme(force: true);
        OnSampled(App.Services.NetSpeed.Latest);
        OnTempSampled(App.Services.Hardware.Latest);
        _lastX = _lastY = _lastW = _lastH = -1;

        if (_hwnd != IntPtr.Zero)
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdateLayout();
                Track();
            }, DispatcherPriority.Loaded);
        }
    }

    private void AddGroup(ref int column, string topLabel, Action<TextBlock> assignTop,
        string bottomLabel, Action<TextBlock> assignBottom, double valueWidth)
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var top = MakeValue(valueWidth);
        var bottom = MakeValue(valueWidth);
        assignTop(top);
        assignBottom(bottom);

        AddRow(column, 0, topLabel, top);
        AddRow(column, 1, bottomLabel, bottom);
        column++;
    }

    private void AddGap(ref int column)
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        column++;
    }

    /// <summary>剩余落单的监控项单独成列，垂直居中跨两行。</summary>
    private void AddSingle(ref int column, string label, Action<TextBlock> assign, double valueWidth)
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var value = MakeValue(valueWidth);
        assign(value);
        var panel = AddRow(column, 0, label, value);
        Grid.SetRowSpan(panel, 2);
        panel.VerticalAlignment = VerticalAlignment.Center;
        column++;
    }

    private static TextBlock MakeValue(double width) => new()
    {
        FontSize = 11.5,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right,
        Width = width,
        Margin = new Thickness(3, 0, 0, 0),
        Text = "--",
    };

    private StackPanel AddRow(int column, int row, string label, TextBlock value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _labels.Add(labelBlock);
        panel.Children.Add(labelBlock);
        panel.Children.Add(value);
        Grid.SetColumn(panel, column);
        Grid.SetRow(panel, row);
        _grid.Children.Add(panel);
        return panel;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        Add("打开控制面板", App.Services.ToggleDashboard);
        menu.Items.Add(new Separator());
        Add("隐藏信息栏", () =>
        {
            App.Services.Settings.General.ShowTaskbarBar = false;
            App.Services.Settings.Save();
            App.Services.SyncTaskbarBar();
        });
        Add("设置…", App.Services.ShowSettingsWindow);
        return menu;

        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }

    // ==================== 位置与 z 序 ====================

    private void Track()
    {
        if (_hwnd == IntPtr.Zero) return;

        IntPtr taskbar = Native.FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar))
        {
            SetBarVisible(false);
            return;
        }

        // Explorer 重启后 Shell_TrayWnd 会换句柄；只有这种变化才重新附着。
        // 某些 Windows 11 构建不会从跨进程 owned popup 的 GW_OWNER 返回任务栏，
        // 若每拍据此校验会导致 400ms 一次的重复 SetWindowLongPtr/SetWindowPos。
        if (_taskbarOwner != taskbar)
            AttachToTaskbarOwner(taskbar);

        if (Native.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4)
        {
            SetBarVisible(false);
            return;
        }

        Native.GetWindowRect(taskbar, out var taskbarRect);
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        if (taskbarWidth <= 0 || taskbarHeight <= 0 || taskbarHeight > taskbarWidth)
        {
            SetBarVisible(false);
            return;
        }

        int screenHeight = Native.GetSystemMetrics(1); // SM_CYSCREEN
        if (taskbarRect.Bottom <= 1 || taskbarRect.Top >= screenHeight - 1)
        {
            SetBarVisible(false);
            return;
        }

        int rightEdge = taskbarRect.Right;
        IntPtr notify = Native.FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && Native.GetWindowRect(notify, out var notifyRect))
            rightEdge = notifyRect.Left;

        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(_root.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(_root.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0)
        {
            SetBarVisible(false);
            return;
        }

        height = Math.Min(height, taskbarHeight);
        int x = Math.Max(taskbarRect.Left, rightEdge - width - (int)Math.Ceiling(8 * dpi.DpiScaleX));
        int y = taskbarRect.Top + (taskbarHeight - height) / 2;

        SetBarVisible(true);
        if (x == _lastX && y == _lastY && width == _lastW && height == _lastH) return;

        _lastX = x;
        _lastY = y;
        _lastW = width;
        _lastH = height;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, width, height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        _positionCount++;
        Logger.Info($"信息栏定位 {x},{y} {width}x{height}（任务栏 owned popup）");
    }

    private void AttachToTaskbarOwner(IntPtr taskbar = default)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (taskbar == IntPtr.Zero) taskbar = Native.FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return;

        SetWindowLongPtrW(_hwnd, GWLP_HWNDPARENT, taskbar);
        _taskbarOwner = taskbar;
        _ownerAttachCount++;
        _lastX = _lastY = _lastW = _lastH = -1;

        Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
    }

    private void SetBarVisible(bool visible)
    {
        if (_barVisible == visible) return;
        _barVisible = visible;
        if (visible)
        {
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            Track();
        }
        else
        {
            ShowWindow(_hwnd, SW_HIDE);
        }
    }

    // ==================== 主题与采样 ====================

    private void ApplyTheme(bool force = false)
    {
        bool dark = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("SystemUsesLightTheme") is not int value || value == 0;
        }
        catch { }

        if (!force && dark == _darkTaskbar) return;
        _darkTaskbar = dark;

        var foreground = new SolidColorBrush(
            dark ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11));
        var label = new SolidColorBrush(
            dark ? Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)
                 : Color.FromArgb(0xC0, 0x11, 0x11, 0x11));

        foreach (var text in new[] { _downText, _upText, _cpuText, _memText })
            if (text is not null) text.Foreground = foreground;
        foreach (var text in _labels) text.Foreground = label;
        UpdateTempColors();
    }

    private void OnSampled(SystemSample sample)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_downText is not null)
                _downText.Text = NetSpeedService.FormatSpeedTaskbar(sample.DownBps);
            if (_upText is not null)
                _upText.Text = NetSpeedService.FormatSpeedTaskbar(sample.UpBps);
            if (_cpuText is not null) _cpuText.Text = $"{sample.CpuPercent:0}%";
            if (_memText is not null) _memText.Text = $"{sample.MemPercent:0}%";
        });
    }

    private void OnTempSampled(Hardware.TemperatureSample sample)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_cpuTempText is not null)
            {
                _cpuTempText.Text = sample.CpuTemp is double cpu ? $"{cpu:0}°C" : "--";
                _cpuTempText.Foreground = TempBrush(sample.CpuTemp);
            }
            if (_gpuTempText is not null)
            {
                _gpuTempText.Text = sample.GpuTemp is double gpu ? $"{gpu:0}°C" : "--";
                _gpuTempText.Foreground = TempBrush(sample.GpuTemp);
            }
            if (_diskTempText is not null)
            {
                _diskTempText.Text = sample.DiskTemp is double disk ? $"{disk:0}°C" : "--";
                _diskTempText.Foreground = TempBrush(sample.DiskTemp);
            }
        });
    }

    private void UpdateTempColors()
    {
        var sample = App.Services.Hardware.Latest;
        if (_cpuTempText is not null) _cpuTempText.Foreground = TempBrush(sample.CpuTemp);
        if (_gpuTempText is not null) _gpuTempText.Foreground = TempBrush(sample.GpuTemp);
        if (_diskTempText is not null) _diskTempText.Foreground = TempBrush(sample.DiskTemp);
    }

    private Brush TempBrush(double? value)
    {
        var normal = new SolidColorBrush(
            _darkTaskbar ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11));
        return value switch
        {
            >= 85 => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x63)),
            >= 70 => new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23)),
            _ => normal,
        };
    }

    public void Reattach()
    {
        _taskbarOwner = IntPtr.Zero;
        AttachToTaskbarOwner();
        Track();
    }

    /// <summary>自检用：当前渲染出的监控数值块数量（网速占两块，其余每项一块）。</summary>
    internal int RenderedValueCountForSelfTest =>
        new[] { _upText, _downText, _cpuText, _memText, _cpuTempText, _gpuTempText, _diskTempText }
            .Count(text => text is not null);

    /// <summary>
    /// 自检：在 Explorer 与任务栏几何不变时连续跟踪三次，不应重复改 owner 或 z 序/位置。
    /// </summary>
    internal (int OwnerReattachments, int Repositions) RunStabilityProbeForSelfTest()
    {
        Track(); // 先完成首次布局定位，后续才是“静态”跟踪
        int ownerBefore = _ownerAttachCount;
        int positionBefore = _positionCount;
        Track();
        Track();
        Track();
        return (_ownerAttachCount - ownerBefore, _positionCount - positionBefore);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tracker.Stop();
        App.Services.NetSpeed.Sampled -= OnSampled;
        App.Services.Hardware.Sampled -= OnTempSampled;
        base.OnClosed(e);
    }

    private const int GWLP_HWNDPARENT = -8;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
