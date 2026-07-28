using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VelvetTools.Common.Interop;

namespace VelvetTools.Common;

/// <summary>
/// 磨砂玻璃窗口基类：无边框 + 亚克力模糊 + Win11 圆角。
/// XAML 中以 common:GlassWindow 作为根元素使用。
/// </summary>
public class GlassWindow : Window
{
    /// <summary>失去焦点时自动隐藏（适合托盘弹出面板）。</summary>
    public bool AutoHideOnDeactivate { get; set; }

    /// <summary>按 Esc 时的行为。</summary>
    public EscAction EscapeAction { get; set; } = EscAction.Close;

    /// <summary>按住空白处拖动窗口。</summary>
    public bool DragMoveEnabled { get; set; }

    public enum EscAction { None, Hide, Close }

    public GlassWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // WindowStyle=None 仍会让 DWM 在窗口顶部画一条玻璃帧/标题帧
        // （深色下看不出，浅色下就是那条突兀的色条）。
        // WindowChrome 把玻璃帧与标题区厚度全部归零，客户区顶到窗口边缘。
        var chrome = new System.Windows.Shell.WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);

        // 底衬跟随主题资源：换肤瞬间生效；半透明部分由亚克力模糊补足
        SetResourceReference(BackgroundProperty, "WindowTintBrush");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        // 拉丁与数字走内嵌的未修改版 Inter（OFL 1.1，Inter 为保留字名），汉字回退系统微软雅黑
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WindowStyle=None 在可调大小窗口上仍保留 WS_CAPTION，
        // 会在窗口顶部渲染一条系统标题栏（浅色主题下是白条），这里彻底剥掉
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int WS_CAPTION = 0x00C00000;
        int style = Interop.Native.GetWindowLong(hwnd, Interop.Native.GWL_STYLE);
        Interop.Native.SetWindowLong(hwnd, Interop.Native.GWL_STYLE, style & ~WS_CAPTION);

        GlassEffect.ApplyThemed(this);
    }

    /// <summary>主题切换时重刷玻璃色调（无需重建窗口）。</summary>
    public void RefreshGlass() => GlassEffect.ApplyThemed(this);

    /// <summary>true 时 Alt+F4 / 关闭按钮只隐藏窗口，供 ServiceHub 缓存复用。</summary>
    public bool HideInsteadOfClose { get; set; }

    /// <summary>ServiceHub 需要真正销毁窗口时（如换肤重建）临时置 true。</summary>
    public bool AllowRealClose { get; set; }

    /// <summary>最近一次因失焦而隐藏的时间戳，用于消解"点托盘关不掉面板"的竞态。</summary>
    protected int LastAutoHideTick { get; private set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        // 常驻窗口被 Alt+F4 真正销毁后，ServiceHub 缓存的引用就成了死对象，
        // 之后再按热键会静默抛异常（表现为"热键突然失灵"）。这里统一拦成隐藏。
        if (HideInsteadOfClose && !AllowRealClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (AutoHideOnDeactivate)
        {
            LastAutoHideTick = Environment.TickCount;
            Hide();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && EscapeAction != EscAction.None)
        {
            e.Handled = true;
            if (EscapeAction == EscAction.Hide) Hide();
            else Close();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (DragMoveEnabled && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* 拖动期间松开会抛异常，忽略 */ }
        }
    }

    /// <summary>把窗口定位到工作区右下角（托盘上方）。</summary>
    public void PlaceBottomRight(double margin = 12)
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - margin;
        Top = wa.Bottom - ActualHeight - margin;
    }
}
