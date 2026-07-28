using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VelvetTools.Common;
using VelvetTools.Common.Interop;
using VelvetTools.Modules.Audio;
using VelvetTools.Modules.Brightness;
using VelvetTools.Modules.Chat;
using VelvetTools.Modules.Clipboard;
using VelvetTools.Modules.ColorPicker;
using VelvetTools.Modules.Dashboard;
using VelvetTools.Modules.Launcher;
using VelvetTools.Modules.NetSpeed;
using VelvetTools.Modules.Ocr;
using VelvetTools.Modules.Screenshot;
using VelvetTools.Modules.Translate;
using VelvetTools.Modules.Tray;

namespace VelvetTools;

public partial class App : Application
{
    /// <summary>全局服务定位器（应用启动时初始化）。</summary>
    public static ServiceHub Services { get; private set; } = null!;

    private SingleInstance? _singleInstance;
    private bool _smokeTest;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Init();
        _smokeTest = e.Args.Contains("--smoke");

        Services = new ServiceHub();

        // “始终最高权限”：通过计划任务免 UAC 拉起管理员实例，然后本实例退出。
        // 必须在占用单实例互斥体之前做，否则新实例会被当作重复实例踢掉。
        // 安装器也可以在用户明确勾选后预先创建同名计划任务。以任务是否存在作为
        // 权限配置的真实来源，避免安装后还要进入设置再保存一次。
        if (!_smokeTest
            && (Services.Settings.General.AlwaysRunAsAdmin || StartupManager.TaskExists())
            && !Elevation.IsAdmin
            && StartupManager.TryRunElevatedTask())
        {
            Logger.Info("已通过计划任务切换为管理员实例，当前实例退出");
            Shutdown();
            return;
        }

