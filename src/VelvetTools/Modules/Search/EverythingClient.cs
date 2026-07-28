using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using VelvetTools.Common;

namespace VelvetTools.Modules.Search;

public sealed record SearchHit(string Name, string Path, bool IsFolder)
{
    public string FullPath => System.IO.Path.Combine(Path, Name);
}

/// <summary>
/// Everything 文件搜索客户端。
///
/// 主路径使用 voidtools 官方 SDK DLL；若 DLL 被安全软件移除或加载失败，
/// 自动降级到公开的 WM_COPYDATA IPC 协议，避免整个搜索功能失效。
/// </summary>
public sealed class EverythingClient : IDisposable
{
    private static readonly SemaphoreSlim SdkGate = new(1, 1);

    private const string IpcWindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";
    internal const string PrivateInstanceName = "VelvetTools";
    private const string PrivateIpcWindowClass = IpcWindowClass + "_(" + PrivateInstanceName + ")";
    private const int WM_COPYDATA = 0x004A;
    private const int EverythingWmIpc = 0x0400; // WM_USER
    private const int EverythingIpcIsDbLoaded = 401;

    private const uint COPYDATA_QUERYW = 2;          // 发送 Unicode 查询
    private const uint REPLY_COPYDATA_MESSAGE = 0;   // 回包时的 dwData 标识（自定义）

    // 搜索标志
    private const uint MATCHCASE = 0x0001;
    private const uint MATCHWHOLEWORD = 0x0002;
    private const uint MATCHPATH = 0x0004;
    private const uint REGEX = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendSimpleMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>承接 Everything 回包的隐藏窗口（每次查询共用）。</summary>
    private readonly HwndSource _replyWindow;
    private TaskCompletionSource<List<SearchHit>>? _pending;

    public EverythingClient()
    {
        var p = new HwndSourceParameters("VelvetTools.EverythingReply")
        {
            Width = 0, Height = 0, PositionX = -10000, PositionY = -10000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
        };
        _replyWindow = new HwndSource(p);
        _replyWindow.AddHook(WndProc);
    }

    /// <summary>Everything 是否正在运行（未运行时搜索不可用）。</summary>
    public static bool IsRunning => FindWindowW(IpcWindowClass, null) != IntPtr.Zero
        || FindWindowW(PrivateIpcWindowClass, null) != IntPtr.Zero;

    public static bool IsUsable => DefaultSdkUsable || IsPrivateDatabaseLoaded();

    private static bool DefaultSdkUsable =>
        EverythingSdk.IsAvailable
            ? EverythingSdk.CanConnect && EverythingSdk.IndexedItemCount > 0
            : FindWindowW(IpcWindowClass, null) != IntPtr.Zero;

    private static bool IsPrivateDatabaseLoaded()
    {
        IntPtr hwnd = FindWindowW(PrivateIpcWindowClass, null);
        return hwnd != IntPtr.Zero
            && SendSimpleMessageW(hwnd, EverythingWmIpc,
                new IntPtr(EverythingIpcIsDbLoaded), IntPtr.Zero) != IntPtr.Zero;
    }

