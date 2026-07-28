using System.Diagnostics;
using System.IO;
using System.Text;
using VelvetTools.Common;

namespace VelvetTools.Modules.Search;

/// <summary>
/// Everything 索引引擎的启动器。
///
/// 引擎**随本软件一起分发**（`Assets/Everything/`，随输出目录拷贝），开箱即用，
/// 用户不需要下载或安装任何东西。首次用到文件搜索时静默拉起，退出时一并关闭。
///
/// 许可：Everything 主程序与 SDK 随附 MIT 风格许可文本
/// （Copyright © David Carpenter）；二进制与许可原文一起放在同目录。
/// </summary>
public static class EverythingBootstrap
{
    /// <summary>随程序分发的引擎目录。</summary>
    private static string BundledDir =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Everything");

    private static string BundledExe => Path.Combine(BundledDir, "Everything.exe");
    private static string PrivateDataDir => Path.Combine(Logger.DataDir, "everything-index");
    private static string PrivateConfig => Path.Combine(PrivateDataDir, "Everything.ini");

    /// <summary>系统里已装的 Everything（有就优先复用，避免重复建索引占资源）。</summary>
    public static string? FindSystemInstall()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "Everything.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 实际要启动的引擎路径：固定优先随包的完整版本。
    /// 已经运行的系统版仍会直接复用；这里只避免误启动不支持 IPC 的 Lite 安装版。
    /// </summary>
    public static string? ResolveExe() => File.Exists(BundledExe) ? BundledExe : FindSystemInstall();

    private static int? _startedProcessId;
    private static string? _startedExe;

    /// <summary>
    /// 确保引擎在跑。已在跑直接返回；没在跑就静默拉起并等待 IPC 就绪。
    /// 首次运行需要建索引，通常几秒内完成。
    /// </summary>
    public static async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (EverythingClient.IsUsable) return true;

        string? exe = ResolveExe();
        if (exe is null)
        {
            Logger.Warn("未找到 Everything 引擎（自带副本缺失）");
            return false;
        }

        try
        {
            PreparePrivateConfig();
            string arguments =
                $"-instance \"{EverythingClient.PrivateInstanceName}\" " +
                $"-config \"{PrivateConfig}\" -startup -minimized";

            using var started = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            _startedProcessId = started?.Id;
            _startedExe = exe;
            Logger.Info($"已启动 Everything 私有索引实例：{exe}");
        }
        catch (Exception ex)
        {
            Logger.Error("启动 Everything 引擎失败", ex);
            return false;
        }

        // NTFS 索引通常几秒；普通权限下的文件夹索引会慢一些，
        // 但出现首批结果后即可先提供搜索。
        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(500, ct);
            if (EverythingClient.IsUsable) return true;
        }
        return false;
    }

    /// <summary>
    /// 为随包实例生成独立配置，不污染用户自己的 Everything。
    /// 管理员进程走高速 NTFS 索引；普通进程用官方文件夹索引覆盖全部固定磁盘，
    /// 不安装服务、不触发 UAC。
    /// </summary>
    private static void PreparePrivateConfig()
    {
        Directory.CreateDirectory(PrivateDataDir);

        var roots = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed)
            .Select(d =>
            {
                try { return d.IsReady ? d.RootDirectory.FullName : null; }
                catch { return null; }
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();

        var ini = new StringBuilder();
        ini.AppendLine("[Everything]");
        ini.AppendLine("app_data=0");
        ini.AppendLine("run_as_admin=0");
        ini.AppendLine("show_tray_icon=0");
        ini.AppendLine("check_for_updates_on_startup=0");
        ini.AppendLine("start_minimized=1");
        ini.AppendLine("hide_on_close=1");
        ini.AppendLine("run_in_background=1");
        ini.AppendLine("ipc=1");
        ini.AppendLine($"db_location={PrivateDataDir}");
        ini.AppendLine($"auto_include_fixed_volumes={(Elevation.IsAdmin ? 1 : 0)}");
        ini.AppendLine("auto_include_removable_volumes=0");

        if (!Elevation.IsAdmin && roots.Count > 0)
        {
            string values = string.Join(",", roots);
            string ones = string.Join(",", Enumerable.Repeat("1", roots.Count));
            string zeros = string.Join(",", Enumerable.Repeat("0", roots.Count));
            string buffers = string.Join(",", Enumerable.Repeat("65536", roots.Count));
            ini.AppendLine("folders=" + values);
            ini.AppendLine("folder_monitor_changes=" + ones);
            ini.AppendLine("folder_buffer_size_list=" + buffers);
            ini.AppendLine("folder_rescan_if_full_list=" + ones);
            ini.AppendLine("folder_update_types=" + zeros);
            ini.AppendLine("folder_update_days=" + zeros);
            ini.AppendLine("folder_update_ats=" + zeros);
            ini.AppendLine("folder_update_intervals=" + zeros);
            ini.AppendLine("folder_update_interval_types=" + zeros);
        }

        File.WriteAllText(PrivateConfig, ini.ToString(), new UTF8Encoding(false));
    }

    /// <summary>退出应用时让我们自己拉起的自带实例正常保存索引并退出（系统安装版不动）。</summary>
    public static void StopIfStartedByUs()
    {
        if (_startedProcessId is not int processId) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return;

            if (_startedExe is { } exe && File.Exists(exe))
            {
                using var exit = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments =
                        $"-instance \"{EverythingClient.PrivateInstanceName}\" " +
                        $"-config \"{PrivateConfig}\" -exit",
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                exit?.WaitForExit(2000);
            }
        }
        catch { }
        finally { _startedProcessId = null; _startedExe = null; }
    }
}