        // 开发自检要能与托盘中的正式实例并行运行；它只存活数秒且不会占用正式实例互斥体。
        // 这样调试新构建时不必强行结束用户当前正在使用的版本。
        if (!_smokeTest)
        {
            _singleInstance = new SingleInstance();
            if (!_singleInstance.TryAcquire(() => Dispatcher.BeginInvoke(() => Services?.ToggleDashboard())))
            {
                Logger.Info("已有实例在运行，唤起后退出");
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("未处理异常（非 UI 线程）", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        try
        {
            ThemeManager.Initialize(Services.Settings.General.Theme);
            Services.Initialize();
            Logger.Info($"Velvet Tools 启动完成（管理员：{Elevation.IsAdmin}，主题：{ThemeManager.Mode}）");
        }
        catch (Exception ex)
        {
            Logger.Error("启动失败", ex);
            MessageBox.Show("Velvet Tools 启动失败：" + ex.Message, "Velvet Tools", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (_smokeTest)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Logger.Info($"SMOKE OK | monitors={Services.Brightness.Monitors.Count} " +
                            $"apps={Services.AppIndex.Apps.Count} " +
                            $"net={Services.NetSpeed.Latest.DownBps:0}B/s " +
                            $"mem={Services.NetSpeed.Latest.MemPercent:0}% " +
                            $"audio={Services.Audio.GetVolume()} " +
                            $"dark={ThemeManager.IsDarkEffective} " +
                            $"cpuTemp={Services.Hardware.Latest.CpuTemp?.ToString("0") ?? "--"} " +
                            $"gpuTemp={Services.Hardware.Latest.GpuTemp?.ToString("0") ?? "--"} " +
                            $"gpuSource={Services.Hardware.Latest.GpuSource ?? "--"} " +
                            $"diskTemp={Services.Hardware.Latest.DiskTemp?.ToString("0") ?? "--"} " +
                            $"diskSource={Services.Hardware.Latest.DiskSource ?? "--"}");

                // --shots <目录>：顺便把每个窗口拍成 PNG，改完界面用来肉眼验收
                int shotIdx = Array.IndexOf(e.Args, "--shots");
                string? shotDir = shotIdx >= 0 && shotIdx + 1 < e.Args.Length ? e.Args[shotIdx + 1] : null;
                int failed = Services.SelfTestWindows(shotDir);
                Logger.Info(failed == 0 ? "SMOKE WINDOWS OK" : $"SMOKE WINDOWS FAILED: {failed}");
                Shutdown(failed == 0 ? 0 : 2);
            };
            timer.Start();
        }
        else
        {
            Toast.Show("Velvet Tools 已启动，正在系统托盘运行");
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("未处理异常（UI 线程）", e.Exception);
        Toast.Show("发生错误：" + e.Exception.Message, 3500);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services?.Dispose();
            _singleInstance?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}

/// <summary>集中管理所有服务与常驻窗口。</summary>
public sealed class ServiceHub : IDisposable
{
    public Settings.AppSettings Settings { get; } = VelvetTools.Settings.AppSettings.Load();

    public MessageWindow MessageWindow { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public NetSpeedService NetSpeed { get; private set; } = null!;
    public BrightnessService Brightness { get; } = new();
    public AudioService Audio { get; } = new();
    public OcrService Ocr { get; } = new();
    public TranslateService Translate { get; } = new();
    public ChatService Chat { get; } = new();
    public WebSearchService WebSearch { get; } = new();
    public Modules.Knowledge.KnowledgeService Knowledge { get; } = new();
    public Modules.Hardware.HardwareMonitorService Hardware { get; } = new();
    public ClipboardService Clipboard { get; private set; } = null!;
    public AppIndexService AppIndex { get; } = new();
    public ScreenshotController Screenshot { get; } = new();
    public ColorPickerController ColorPicker { get; } = new();

    private TrayController? _tray;
    private DashboardWindow? _dashboard;
    private ClipboardWindow? _clipboardWindow;
    private LauncherWindow? _launcherWindow;
    private ChatWindow? _chatWindow;
    private Modules.Search.SearchWindow? _searchWindow;
    private Modules.Knowledge.KnowledgeWindow? _knowledgeWindow;
    private Settings.SettingsWindow? _settingsWindow;
    private FloatWindow? _floatWindow;
    private TaskbarBarWindow? _taskbarBar;

    public void Initialize()
    {
        MessageWindow = new MessageWindow();
        NetSpeed = new NetSpeedService();
        Clipboard = new ClipboardService(MessageWindow);
        Hotkeys = new HotkeyManager(MessageWindow);
        _tray = new TrayController(MessageWindow);

        uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        MessageWindow.AddHook((msg, _, _) =>
        {
            if (msg == Native.WM_DISPLAYCHANGE)
                Brightness.RefreshAsync();
            else if (msg == Native.WM_SETTINGCHANGE)
                ThemeManager.OnSystemThemeChanged();
            else if (msg == (int)taskbarCreated)
                _taskbarBar?.Reattach();
            return false;
        });

        ThemeManager.Changed += OnThemeChanged;

        Brightness.RefreshAsync();
        AppIndex.RescanAsync();
        SyncFloatWindow();
        SyncTaskbarBar();

        var failures = ApplyHotkeys();
        if (failures.Count > 0)
            Toast.Show("部分热键注册失败：" + string.Join("；", failures), 4500);
    }

    /// <summary>主题切换：给已开窗口重刷玻璃色调，重建缓存了旧画刷的弹窗。</summary>
    private void OnThemeChanged()
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is GlassWindow gw)
                    gw.RefreshGlass();
            }

            // 控制面板/悬浮窗的部分元素在代码里缓存了画刷，重建最干净
            if (_dashboard is not null)
            {
                try { _dashboard.AllowRealClose = true; _dashboard.Close(); } catch { }
                _dashboard = null;
            }
            if (_floatWindow is not null)
            {
                try { _floatWindow.AllowRealClose = true; _floatWindow.Close(); } catch { }
                _floatWindow = null;
                SyncFloatWindow();
            }
        });
    }

    public sealed record HotkeyFailure(string Name, string Message)
    {
        public override string ToString() => Message;
    }

    /// <summary>按设置注册全部热键；失败项继续保留修改前仍可用的系统注册。</summary>
    public List<HotkeyFailure> ApplyHotkeys()
    {
        var h = Settings.Hotkeys;
        var failures = new List<HotkeyFailure>();

        void Reg(string name, string gesture, Action action)
        {
            var error = Hotkeys.Register(name, gesture, action);
            if (error is not null) failures.Add(new HotkeyFailure(name, error));
        }

        Reg("screenshot", h.Screenshot, () => _ = Screenshot.CaptureRegionAsync());
        Reg("ocr", h.ScreenshotOcr, () => _ = Screenshot.CaptureRegionAsync(CaptureAction.Ocr));
        Reg("translate", h.ScreenshotTranslate, () => _ = Screenshot.CaptureRegionAsync(CaptureAction.Translate));
        Reg("colorpicker", h.ColorPicker, () => _ = ColorPicker.PickAsync());
        Reg("clipboard", h.ClipboardHistory, ShowClipboardWindow);
        Reg("launcher", h.Launcher, ShowLauncherWindow);
        Reg("chat", Settings.Chat.Hotkey, ShowChatWindow);
        Reg("search", Settings.Search.Hotkey, ShowSearchWindow);
        return failures;
    }

