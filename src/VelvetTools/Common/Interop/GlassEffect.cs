using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VelvetTools.Common.Interop;

/// <summary>
/// 为窗口应用“磨砂玻璃”（Acrylic）效果：
/// Win10 1803+ / Win11 使用未公开的 SetWindowCompositionAttribute 亚克力模糊，
/// Win11 额外启用圆角与深色边框。灵感来自 Fluent/苹果液态玻璃的观感，全部代码自研。
/// </summary>
internal static class GlassEffect
{
    private enum AccentState
    {
        Disabled = 0,
        Gradient = 1,
        TransparentGradient = 2,
        BlurBehind = 3,
        AcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor; // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute; // 19 = WCA_ACCENT_POLICY
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int WCA_ACCENT_POLICY = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    internal static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>按当前主题给窗口上磨砂玻璃：深色为暗紫色调，浅色为亮白色调。</summary>
    internal static void ApplyThemed(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        ApplyThemedHwnd(hwnd);
    }

    internal static void ApplyThemedHwnd(IntPtr hwnd)
    {
        // 色调由窗口的 WindowTintBrush 主题资源绘制；这里只开启背后模糊
        // （亚克力要求非零 tint，给 1/255 的黑）
        ApplyHwnd(hwnd, 0x01, 0x00, 0x00, 0x00, darkFrame: Common.ThemeManager.IsDarkEffective);
    }

    /// <summary>应用亚克力模糊。tint 为叠加色（含透明度）。</summary>
    internal static void Apply(Window window, byte alpha = 0x9E, byte r = 0x16, byte g = 0x10, byte b = 0x20)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        ApplyHwnd(hwnd, alpha, r, g, b);
    }

    internal static void ApplyHwnd(IntPtr hwnd, byte alpha = 0x9E, byte r = 0x16, byte g = 0x10, byte b = 0x20, bool darkFrame = true)
    {
        if (IsWin11)
        {
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int dark = darkFrame ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }

        // GradientColor 字节序为 AABBGGRR
        uint tint = ((uint)alpha << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

        if (!TrySetAccent(hwnd, AccentState.AcrylicBlurBehind, tint))
            if (!TrySetAccent(hwnd, AccentState.BlurBehind, tint))
                TrySetAccent(hwnd, AccentState.TransparentGradient, tint);
    }

    private static bool TrySetAccent(IntPtr hwnd, AccentState state, uint tint)
    {
        var accent = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = 2,
            GradientColor = tint,
        };

        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
