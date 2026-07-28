using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VelvetTools.Common;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.Screenshot;

/// <summary>
/// 贴图窗口（灵感来自 Snipaste 的"贴到屏幕上"，自研实现）：
/// 置顶显示截图，滚轮缩放，拖动移动，Ctrl+C 复制，Esc/双击关闭。
/// </summary>
public sealed class PinWindow : Window
{
    private readonly Bitmap _bitmap;
    private System.Drawing.Rectangle _phys; // 当前物理位置与大小
    private double _scale = 1.0;

    public PinWindow(Bitmap bitmap, System.Drawing.Rectangle physicalRect)
    {
        _bitmap = bitmap;
        _phys = physicalRect;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Left = 0; Top = 0; Width = 100; Height = 100;

        var image = new System.Windows.Controls.Image
        {
            Source = CaptureService.ToBitmapSource(bitmap),
            Stretch = Stretch.Fill,
        };

        var border = new Border
        {
            BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Child = image,
        };
        Content = border;

        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        MouseWheel += OnWheel;
        MouseDoubleClick += (_, _) => Close();
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CaptureService.CopyToClipboard(_bitmap);
                Toast.Show("贴图已复制到剪贴板");
            }
        };

        ContextMenu = BuildMenu();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _phys.X, _phys.Y, _phys.Width, _phys.Height, Native.SWP_SHOWWINDOW);
        };
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        Add("复制图像", () => { CaptureService.CopyToClipboard(_bitmap); Toast.Show("贴图已复制到剪贴板"); });
        Add("保存为文件…", () =>
        {
            var path = CaptureService.SaveToDir(_bitmap, App.Services.Settings.Screenshot.SaveDir);
            Toast.Show($"已保存：{path}");
        });
        Add("OCR 识别文字", () => _ = App.Services.Screenshot.RunOcrFlowAsync((Bitmap)_bitmap.Clone()));
        menu.Items.Add(new Separator());
        Add("关闭贴图", Close);
        return menu;

        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        double newScale = Math.Clamp(_scale * factor, 0.15, 5.0);
        if (Math.Abs(newScale - _scale) < 0.001) return;
        _scale = newScale;

        var hwnd = new WindowInteropHelper(this).Handle;
        int w = Math.Max(40, (int)Math.Round(_bitmap.Width * _scale));
        int h = Math.Max(24, (int)Math.Round(_bitmap.Height * _scale));
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, w, h,
            Native.SWP_NOZORDER | 0x0002 /*SWP_NOMOVE*/);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _bitmap.Dispose();
    }
}