    // ---------- 窗口管理 ----------
    public void ToggleDashboard()
    {
        _dashboard ??= new DashboardWindow();
        _dashboard.Toggle();
    }

    public void ShowClipboardWindow()
    {
        _clipboardWindow ??= new ClipboardWindow();
        _clipboardWindow.ShowAtCenter();
    }

    public void ShowLauncherWindow()
    {
        _launcherWindow ??= new LauncherWindow();
        _launcherWindow.ShowLauncher();
    }

    public void ShowChatWindow()
    {
        _chatWindow ??= new ChatWindow();
        _chatWindow.ShowChat();
    }

    public void ShowSearchWindow()
    {
        _searchWindow ??= new Modules.Search.SearchWindow();
        _searchWindow.ShowSearch();
    }

    public void ShowKnowledgeWindow()
    {
        _knowledgeWindow ??= new Modules.Knowledge.KnowledgeWindow();
        _knowledgeWindow.ShowKnowledge();
    }

    public void ShowSettingsWindow()
    {
        _settingsWindow ??= new Settings.SettingsWindow();
        _settingsWindow.ShowSettings();
    }

    public void SyncFloatWindow()
    {
        if (Settings.General.ShowFloatWindow)
        {
            _floatWindow ??= new FloatWindow();
            _floatWindow.Show();
        }
        else
        {
            _floatWindow?.Hide();
        }
    }

    /// <summary>
    /// 自检：逐个构造并显示所有窗口，捕获初始化期的异常（XAML 资源缺失、
    /// 绑定失败、样式键写错等在运行前发现不了的问题）。返回失败个数。
    /// 传 shotDir 时顺便把每个窗口渲染成 PNG，方便改完界面直接看效果。
    /// </summary>
    public int SelfTestWindows(string? shotDir = null)
    {
        int failed = 0;

        var cases = new (string Name, Action Open, Func<Window?> Get, Action Close)[]
        {
            ("控制面板", ToggleDashboard, () => _dashboard, () => _dashboard?.Hide()),
            ("设置", ShowSettingsWindow, () => _settingsWindow, () => _settingsWindow?.Hide()),
            ("AI 对话", ShowChatWindow, () => _chatWindow, () => _chatWindow?.Hide()),
            ("文件搜索", ShowSearchWindow, () => _searchWindow, () => _searchWindow?.Hide()),
            ("知识库", ShowKnowledgeWindow, () => _knowledgeWindow, () => _knowledgeWindow?.Hide()),
            ("剪贴板历史", ShowClipboardWindow, () => _clipboardWindow, () => _clipboardWindow?.Hide()),
            ("快速启动器", ShowLauncherWindow, () => _launcherWindow, () => _launcherWindow?.Hide()),
        };

        if (shotDir is not null) System.IO.Directory.CreateDirectory(shotDir);

        foreach (var (name, open, get, close) in cases)
        {
            try
            {
                open();
                // 强制走一遍布局与渲染，才能触发资源查找异常
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                if (shotDir is not null) SaveShot(get(), System.IO.Path.Combine(shotDir, name + ".png"));
                close();
                Logger.Info($"  [自检] {name} ✓");
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error($"  [自检] {name} ✗", ex);
            }
        }

        if (shotDir is not null)
        {
            try
            {
                ShowSettingsWindow();
                _settingsWindow?.SelectPageForSelfTest(1);
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                SaveShot(_settingsWindow, System.IO.Path.Combine(shotDir, "设置-快捷键.png"));
                _settingsWindow?.Hide();
                Logger.Info("  [自检] 设置-快捷键 ✓");
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error("  [自检] 设置-快捷键 ✗", ex);
            }

            try
            {
                ShowSettingsWindow();
                _settingsWindow?.SelectPageForSelfTest(5); // AI 对话（含知识库管理入口）
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                SaveShot(_settingsWindow, System.IO.Path.Combine(shotDir, "设置-AI 对话.png"));
                _settingsWindow?.Hide();
                Logger.Info("  [自检] 设置-AI 对话 ✓");
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error("  [自检] 设置-AI 对话 ✗", ex);
            }

            try
            {
                ShowSettingsWindow();
                _settingsWindow?.SelectPageForSelfTest(7); // 关于
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                SaveShot(_settingsWindow, System.IO.Path.Combine(shotDir, "设置-关于.png"));
                _settingsWindow?.Hide();
                Logger.Info("  [自检] 设置-关于 ✓");
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error("  [自检] 设置-关于 ✗", ex);
            }

            try
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen
                    ?? throw new InvalidOperationException("未找到主显示器");
                var bitmap = Modules.Screenshot.CaptureService.CaptureRect(screen.Bounds);
                var session = new Modules.Screenshot.OverlaySession();
                var overlay = new Modules.Screenshot.OverlayWindow(
                    new Modules.Screenshot.ScreenShotData(screen, bitmap), session, null);
                overlay.Show();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                var toolbar = overlay.PrepareToolbarForSelfTest();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                SaveElementShot(toolbar, System.IO.Path.Combine(shotDir, "截图工具栏-画笔参数.png"));
                overlay.Close();
                Logger.Info("  [自检] 截图工具栏-画笔参数 ✓");
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error("  [自检] 截图工具栏-画笔参数 ✗", ex);
            }
        }

