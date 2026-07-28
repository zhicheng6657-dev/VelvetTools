using System.Runtime.InteropServices;
using System.IO;

namespace VelvetTools.Modules.Search;

/// <summary>
/// voidtools 官方 Everything SDK 的最小托管封装。
/// 仅声明本应用需要的导出，不复制 SDK 示例源码；SDK DLL 与 Everything 共用其 MIT 许可。
/// </summary>
public sealed class EverythingSdk
{
    private const uint RequestFileName = 0x00000001;
    private const uint RequestPath = 0x00000002;

    private static readonly Lazy<EverythingSdk?> Instance = new(Create);

    private readonly IntPtr _library;
    private readonly ResetDelegate _reset;
    private readonly SetSearchDelegate _setSearch;
    private readonly SetBoolDelegate _setMatchPath;
    private readonly SetBoolDelegate _setMatchCase;
    private readonly SetBoolDelegate _setMatchWholeWord;
    private readonly SetBoolDelegate _setRegex;
    private readonly SetUIntDelegate _setMax;
    private readonly SetUIntDelegate _setOffset;
    private readonly SetUIntDelegate _setRequestFlags;
    private readonly QueryDelegate _query;
    private readonly GetUIntDelegate _getLastError;
    private readonly GetUIntDelegate _getNumResults;
    private readonly GetUIntDelegate _getTotalResults;
    private readonly GetBoolDelegate _isDatabaseLoaded;
    private readonly GetResultStringDelegate _getResultFileName;
    private readonly GetResultStringDelegate _getResultPath;
    private readonly IsResultDelegate _isFolderResult;

    private EverythingSdk(string path)
    {
        _library = NativeLibrary.Load(path);
        _reset = Export<ResetDelegate>("Everything_Reset");
        _setSearch = Export<SetSearchDelegate>("Everything_SetSearchW");
        _setMatchPath = Export<SetBoolDelegate>("Everything_SetMatchPath");
        _setMatchCase = Export<SetBoolDelegate>("Everything_SetMatchCase");
        _setMatchWholeWord = Export<SetBoolDelegate>("Everything_SetMatchWholeWord");
        _setRegex = Export<SetBoolDelegate>("Everything_SetRegex");
        _setMax = Export<SetUIntDelegate>("Everything_SetMax");
        _setOffset = Export<SetUIntDelegate>("Everything_SetOffset");
        _setRequestFlags = Export<SetUIntDelegate>("Everything_SetRequestFlags");
        _query = Export<QueryDelegate>("Everything_QueryW");
        _getLastError = Export<GetUIntDelegate>("Everything_GetLastError");
        _getNumResults = Export<GetUIntDelegate>("Everything_GetNumResults");
        _getTotalResults = Export<GetUIntDelegate>("Everything_GetTotResults");
        _isDatabaseLoaded = Export<GetBoolDelegate>("Everything_IsDBLoaded");
        _getResultFileName = Export<GetResultStringDelegate>("Everything_GetResultFileNameW");
        _getResultPath = Export<GetResultStringDelegate>("Everything_GetResultPathW");
        _isFolderResult = Export<IsResultDelegate>("Everything_IsFolderResult");
    }

    public static bool IsAvailable => Instance.Value is not null;

    public static bool CanConnect
    {
        get
        {
            try { return Instance.Value?._isDatabaseLoaded() == true; }
            catch { return false; }
        }
    }

    public static uint IndexedItemCount
    {
        get
        {
            try { return CanConnect ? Instance.Value?._getTotalResults() ?? 0 : 0; }
            catch { return 0; }
        }
    }

    public static List<SearchHit> Search(string query, int maxResults,
        bool matchCase, bool matchWholeWord, bool regex)
    {
        var sdk = Instance.Value
            ?? throw new DllNotFoundException("Everything SDK DLL 不可用");
        return sdk.Query(query, maxResults, matchCase, matchWholeWord, regex);
    }

    private List<SearchHit> Query(string query, int maxResults,
        bool matchCase, bool matchWholeWord, bool regex)
    {
        _reset();
        try
        {
            _setSearch(query);
            _setMatchPath(true);
            _setMatchCase(matchCase);
            _setMatchWholeWord(matchWholeWord);
            _setRegex(regex);
            _setOffset(0);
            _setMax((uint)Math.Clamp(maxResults, 1, 10_000));
            _setRequestFlags(RequestFileName | RequestPath);

            if (!_query(true))
            {
                uint code = _getLastError();
                throw new EverythingSdkException(code, DescribeError(code));
            }

            uint count = _getNumResults();
            var hits = new List<SearchHit>((int)Math.Min(count, int.MaxValue));
            for (uint i = 0; i < count; i++)
            {
                string name = Marshal.PtrToStringUni(_getResultFileName(i)) ?? "";
                if (name.Length == 0) continue;
                string path = Marshal.PtrToStringUni(_getResultPath(i)) ?? "";
                hits.Add(new SearchHit(name, path, _isFolderResult(i)));
            }
            return hits;
        }
        finally
        {
            _reset();
        }
    }

    private T Export<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private static EverythingSdk? Create()
    {
        try
        {
            string fileName = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "Everything32.dll",
                Architecture.X64 => "Everything64.dll",
                Architecture.Arm => "EverythingARM.dll",
                Architecture.Arm64 => "EverythingARM64.dll",
                _ => "",
            };
            if (fileName.Length == 0) return null;

            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Everything", fileName);
            if (!File.Exists(path)) return null;
            return new EverythingSdk(path);
        }
        catch (Exception ex)
        {
            Common.Logger.Warn("加载 Everything 官方 SDK 失败，将降级到 IPC：" + ex.Message);
            return null;
        }
    }

    private static string DescribeError(uint code) => code switch
    {
        1 => "内存不足",
        2 => "Everything 搜索进程未运行",
        3 => "无法注册 SDK 消息窗口",
        4 => "无法创建 SDK 消息窗口",
        5 => "无法创建 SDK 查询线程",
        6 => "结果索引无效",
        7 => "SDK 调用顺序无效",
        8 => "未请求所需结果字段",
        9 => "参数无效",
        _ => $"未知错误 {code}",
    };

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ResetDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate void SetSearchDelegate([MarshalAs(UnmanagedType.LPWStr)] string value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetBoolDelegate([MarshalAs(UnmanagedType.Bool)] bool value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetUIntDelegate(uint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool QueryDelegate([MarshalAs(UnmanagedType.Bool)] bool wait);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint GetUIntDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GetBoolDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetResultStringDelegate(uint index);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool IsResultDelegate(uint index);
}

internal sealed class EverythingSdkException(uint errorCode, string message) : Exception(message)
{
    public uint ErrorCode { get; } = errorCode;
}
