using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VelvetTools.Common;
using VelvetTools.Modules.NetSpeed;
using VelvetTools.Modules.Screenshot;

namespace VelvetTools.Modules.Tray;

/// <summary>
/// 托盘：一个普通应用图标 + 右键菜单 + 悬停提示。
/// 实时数据不塞进托盘图标（那样只能放几个字符、还占多个图标位），
/// 交给内嵌在任务栏里的信息栏显示。
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly TrayIcon _icon;
    private ContextMenu? _menu;
    private DateTime _lastTipUpdate = DateTime.MinValue;

    public TrayController(MessageWindow window)
    {
        _icon = new TrayIcon(window);
        _icon.LeftClick += () => App.Services.ToggleDashboard();
        _icon.RightClick += ShowMenu;
        App.Services.NetSpeed.Sampled += OnSampled;
    }

    /// <summary>悬停提示里给出完整数据（2 秒更新一次足够）。</summary>
    private void OnSampled(SystemSample s)
    {
        if ((DateTime.Now - _lastTipUpdate).TotalMilliseconds < 2000) return;
        _lastTipUpdate = DateTime.Now;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var t = App.Services.Hardware.Latest;
            string cpuTempLabel = t.CpuName == "系统热区" ? "热区" : "CPU";
            string temp = t.CpuTemp is double c ? $"\n{cpuTempLabel} {c:0}°C" : "";
            if (t.GpuTemp is double gv) temp += $"  GPU {gv:0}°C";

            _icon.SetTooltip(
                $"Velvet Tools\n" +
                $"↓ {NetSpeedService.FormatSpeedLong(s.DownBps)}  ↑ {NetSpeedService.FormatSpeedLong(s.UpBps)}\n" +
                $"CPU {s.CpuPercent:0}%  内存 {s.MemPercent:0}%（{s.MemUsedGb:0.0}/{s.MemTotalGb:0.0} GB）{temp}");
        });
    }

    private void ShowMenu()
    {
        _menu ??= BuildMenu();

        foreach (var item in _menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag as string)
            {
                case "bar":
                    item.IsChecked = App.Services.Settings.General.ShowTaskbarBar;
                    break;
                case "float":
                    item.IsChecked = App.Services.Settings.General.ShowFloatWindow;
                    break;
                case "autostart":
                    item.IsChecked = StartupManager.IsAutoStartEnabled();
                    break;
            }
        }

        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        Add(menu, "显示控制面板", () => App.Services.ToggleDashboard());
        menu.Items.Add(new Separator());
        Add(menu, "AI 对话", App.Services.ShowChatWindow);
        Add(menu, "文件搜索", App.Services.ShowSearchWindow);
        Add(menu, "快速启动器", App.Services.ShowLauncherWindow);
        Add(menu, "剪贴板历史", App.Services.ShowClipboardWindow);
        menu.Items.Add(new Separator());
        Add(menu, "区域截图", () => _ = App.Services.Screenshot.CaptureRegionAsync());
        Add(menu, "全屏截图", () => App.Services.Screenshot.CaptureFullScreen());
        Add(menu, "OCR 文字识别", () => _ = App.Services.Screenshot.CaptureRegionAsync(CaptureAction.Ocr));
        Add(menu, "截图翻译", () => _ = App.Services.Screenshot.CaptureRegionAsync(CaptureAction.Translate));
        Add(menu, "屏幕取色", () => _ = App.Services.ColorPicker.PickAsync());
        menu.Items.Add(new Separator());

        Add(menu, "任务栏信息栏", () =>
        {
            var g = App.Services.Settings.General;
            g.ShowTaskbarBar = !g.ShowTaskbarBar;
            App.Services.Settings.Save();
            App.Services.SyncTaskbarBar();
        }, tag: "bar");

        Add(menu, "桌面悬浮窗", () =>
        {
            var g = App.Services.Settings.General;
            g.ShowFloatWindow = !g.ShowFloatWindow;
            App.Services.Settings.Save();
            App.Services.SyncFloatWindow();
        }, tag: "float");

        Add(menu, "开机自启", () =>
        {
            try
            {
                bool enable = !StartupManager.IsAutoStartEnabled();
                bool alwaysAdmin = App.Services.Settings.General.AlwaysRunAsAdmin;
                if (alwaysAdmin && !Elevation.IsAdmin)
                {
                    Toast.Show("当前为最高权限模式，请到设置中以管理员身份调整自启");
                    return;
                }
                StartupManager.Apply(enable, alwaysAdmin);
                Toast.Show(enable ? "已开启开机自启" : "已关闭开机自启");
            }
            catch (Exception ex)
            {
                Toast.Show("设置开机自启失败：" + ex.Message);
            }
        }, tag: "autostart");

        Add(menu, "设置…", App.Services.ShowSettingsWindow);
        menu.Items.Add(new Separator());
        Add(menu, "退出", () => Application.Current.Shutdown());
        return menu;

        static void Add(ContextMenu menu, string header, Action action, string? tag = null)
        {
            var item = new MenuItem { Header = header, Tag = tag };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }

    public void Dispose()
    {
        App.Services.NetSpeed.Sampled -= OnSampled;
        _icon.Dispose();
    }
}