        // 非窗口类服务的关键路径
        try
        {
            const string ownerName = "__smoke_hotkey_owner";
            const string conflictName = "__smoke_hotkey_conflict";
            string? selected = null;
            try
            {
                foreach (string candidate in new[]
                         {
                             "Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F23",
                             "Ctrl+Alt+Shift+F22", "Ctrl+Alt+Shift+F21",
                         })
                {
                    if (Hotkeys.Register(ownerName, candidate, () => { }) is null)
                    {
                        selected = candidate;
                        break;
                    }
                }

                if (selected is null)
                    throw new InvalidOperationException("没有可用于热键冲突自检的临时组合");

                string? conflict = Hotkeys.Register(conflictName, selected, () => { });
                if (conflict is null)
                    throw new InvalidOperationException("同一组合被错误地重复注册");

                if (Hotkeys.Register(ownerName, selected, () => { }) is not null)
                    throw new InvalidOperationException("冲突失败后原热键注册未被保留");

                if (!HotkeyManager.TryFormatGesture(
                        System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt,
                        System.Windows.Input.Key.A,
                        out string formatted)
                    || formatted != "Ctrl+Alt+A")
                    throw new InvalidOperationException("键盘输入格式化回归失败");
            }
            finally
            {
                Hotkeys.Unregister(conflictName);
                Hotkeys.Unregister(ownerName);
            }
            Logger.Info("  [自检] 快捷键点击录入格式与冲突拒绝 ✓");
        }
        catch (Exception ex)
        {
            failed++;
            Logger.Error("  [自检] 快捷键点击录入格式与冲突拒绝 ✗", ex);
        }

        try
        {
            var probe = new TaskbarBarWindow();
            try
            {
                probe.Show();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                var result = probe.RunStabilityProbeForSelfTest();
                if (result.OwnerReattachments != 0 || result.Repositions != 0)
                    throw new InvalidOperationException(
                        $"静态任务栏被重复刷新：owner={result.OwnerReattachments}, position={result.Repositions}");

                // 监控项逐项开关：全开/全关（全关时回退为只显网速）都能正确重建布局。
                var general = Settings.General;
                (bool net, bool? cpu, bool? mem, bool? ct, bool? gt, bool? dt) backup =
                    (general.TaskbarShowNet, general.TaskbarShowCpu, general.TaskbarShowMem,
                     general.TaskbarShowCpuTemp, general.TaskbarShowGpuTemp, general.TaskbarShowDiskTemp);
                try
                {
                    general.TaskbarShowNet = true;
                    general.TaskbarShowCpu = general.TaskbarShowMem = true;
                    general.TaskbarShowCpuTemp = general.TaskbarShowGpuTemp = general.TaskbarShowDiskTemp = true;
                    probe.BuildContent();
                    int expected = Hardware.IsAvailable ? 7 : 4;
                    if (probe.RenderedValueCountForSelfTest != expected)
                        throw new InvalidOperationException(
                            $"全开时应渲染 {expected} 个数值块，实际 {probe.RenderedValueCountForSelfTest}");

                    general.TaskbarShowNet = false;
                    general.TaskbarShowCpu = general.TaskbarShowMem = false;
                    general.TaskbarShowCpuTemp = general.TaskbarShowGpuTemp = general.TaskbarShowDiskTemp = false;
                    probe.BuildContent();
                    if (probe.RenderedValueCountForSelfTest != 2)
                        throw new InvalidOperationException(
                            $"全关时应回退为网速 2 个数值块，实际 {probe.RenderedValueCountForSelfTest}");
                }
                finally
                {
                    (general.TaskbarShowNet, general.TaskbarShowCpu, general.TaskbarShowMem,
                     general.TaskbarShowCpuTemp, general.TaskbarShowGpuTemp, general.TaskbarShowDiskTemp) = backup;
                }
                Logger.Info("  [自检] 信息栏监控项逐项开关布局 ✓");
            }
            finally
            {
                probe.Close();
            }
            Logger.Info("  [自检] 任务栏信息栏静态跟踪不重复抢占 z 序 ✓");
        }
        catch (Exception ex)
        {
            failed++;
            Logger.Error("  [自检] 任务栏信息栏稳定性 ✗", ex);
        }

