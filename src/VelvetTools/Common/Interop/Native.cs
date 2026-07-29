using System.Runtime.InteropServices;

namespace VelvetTools.Common.Interop;

/// <summary>集中存放通用 Win32 P/Invoke 声明。模块专用的互操作放在各自模块内。</summary>
internal static class Native
{
    // ---------- window ----------
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern uint RegisterWindowMessage(string lpString);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x0080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT Reserved;
        public POINT MaxSize;
        public POINT MaxPosition;
        public POINT MinTrackSize;
        public POINT MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public uint Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    internal const int WM_GETMINMAXINFO = 0x0024;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // ---------- hotkey ----------
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    internal const int WM_HOTKEY = 0x0312;

    // ---------- clipboard ----------
    [DllImport("user32.dll")] internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    internal const int WM_CLIPBOARDUPDATE = 0x031D;

    // ---------- misc messages ----------
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_SETTINGCHANGE = 0x001A;
    internal const int WM_APP = 0x8000;

    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    internal const int SM_CXSMICON = 49;

    // ---------- taskbar embedding ----------
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowW(string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string? className, string? windowName);
    [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr hwnd, IntPtr newParent);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    internal const int GWL_STYLE = -16;
    internal const int WS_CHILD = 0x40000000;
    internal const uint WS_POPUP_STYLE = 0x80000000;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const uint LWA_ALPHA = 0x02;

    // ---------- 全屏应用检测（信息栏让路用） ----------
    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(out int state);
    // 2=BUSY 3=RUNNING_D3D_FULL_SCREEN 4=PRESENTATION_MODE

    // ---------- system sampling（只读监控：CPU / 物理内存占用） ----------
    [DllImport("kernel32.dll")]
    internal static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ---------- icon ----------
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr hIcon);

    // ---------- keyboard input (模拟 Ctrl+V) ----------
    [DllImport("user32.dll")] internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_V = 0x56;

    /// <summary>向当前前台窗口发送 Ctrl+V。</summary>
    internal static void SendCtrlV()
    {
        var inputs = new INPUT[4];
        inputs[0] = Key(VK_CONTROL, false);
        inputs[1] = Key(VK_V, false);
        inputs[2] = Key(VK_V, true);
        inputs[3] = Key(VK_CONTROL, true);
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        static INPUT Key(ushort vk, bool up) => new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
        };
    }
}
