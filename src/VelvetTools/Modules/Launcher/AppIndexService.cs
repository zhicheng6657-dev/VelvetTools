using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using VelvetTools.Common;

namespace VelvetTools.Modules.Launcher;

public sealed class AppItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Arguments { get; init; }
    public string? Description { get; init; }
    public ImageSource? Icon { get; set; }
    public string SearchKey { get; init; } = "";
}

/// <summary>
/// 应用索引：扫描开始菜单（系统 + 当前用户）的快捷方式。
/// 思路与 Flow Launcher / ZTools 的启动器一致（两者均 MIT），实现自研。
/// </summary>
public sealed class AppIndexService
{
    private volatile List<AppItem> _apps = new();
    public IReadOnlyList<AppItem> Apps => _apps;
    public event Action? Indexed;

    public void RescanAsync() => Task.Run(Rescan);

    public void Rescan()
    {
        try
        {
            var found = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            };

            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
            };

            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", enumOpts))
                {
                    string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (ext is not (".lnk" or ".url")) continue;

                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (name.Contains("卸载") || name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;

                    string target = file;
                    string? args = null;
                    string? iconSource = null;

                    if (ext == ".lnk")
                    {
                        try
                        {
                            (target, args, iconSource) = ResolveShortcut(file);
                            if (string.IsNullOrWhiteSpace(target)) target = file;
                            string targetExt = System.IO.Path.GetExtension(target).ToLowerInvariant();
                            if (targetExt is ".txt" or ".chm" or ".html" or ".htm" or ".pdf" or ".rtf") continue;
                        }
                        catch { target = file; }
                    }

                    string key = $"{name}|{target}|{args}";
                    if (found.ContainsKey(key)) continue;

                    found[key] = new AppItem
                    {
                        Name = name,
                        Path = ext == ".lnk" ? file : file, // 启动统一走快捷方式，保证工作目录等参数正确
                        Description = ext == ".lnk" ? target : null,
                        SearchKey = name.ToLowerInvariant(),
                        Icon = TryLoadIcon(iconSource ?? target, fallback: file),
                    };
                }
            }

            _apps = found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Logger.Info($"应用索引完成：{_apps.Count} 项");
            Indexed?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error("应用索引失败", ex);
        }
    }

    private static ImageSource? TryLoadIcon(string? source, string fallback)
    {
        foreach (var candidate in new[] { source, fallback })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = candidate;
            int comma = path.LastIndexOf(',');
            if (comma > 1 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma];
            if (!File.Exists(path)) continue;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) continue;
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32, 32));
                src.Freeze();
                return src;
            }
            catch { }
        }
        return null;
    }

    // ---------- IShellLink 解析 ----------
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCom { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    private static (string target, string? args, string? icon) ResolveShortcut(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLinkCom();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0);

            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string target = sb.ToString();

            sb.Clear();
            link.GetArguments(sb, sb.Capacity);
            string args = sb.ToString();

            sb.Clear();
            link.GetIconLocation(sb, sb.Capacity, out _);
            string icon = sb.ToString();

            return (target, string.IsNullOrWhiteSpace(args) ? null : args,
                    string.IsNullOrWhiteSpace(icon) ? null : Environment.ExpandEnvironmentVariables(icon));
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}