        try
        {
            bool ready = Task.Run(() => Modules.Search.EverythingBootstrap.EnsureRunningAsync())
                .GetAwaiter().GetResult();
            if (!ready) throw new InvalidOperationException("Everything 索引引擎未就绪");

            if (Modules.Search.EverythingSdk.IndexedItemCount > 0)
            {
                var hits = Modules.Search.EverythingSdk.Search(
                    "Everything.exe", 5, matchCase: false, matchWholeWord: false, regex: false);
                Logger.Info($"  [自检] Everything 默认索引实际查询 ✓（{hits.Count} 个样例结果）");
            }
            else
            {
                // 官方 SDK 1.4 只能查询默认实例；命名实例走我们自行实现的公开 IPC。
                using var client = new Modules.Search.EverythingClient();
                var hits = client.SearchForSelfTest("Everything.exe", 10)
                    ?? throw new InvalidOperationException("Everything 私有实例拒绝查询");
                if (hits.Count == 0)
                    throw new InvalidOperationException("Everything 私有索引已加载但实际查询无结果");
                Logger.Info($"  [自检] Everything 私有索引实际 IPC 查询 ✓（{hits.Count} 个样例结果）");
            }
        }
        catch (Exception ex)
        {
            failed++;
            Logger.Error("  [自检] Everything 探测 ✗", ex);
        }

        try
        {
            Modules.Knowledge.KnowledgeService.RunCoreSelfTest();
            Modules.Knowledge.KnowledgeStore.RunStorageSelfTest();
            Logger.Info("  [自检] 知识库分块与版本化存储 ✓");
        }
        catch (Exception ex)
        {
            failed++;
            Logger.Error("  [自检] 知识库分块与版本化存储 ✗", ex);
        }

        return failed;
    }

    /// <summary>把窗口内容渲染成 PNG（不经过屏幕，所以被遮挡也拍得到）。</summary>
    private static void SaveShot(Window? window, string path)
    {
        if (window is null || window.ActualWidth < 1 || window.ActualHeight < 1) return;

        var dpi = VisualTreeHelper.GetDpi(window);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    private static void SaveElementShot(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        if (element.ActualWidth < 1 || element.ActualHeight < 1) return;
        var dpi = VisualTreeHelper.GetDpi(element);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // 直接 Render(element) 会把它在 Canvas 中的绝对偏移也带进来，导致裁出的
        // 小图只有透明/黑色区域。VisualBrush 把元素自身坐标归一到 (0,0)。
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(
                new VisualBrush(element) { Stretch = Stretch.Fill },
                null,
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    public void SyncTaskbarBar()
    {
        if (Settings.General.ShowTaskbarBar)
        {
            if (_taskbarBar is null)
            {
                _taskbarBar = new TaskbarBarWindow();
                _taskbarBar.Show();
            }
            else
            {
                if (!_taskbarBar.IsVisible) _taskbarBar.Show();
                _taskbarBar.BuildContent(); // 显示项变化
            }
        }
        else
        {
            try { _taskbarBar?.Close(); } catch { }
            _taskbarBar = null;
        }
    }

    public void SyncClipboardListener()
    {
        if (Settings.Clipboard.Enabled) Clipboard.Start();
        else Clipboard.Stop();
    }

    public void Dispose()
    {
        Settings.Save();
        try { _taskbarBar?.Close(); } catch { }
        Modules.Search.EverythingBootstrap.StopIfStartedByUs();
        Hardware.Dispose();
        _tray?.Dispose();
        Hotkeys?.UnregisterAll();
        Clipboard?.Dispose();
        NetSpeed?.Dispose();
        Brightness.Dispose();
        MessageWindow?.Dispose();
    }
}
