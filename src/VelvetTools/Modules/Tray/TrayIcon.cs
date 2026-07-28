using System.Runtime.InteropServices;
using VelvetTools.Common;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.Tray;

/// <summary>
/// 托盘图标（Shell_NotifyIcon 自研封装，无第三方依赖）。
/// 就是一个普通应用的常驻图标：单个 V 图标 + 悬停提示 + 左右键回调，
/// 实时数据由任务栏内嵌信息栏负责显示，不往托盘里塞数字。
/// Explorer 重启后自动重挂。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04;
    private const int WM_TRAYCALLBACK = Native.WM_APP + 100;
    private const int WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMsg;
    private IntPtr _icon;
    private bool _added;
    private string _tip = "Velvet Tools";

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
        _window.AddHook(OnMessage);

        _icon = TrayIconRenderer.RenderLogo();
        Add();
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYCALLBACK)
        {
            switch (lParam.ToInt32())
            {
                case WM_LBUTTONUP:
                    // 不接 WM_LBUTTONDBLCLK：双击的消息序列是 DOWN→UP→DBLCLK→UP，
                    // 两个都接会让一次双击触发三次回调（面板疯狂闪烁）
                    LeftClick?.Invoke();
                    return true;
                case WM_RBUTTONUP:
                    RightClick?.Invoke();
                    return true;
            }
            return false;
        }

        if (msg == (int)_taskbarCreatedMsg)
        {
            _added = false;
            Add();
        }
        return false;
    }

    private NOTIFYICONDATAW BuildData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_TRAYCALLBACK,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void Add()
    {
        var data = BuildData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
    }

    public void SetTooltip(string tip)
    {
        _tip = tip.Length > 127 ? tip[..127] : tip;
        if (!_added) return;
        var data = BuildData(NIF_TIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = BuildData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            Native.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
