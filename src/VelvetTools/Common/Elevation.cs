using System.Diagnostics;
using System.Security.Principal;

namespace VelvetTools.Common;

/// <summary>管理员权限辅助（供“始终最高权限”计划任务与设置页使用）。</summary>
public static class Elevation
{
    public static bool IsAdmin
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static void RestartAsAdmin()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas",
            };
            Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
        }
        catch
        {
            // 用户取消 UAC，忽略
        }
    }
}
