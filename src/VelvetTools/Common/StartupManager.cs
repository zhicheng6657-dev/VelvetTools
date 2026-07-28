using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VelvetTools.Common;

/// <summary>
/// 开机自启与最高权限：
/// 普通模式走 HKCU Run 键；“始终最高权限”模式改用计划任务（RunLevel Highest），
/// 配置一次后，之后每次启动（含开机自启）都不再弹 UAC。
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VelvetTools";
    private const string TaskName = "VelvetTools";

    public static bool RunKeyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static bool TaskExists()
        => Exec("schtasks.exe", $"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>
    /// 设置页显示用：普通模式需要 Run 键，最高权限模式则必须真的包含登录触发器。
    /// 仅供“按需提升”的计划任务不能被误判为开机自启。
    /// </summary>
    public static bool IsAutoStartEnabled() => RunKeyEnabled() || TaskHasLogonTrigger();

    public static bool TaskHasLogonTrigger()
    {
        object? serviceObject = null;
        object? rootFolder = null;
        object? registeredTask = null;
        object? definition = null;
        object? triggers = null;

        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType is null) return false;

            serviceObject = Activator.CreateInstance(schedulerType);
            if (serviceObject is null) return false;

            dynamic service = serviceObject;
            service.Connect();
            rootFolder = service.GetFolder("\\");
            registeredTask = ((dynamic)rootFolder).GetTask(TaskName);
            definition = ((dynamic)registeredTask).Definition;
            triggers = ((dynamic)definition).Triggers;

            int count = ((dynamic)triggers).Count;
            for (int index = 1; index <= count; index++)
            {
                object trigger = ((dynamic)triggers).Item(index);
                try
                {
                    // TASK_TRIGGER_LOGON = 9
                    if ((int)((dynamic)trigger).Type == 9
                        && (bool)((dynamic)trigger).Enabled)
                    {
                        return true;
                    }
                }
                finally
                {
                    ReleaseComObject(trigger);
                }
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(triggers);
            ReleaseComObject(definition);
            ReleaseComObject(registeredTask);
            ReleaseComObject(rootFolder);
            ReleaseComObject(serviceObject);
        }

        return false;
    }

    /// <summary>通过已注册的计划任务以最高权限拉起新实例（无 UAC 弹窗）。</summary>
    public static bool TryRunElevatedTask()
        => TaskExists() && Exec("schtasks.exe", $"/Run /TN \"{TaskName}\"") == 0;

    /// <summary>应用自启与权限配置。alwaysAdmin=true 时需要当前进程已是管理员（否则抛异常）。</summary>
    public static void Apply(bool autoStart, bool alwaysAdmin)
    {
        // 清理旧版本遗留
        try
        {
            using var legacy = Registry.CurrentUser.CreateSubKey(RunKey);
            legacy.DeleteValue("FrostBox", throwOnMissingValue: false);
        }
        catch { }

        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

        if (alwaysAdmin)
        {
            if (ExecPowerShell(BuildRegisterTaskCommand(exe, autoStart)) != 0)
                throw new InvalidOperationException("创建计划任务失败（需要管理员权限）");

            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            ExecPowerShell($"Unregister-ScheduledTask -TaskName '{TaskName}' -Confirm:$false -ErrorAction SilentlyContinue");
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (autoStart)
                key.SetValue(ValueName, $"\"{exe}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        Logger.Info($"自启配置已应用：autoStart={autoStart} alwaysAdmin={alwaysAdmin}");
    }

    /// <summary>
    /// 普通权限进程配置“始终最高权限”：弹一次 UAC，由提权的 PowerShell 完成计划任务注册，
    /// 免去“先以管理员重启再回来保存一次”。用户取消 UAC 时返回 false，不抛异常。
    /// </summary>
    public static bool TryApplyAdminElevated(bool autoStart)
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        if (!ExecPowerShellElevated(BuildRegisterTaskCommand(exe, autoStart)))
            return false;

        // 验证任务真的建成了，再清理普通模式的 Run 键，避免双重自启。
        if (!TaskExists()) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
        Logger.Info($"已通过 UAC 提权完成最高权限配置：autoStart={autoStart}");
        return true;
    }

    /// <summary>
    /// 普通权限进程删除由管理员创建的最高权限任务：静默删除失败后弹一次 UAC 重试。
    /// 否则关不掉“始终最高权限”，下次启动仍会被计划任务接管。
    /// </summary>
    public static bool TryRemoveTaskElevated()
    {
        if (!TaskExists()) return true;
        ExecPowerShell($"Unregister-ScheduledTask -TaskName '{TaskName}' -Confirm:$false -ErrorAction SilentlyContinue");
        if (!TaskExists()) return true;
        ExecPowerShellElevated($"Unregister-ScheduledTask -TaskName '{TaskName}' -Confirm:$false -ErrorAction SilentlyContinue");
        return !TaskExists();
    }

    private static string BuildRegisterTaskCommand(string exe, bool autoStart)
    {
        string trigger = autoStart ? "-Trigger (New-ScheduledTaskTrigger -AtLogOn)" : "";
        return
            $"Unregister-ScheduledTask -TaskName '{TaskName}' -Confirm:$false -ErrorAction SilentlyContinue; " +
            $"$a = New-ScheduledTaskAction -Execute '{exe}'; " +
            "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); " +
            $"Register-ScheduledTask -TaskName '{TaskName}' -Action $a -RunLevel Highest -Settings $s {trigger} | Out-Null";
    }

    private static int Exec(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return -1;
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
            return p.ExitCode;
        }
        catch { return -1; }
    }

    private static int ExecPowerShell(string command)
        => Exec("powershell.exe",
            "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + command.Replace("\"", "\\\"") + "\"");

    /// <summary>以管理员身份运行 PowerShell 命令（触发 UAC）；用户拒绝授权时返回 false。</summary>
    private static bool ExecPowerShellElevated(string command)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + command.Replace("\"", "\\\"") + "\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return false;
            if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 用户在 UAC 弹窗点了“否”
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); }
            catch { }
        }
    }
}
