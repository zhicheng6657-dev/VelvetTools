using System.Diagnostics;
using System.Net.NetworkInformation;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.NetSpeed;

public sealed record SystemSample(double DownBps, double UpBps, double MemPercent, double MemUsedGb, double MemTotalGb,
    double CpuPercent, double MemAvailMb, double PageFileAvailMb, double PageFileTotalMb);

/// <summary>每秒采样一次全局网速（所有物理网卡收发字节增量）与内存占用。</summary>
public sealed class NetSpeedService : IDisposable
{
    private readonly System.Timers.Timer _timer = new(1000);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastRx = -1, _lastTx = -1;
    private double _lastElapsed;
    private long _lastIdle = -1, _lastKernel, _lastUser;

    public SystemSample Latest { get; private set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    public event Action<SystemSample>? Sampled;

    public NetSpeedService()
    {
        _timer.Elapsed += (_, _) => Tick();
        _timer.AutoReset = true;
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var st = nic.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }

            double now = _clock.Elapsed.TotalSeconds;
            double down = 0, up = 0;
            if (_lastRx >= 0 && now > _lastElapsed)
            {
                double dt = now - _lastElapsed;
                down = Math.Max(0, (rx - _lastRx) / dt);
                up = Math.Max(0, (tx - _lastTx) / dt);
            }
            _lastRx = rx; _lastTx = tx; _lastElapsed = now;

            var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            Native.GlobalMemoryStatusEx(ref mem);
            double totalGb = mem.ullTotalPhys / 1073741824.0;
            double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;

            // CPU：GetSystemTimes 增量。kernel 包含 idle，故 busy = (kernel-idle) + user。
            double cpu = 0;
            if (Native.GetSystemTimes(out long idle, out long kernel, out long user))
            {
                if (_lastIdle >= 0)
                {
                    long idleDelta = idle - _lastIdle;
                    long totalDelta = (kernel - _lastKernel) + (user - _lastUser);
                    if (totalDelta > 0)
                        cpu = Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
                }
                _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
            }
            else
            {
                cpu = Latest.CpuPercent;
            }

            Latest = new SystemSample(down, up, mem.dwMemoryLoad, usedGb, totalGb,
                cpu,
                mem.ullAvailPhys / 1048576.0,
                mem.ullAvailPageFile / 1048576.0,
                mem.ullTotalPageFile / 1048576.0);
            Sampled?.Invoke(Latest);
        }
        catch (Exception ex)
        {
            Common.Logger.Error("网速采样失败", ex);
        }
    }

    /// <summary>把字节速率格式化为紧凑字符串，如 0K / 23K / 1.2M / 456M / 1.1G。</summary>
    public static string FormatSpeed(double bps)
    {
        return bps switch
        {
            < 1024 => "0K",
            < 10 * 1024 => $"{bps / 1024:0.0}K",
            < 1024 * 1024 => $"{bps / 1024:0}K",
            < 10 * 1024 * 1024 => $"{bps / (1024.0 * 1024):0.0}M",
            < 1024L * 1024 * 1024 => $"{bps / (1024.0 * 1024):0}M",
            _ => $"{bps / (1024.0 * 1024 * 1024):0.0}G",
        };
    }

    /// <summary>任务栏用：固定两位小数 + 单位，如 0.42KB/s。</summary>
    public static string FormatSpeedTaskbar(double bps)
    {
        return bps switch
        {
            < 1024 * 1024 => $"{bps / 1024:0.00}KB/s",
            < 1024L * 1024 * 1024 => $"{bps / (1024.0 * 1024):0.00}MB/s",
            _ => $"{bps / (1024.0 * 1024 * 1024):0.00}GB/s",
        };
    }

    /// <summary>较完整的速率文本，如 1.25 MB/s。</summary>
    public static string FormatSpeedLong(double bps)
    {
        return bps switch
        {
            < 1024 => $"{bps:0} B/s",
            < 1024 * 1024 => $"{bps / 1024:0.0} KB/s",
            < 1024L * 1024 * 1024 => $"{bps / (1024.0 * 1024):0.00} MB/s",
            _ => $"{bps / (1024.0 * 1024 * 1024):0.00} GB/s",
        };
    }

    public void Dispose() => _timer.Dispose();
}
