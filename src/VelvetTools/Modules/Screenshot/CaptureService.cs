using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace VelvetTools.Modules.Screenshot;

public sealed record ScreenShotData(System.Windows.Forms.Screen Screen, Bitmap Bitmap);

/// <summary>屏幕捕获与图像工具（物理像素，PerMonitorV2 感知）。</summary>
public static class CaptureService
{
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int sx, int sy, uint rop);

    private const uint SRCCOPY_CAPTUREBLT = 0x40CC0020; // SRCCOPY | CAPTUREBLT（含分层窗口）

    /// <summary>
    /// 抓取屏幕物理矩形。直接用 BitBlt：Graphics.CopyFromScreen 对
    /// SourceCopy|CaptureBlt 组合枚举做了错误校验会抛异常（.NET 已知问题）。
    /// </summary>
    public static Bitmap CaptureRect(Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        IntPtr dest = g.GetHdc();
        IntPtr src = GetDC(IntPtr.Zero);
        try
        {
            BitBlt(dest, 0, 0, bounds.Width, bounds.Height, src, bounds.X, bounds.Y, SRCCOPY_CAPTUREBLT);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, src);
            g.ReleaseHdc(dest);
        }
        return bmp;
    }

    /// <summary>逐显示器捕获（含分层窗口）。</summary>
    public static List<ScreenShotData> CaptureAllScreens()
    {
        var list = new List<ScreenShotData>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            list.Add(new ScreenShotData(screen, CaptureRect(screen.Bounds)));
        return list;
    }

    /// <summary>整个虚拟桌面一张图。</summary>
    public static Bitmap CaptureVirtualScreen()
        => CaptureRect(System.Windows.Forms.SystemInformation.VirtualScreen);

    public static Bitmap Crop(Bitmap src, Rectangle rect)
    {
        rect.Intersect(new Rectangle(0, 0, src.Width, src.Height));
        if (rect.Width < 1 || rect.Height < 1) rect = new Rectangle(0, 0, src.Width, src.Height);
        return src.Clone(rect, PixelFormat.Format32bppArgb);
    }

    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        IntPtr hBitmap = bmp.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero,
                System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DeleteObject(hBitmap); }
    }

    /// <summary>复制到剪贴板（带重试，剪贴板可能被占用）。</summary>
    public static bool CopyToClipboard(Bitmap bmp)
    {
        var src = ToBitmapSource(bmp);
        for (int i = 0; i < 4; i++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(src);
                return true;
            }
            catch (COMException) { Thread.Sleep(60); }
        }
        return false;
    }

    /// <summary>保存 PNG 到目录，返回完整路径。</summary>
    public static string SaveToDir(Bitmap bmp, string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"VelvetTools_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    public static string ToBase64Png(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }
}
