using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;
using VelvetTools.Modules.NetSpeed;
using VelvetTools.Modules.Screenshot;

namespace VelvetTools.Modules.Dashboard;

public partial class DashboardWindow : GlassWindow
{
    private bool _updatingVolume;

    private readonly Action _onMonitorsChanged;

    public DashboardWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Hide;
        AutoHideOnDeactivate = true;
        HideInsteadOfClose = true;

        BuildTools();
        _onMonitorsChanged = () => Dispatcher.BeginInvoke(RefreshBrightness);
        App.Services.NetSpeed.Sampled += OnSampled;
        App.Services.Brightness.MonitorsChanged += _onMonitorsChanged;
        App.Services.Hardware.Sampled += OnTempSampled;

        // 每一类传感器独立回退；没有可信来源的项目显示 "--"，不伪造读数。
        TempRow.Visibility = Visibility.Visible;
        OnTempSampled(App.Services.Hardware.Latest);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        App.Services.NetSpeed.Sampled -= OnSampled;
        App.Services.Brightness.MonitorsChanged -= _onMonitorsChanged;
        App.Services.Hardware.Sampled -= OnTempSampled;
    }

    // ---------- 温度 ----------
    private void OnTempSampled(Hardware.TemperatureSample t)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SetTemp(CpuTempText, t.CpuTemp);
            SetTemp(GpuTempText, t.GpuTemp);
            SetTemp(DiskTempText, t.DiskTemp);

            CpuTempLabel.Text = t.CpuName == "系统热区" ? "系统热区" : "CPU";
            GpuTempLabel.Text = string.IsNullOrWhiteSpace(t.GpuName)
                ? "GPU"
                : ShortenGpu(t.GpuName);
            DiskTempLabel.Text = t.Disks.Count > 1 ? $"硬盘 ({t.Disks.Count})" : "硬盘";

            CpuTempText.ToolTip = t.CpuSource ?? App.Services.Hardware.UnavailableReason;
            GpuTempText.ToolTip = t.GpuSource ?? App.Services.Hardware.UnavailableReason;
            DiskTempText.ToolTip = t.DiskSource ?? App.Services.Hardware.UnavailableReason;
        });
    }

    private void SetTemp(TextBlock target, double? value)
    {
        if (value is not double v)
        {
            target.Text = "--°C";
            target.Foreground = (Brush)FindResource("TextTertiaryBrush");
            return;
        }
        target.Text = $"{v:0}°C";
        target.Foreground = (Brush)FindResource(Hardware.HardwareMonitorService.ColorKeyFor(v));
    }

    /// <summary>"NVIDIA GeForce RTX 4060 Laptop GPU" → "RTX 4060"</summary>
    private static string ShortenGpu(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var idx = Array.FindIndex(parts, p => p.StartsWith("RTX", StringComparison.OrdinalIgnoreCase)
                                           || p.StartsWith("GTX", StringComparison.OrdinalIgnoreCase)
                                           || p.StartsWith("RX", StringComparison.OrdinalIgnoreCase)
                                           || p.StartsWith("Arc", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < parts.Length) return $"{parts[idx]} {parts[idx + 1]}";
        return name.Length > 14 ? "GPU" : name;
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }

        // 面板可见时点托盘图标：焦点先离开窗口触发失焦隐藏，随后托盘的
        // 点击消息才到达，若不加冷却就会立刻又弹出来，表现为"关不掉"
        if (Environment.TickCount - LastAutoHideTick < 350) return;

        RefreshBrightness();
        RefreshVolume();
        UpdateSample(App.Services.NetSpeed.Latest);
        Show();
        UpdateLayout();
        PlaceBottomRight();
        Activate();
    }

    // ---------- 状态 ----------
    private void OnSampled(SystemSample s)
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(() => UpdateSample(s));
    }

    private void UpdateSample(SystemSample s)
    {
        DownText.Text = NetSpeedService.FormatSpeedLong(s.DownBps);
        UpText.Text = NetSpeedService.FormatSpeedLong(s.UpBps);
        CpuText.Text = $"CPU {s.CpuPercent:0}%";
        MemLabel.Text = $"内存 {s.MemPercent:0}% · {s.MemUsedGb:0.0}/{s.MemTotalGb:0.0} GB";
        MemBar.Value = s.MemPercent;

        // ProgressBar 自绘模板需要手动算指示条宽度
        if (MemBar.Template?.FindName("PART_Indicator", MemBar) is Border ind &&
            MemBar.Template?.FindName("PART_Track", MemBar) is Border track)
        {
            ind.Width = Math.Max(0, track.ActualWidth * s.MemPercent / 100.0);
        }
    }

    // ---------- 亮度 ----------
    private void RefreshBrightness()
    {
        var monitors = App.Services.Brightness.Monitors;
        MonitorList.ItemsSource = null;
        MonitorList.ItemsSource = monitors;
        NoMonitorText.Visibility = monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LinkCheck.Visibility = monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        LinkCheck.IsChecked = App.Services.Brightness.LinkMonitors;
    }

    private void OnLinkChanged(object sender, RoutedEventArgs e)
        => App.Services.Brightness.LinkMonitors = LinkCheck.IsChecked == true;

    // ---------- 音量 ----------
    private void RefreshVolume()
    {
        int volume = App.Services.Audio.GetVolume();
        if (volume < 0)
        {
            VolumeCard.Visibility = Visibility.Collapsed;
            return;
        }
        _updatingVolume = true;
        VolumeSlider.Value = volume;
        VolumeText.Text = volume + "%";
        SetMuteIcon(App.Services.Audio.GetMute());
        _updatingVolume = false;
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume || !IsLoaded) return;
        int v = (int)Math.Round(e.NewValue);
        VolumeText.Text = v + "%";
        App.Services.Audio.SetVolume(v);
        if (v > 0 && App.Services.Audio.GetMute())
        {
            App.Services.Audio.SetMute(false);
            SetMuteIcon(false);
        }
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        bool mute = !App.Services.Audio.GetMute();
        App.Services.Audio.SetMute(mute);
        SetMuteIcon(mute);
    }

    // ---------- 工具磁贴 ----------
    private void BuildTools()
    {
        AddTile(IconKind.Screenshot, "截图", () => _ = App.Services.Screenshot.CaptureRegionAsync());
        AddTile(IconKind.Desktop, "全屏截图", () => App.Services.Screenshot.CaptureFullScreen());
        AddTile(IconKind.ScanText, "OCR", () => _ = App.Services.Screenshot.CaptureRegionAsync(CaptureAction.Ocr));
        AddTile(IconKind.Translate, "翻译", () => _ = App.Services.Screenshot.CaptureRegionAsync(CaptureAction.Translate));
        AddTile(IconKind.Eyedropper, "取色", () => _ = App.Services.ColorPicker.PickAsync());
        AddTile(IconKind.Clipboard, "剪贴板", App.Services.ShowClipboardWindow);
        AddTile(IconKind.Apps, "启动器", App.Services.ShowLauncherWindow);
        AddTile(IconKind.Bot, "AI 对话", App.Services.ShowChatWindow);
        AddTile(IconKind.Search, "文件搜索", App.Services.ShowSearchWindow);
    }

    private void AddTile(IconKind icon, string label, Action action)
    {
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(13),
            Background = (Brush)FindResource("TileBubbleBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = AppIconFactory.Create(icon, 18, (Brush)FindResource("AccentLightBrush")),
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 0),
        });

        var btn = new Button
        {
            Style = (Style)FindResource("TileButton"),
            Content = stack,
        };
        btn.Click += (_, _) =>
        {
            Hide();
            action();
        };
        ToolGrid.Children.Add(btn);
    }

    private void SetMuteIcon(bool muted)
    {
        MuteBtn.Content = AppIconFactory.Create(muted ? IconKind.SpeakerMute : IconKind.Speaker2, 15);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        App.Services.ShowSettingsWindow();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
