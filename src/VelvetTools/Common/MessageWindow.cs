using System.Windows.Interop;

namespace VelvetTools.Common;

/// <summary>
/// 隐藏消息窗口：承载托盘回调、全局热键 WM_HOTKEY、剪贴板 WM_CLIPBOARDUPDATE、
/// 显示器变更 WM_DISPLAYCHANGE 等。必须在 UI 线程创建。
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<Func<int, IntPtr, IntPtr, bool>> _hooks = new();

    public IntPtr Handle => _source.Handle;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("VelvetTools.MessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -10000,
            PositionY = -10000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>注册消息钩子；返回 true 表示已处理。</summary>
    public void AddHook(Func<int, IntPtr, IntPtr, bool> hook) => _hooks.Add(hook);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                if (hook(msg, wParam, lParam))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MessageWindow hook 异常", ex);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