    internal static string? RunningPrivateProcessPath()
    {
        IntPtr hwnd = FindWindowW(PrivateIpcWindowClass, null);
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == 0) return null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.MainModule?.FileName;
        }
        catch { return null; }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);


    /// <summary>
    /// 执行搜索。Everything 未运行返回 null；否则返回结果（可能为空列表）。
    /// </summary>
    public async Task<List<SearchHit>?> SearchAsync(string query, int maxResults,
        bool matchCase, bool matchWholeWord, bool regex, CancellationToken ct = default)
    {
        bool useSdk = DefaultSdkUsable;
        IntPtr target = useSdk
            ? FindWindowW(IpcWindowClass, null)
            : FindWindowW(PrivateIpcWindowClass, null);
        if (target == IntPtr.Zero) return null;
        if (string.IsNullOrWhiteSpace(query)) return new List<SearchHit>();

        if (useSdk && EverythingSdk.IsAvailable)
        {
            await SdkGate.WaitAsync(ct);
            try
            {
                return await Task.Run(
                    () => EverythingSdk.Search(query, maxResults, matchCase, matchWholeWord, regex), ct);
            }
            catch (EverythingSdkException ex) when (ex.ErrorCode == 2)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn("Everything SDK 查询失败，降级到 IPC：" + ex.Message);
            }
            finally
            {
                SdkGate.Release();
            }
        }

        return await SearchViaIpcAsync(target, query, maxResults, matchCase, matchWholeWord, regex, ct);
    }

    /// <summary>
    /// 冒烟测试专用的同步入口。IPC 回包依赖当前 WPF Dispatcher，所以等待期间运行一个
    /// 受控的嵌套消息泵；产品搜索仍使用上面的异步 API。
    /// </summary>
    internal List<SearchHit>? SearchForSelfTest(string query, int maxResults = 10)
    {
        var task = SearchAsync(query, maxResults, matchCase: false, matchWholeWord: false, regex: false);
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(
                _ => _replyWindow.Dispatcher.BeginInvoke(() => frame.Continue = false),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }

    private async Task<List<SearchHit>> SearchViaIpcAsync(IntPtr target, string query, int maxResults,
        bool matchCase, bool matchWholeWord, bool regex, CancellationToken ct)
    {
        // 同一时刻只允许一个查询在飞（新查询取消旧的）
        _pending?.TrySetResult(new List<SearchHit>());
        var tcs = new TaskCompletionSource<List<SearchHit>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;

        uint flags = MATCHPATH;
        if (matchCase) flags |= MATCHCASE;
        if (matchWholeWord) flags |= MATCHWHOLEWORD;
        if (regex) flags |= REGEX;

        // EVERYTHING_IPC_QUERYW: 5 个 DWORD + 以 null 结尾的宽字符串
        const int headerSize = 5 * 4;
        byte[] queryBytes = Encoding.Unicode.GetBytes(query + "\0");
        int total = headerSize + queryBytes.Length;

        IntPtr buffer = Marshal.AllocHGlobal(total);
        try
        {
            Marshal.WriteInt32(buffer, 0, (int)_replyWindow.Handle.ToInt64()); // reply_hwnd（低 32 位）
            Marshal.WriteInt32(buffer, 4, (int)REPLY_COPYDATA_MESSAGE);
            Marshal.WriteInt32(buffer, 8, (int)flags);
            Marshal.WriteInt32(buffer, 12, 0);                                  // offset
            Marshal.WriteInt32(buffer, 16, maxResults);
            Marshal.Copy(queryBytes, 0, buffer + headerSize, queryBytes.Length);

            var cds = new COPYDATASTRUCT
            {
                dwData = new IntPtr(COPYDATA_QUERYW),
                cbData = total,
                lpData = buffer,
            };

            IntPtr ok = SendMessageW(target, WM_COPYDATA, _replyWindow.Handle, ref cds);
            if (ok == IntPtr.Zero)
            {
                _pending = null;
                Logger.Warn("Everything 拒绝了查询请求");
                return new List<SearchHit>();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        // 超时保护：Everything 正常在毫秒级返回
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        // 取消要真的取消：以前 TrySetResult(空表) 会让被取消的旧查询
        // 以"0 个结果"正常返回，把新查询的结果擦掉
        using (linked.Token.Register(() => tcs.TrySetCanceled(linked.Token)))
        {
            return await tcs.Task;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_COPYDATA || _pending is null) return IntPtr.Zero;

        try
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (cds.dwData.ToInt64() != REPLY_COPYDATA_MESSAGE) return IntPtr.Zero;

            var hits = ParseList(cds.lpData);
            _pending.TrySetResult(hits);
            _pending = null;
            handled = true;
            return new IntPtr(1);
        }
        catch (Exception ex)
        {
            Logger.Error("解析 Everything 回包失败", ex);
            _pending?.TrySetResult(new List<SearchHit>());
            _pending = null;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 解析 EVERYTHING_IPC_LISTW：
    /// 7 个 DWORD 头（totfolders/totfiles/totitems/numfolders/numfiles/numitems/offset），
    /// 随后是 numitems 个 ITEM（flags、filename_offset、path_offset —— 偏移相对于结构体起始）。
    /// </summary>
    private static List<SearchHit> ParseList(IntPtr data)
    {
        var hits = new List<SearchHit>();
        const int headerSize = 7 * 4;
        const int itemSize = 3 * 4;
        const uint FLAG_FOLDER = 0x01;

        int numItems = Marshal.ReadInt32(data, 5 * 4);
        for (int i = 0; i < numItems; i++)
        {
            int itemBase = headerSize + i * itemSize;
            uint flags = (uint)Marshal.ReadInt32(data, itemBase);
            int nameOffset = Marshal.ReadInt32(data, itemBase + 4);
            int pathOffset = Marshal.ReadInt32(data, itemBase + 8);

            string name = Marshal.PtrToStringUni(data + nameOffset) ?? "";
            string path = Marshal.PtrToStringUni(data + pathOffset) ?? "";
            if (name.Length == 0) continue;

            hits.Add(new SearchHit(name, path, (flags & FLAG_FOLDER) != 0));
        }
        return hits;
    }

    public void Dispose()
    {
        _pending?.TrySetResult(new List<SearchHit>());
        _replyWindow.Dispose();
    }
}
