using System.Drawing;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.Tray;

/// <summary>
/// 托盘图标绘制：按当前 DPI 的托盘尺寸缩放项目原创 Logo。
/// 返回 HICON（调用方负责 DestroyIcon）。
/// </summary>
internal static class TrayIconRenderer
{
    private static Bitmap? _logo;

    private static int IconSize()
    {
        int s = Native.GetSystemMetrics(Native.SM_CXSMICON);
        return Math.Max(16, s);
    }

    internal static IntPtr RenderLogo()
    {
        int size = Math.Max(16, IconSize());
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            var logo = LoadLogo();
            if (logo is not null)
            {
                g.DrawImage(logo, new Rectangle(0, 0, size, size));
            }
            else
            {
                // 资源异常时使用同一原创应用 ICO，不在代码里另画来源不一致的图形。
                if (Environment.ProcessPath is string processPath)
                {
                    using var fallback = Icon.ExtractAssociatedIcon(processPath);
                    if (fallback is not null)
                        g.DrawIcon(fallback, new Rectangle(0, 0, size, size));
                }
            }
        }
        return bmp.GetHicon();
    }

    private static Bitmap? LoadLogo()
    {
        if (_logo is not null) return _logo;
        try
        {
            // 托盘背景是任务栏，与应用主题无关：始终用带底板的深色版，两种任务栏配色下都清晰
            var res = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/tray-dark-32.png"));
            if (res is not null)
                _logo = new Bitmap(res.Stream);
        }
        catch { }
        return _logo;
    }
}
