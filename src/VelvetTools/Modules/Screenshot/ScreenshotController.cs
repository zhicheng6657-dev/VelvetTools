using System.Drawing;
using VelvetTools.Common;
using VelvetTools.Modules.Ocr;
using VelvetTools.Modules.Translate;

namespace VelvetTools.Modules.Screenshot;

/// <summary>截图流程编排：热键入口 → 遮罩选区 → 复制/保存/钉住/OCR/翻译。</summary>
public sealed class ScreenshotController
{
    private OverlaySession? _session;

    public bool IsCapturing => _session is not null;

    /// <summary>区域截图。directAction 为空时选区后弹操作栏。</summary>
    public async Task CaptureRegionAsync(CaptureAction? directAction = null)
    {
        if (_session is not null) return;
        try
        {
            var shots = CaptureService.CaptureAllScreens();
            _session = new OverlaySession();

            OverlayWindow? cursorWindow = null;
            Native.GetCursorPos(out var cursor);
            foreach (var shot in shots)
            {
                var w = new OverlayWindow(shot, _session, directAction);
                w.Show();
                if (shot.Screen.Bounds.Contains(cursor.X, cursor.Y))
                    cursorWindow = w;
            }
            cursorWindow?.Activate();

            var result = await _session.Tcs.Task;
            _session = null;
            if (result is null) return;
            await HandleAsync(result);
        }
        catch (Exception ex)
        {
            _session = null;
            Logger.Error("截图流程异常", ex);
            Toast.Show("截图失败：" + ex.Message);
        }
    }

    /// <summary>全屏（虚拟桌面）直接截图。</summary>
    public void CaptureFullScreen()
    {
        try
        {
            using var bmp = CaptureService.CaptureVirtualScreen();
            Finish(bmp, copy: true, save: App.Services.Settings.Screenshot.AutoSaveFile);
        }
        catch (Exception ex)
        {
            Logger.Error("全屏截图失败", ex);
            Toast.Show("全屏截图失败：" + ex.Message);
        }
    }

    private async Task HandleAsync(CaptureSelection sel)
    {
        var s = App.Services.Settings.Screenshot;
        switch (sel.Action)
        {
            case CaptureAction.Copy:
                Finish(sel.Image, copy: true, save: s.AutoSaveFile);
                sel.Image.Dispose();
                break;

            case CaptureAction.Save:
                Finish(sel.Image, copy: s.AutoCopy, save: true);
                sel.Image.Dispose();
                break;

            case CaptureAction.SaveAs:
                try
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                        DefaultExt = ".png",
                        FileName = $"VelvetTools_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                        InitialDirectory = System.IO.Directory.Exists(s.SaveDir) ? s.SaveDir : null,
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        var fmt = dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                            ? System.Drawing.Imaging.ImageFormat.Jpeg
                            : System.Drawing.Imaging.ImageFormat.Png;
                        sel.Image.Save(dialog.FileName, fmt);
                        Toast.Show("已保存 " + System.IO.Path.GetFileName(dialog.FileName));
                    }
                    if (s.AutoCopy) CaptureService.CopyToClipboard(sel.Image);
                }
                catch (Exception ex)
                {
                    Logger.Error("另存截图失败", ex);
                    Toast.Show("保存失败：" + ex.Message);
                }
                finally { sel.Image.Dispose(); }
                break;

            case CaptureAction.Pin:
                Native.GetCursorPos(out var cursor);
                var rect = new Rectangle(
                    Math.Max(0, cursor.X - sel.Image.Width / 2),
                    Math.Max(0, cursor.Y - sel.Image.Height / 2),
                    sel.Image.Width, sel.Image.Height);
                new PinWindow(sel.Image, rect).Show(); // PinWindow 接管位图生命周期
                if (s.AutoCopy) CaptureService.CopyToClipboard(sel.Image);
                break;

            case CaptureAction.Ocr:
                await RunOcrFlowAsync(sel.Image); // 接管位图
                break;

            case CaptureAction.Translate:
                await RunTranslateFlowAsync(sel.Image); // 接管位图
                break;

            default:
                // 兜底：将来新增动作忘了处理时，至少别泄漏非托管位图
                Logger.Warn($"未处理的截图动作：{sel.Action}");
                sel.Image.Dispose();
                break;
        }
    }

    private static void Finish(Bitmap bmp, bool copy, bool save)
    {
        string message = "";
        if (copy)
            message = CaptureService.CopyToClipboard(bmp) ? "截图已复制到剪贴板" : "复制到剪贴板失败";
        if (save)
        {
            var path = CaptureService.SaveToDir(bmp, App.Services.Settings.Screenshot.SaveDir);
            message += (message.Length > 0 ? "，" : "") + $"已保存 {System.IO.Path.GetFileName(path)}";
        }
        if (message.Length > 0) Toast.Show(message);
    }

    /// <summary>OCR 流程：识别 → 结果窗口。接管位图生命周期。</summary>
    public async Task RunOcrFlowAsync(Bitmap bmp)
    {
        Toast.Show("正在识别文字…");
        try
        {
            string text = await App.Services.Ocr.RecognizeAsync(bmp);
            if (string.IsNullOrWhiteSpace(text))
            {
                Toast.Show("未识别到文字");
                return;
            }
            TextResultWindow.ShowText("OCR 识别结果", text);
        }
        catch (Exception ex)
        {
            Logger.Error("OCR 失败", ex);
            Toast.Show("OCR 失败：" + ex.Message, 3500);
        }
        finally { bmp.Dispose(); }
    }

    /// <summary>截图翻译流程：OCR → 翻译窗口自动翻译。接管位图生命周期。</summary>
    public async Task RunTranslateFlowAsync(Bitmap bmp)
    {
        Toast.Show("正在识别文字…");
        try
        {
            string text = await App.Services.Ocr.RecognizeAsync(bmp);
            if (string.IsNullOrWhiteSpace(text))
            {
                Toast.Show("未识别到文字");
                return;
            }
            TranslateWindow.Open(text, autoTranslate: true);
        }
        catch (Exception ex)
        {
            Logger.Error("截图翻译失败", ex);
            Toast.Show("截图翻译失败：" + ex.Message, 3500);
        }
        finally { bmp.Dispose(); }
    }

    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct POINT { public int X; public int Y; }
    }
}
