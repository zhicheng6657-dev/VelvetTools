using System.Diagnostics;
using System.Globalization;
using System.Management;
using VelvetTools.Common;
using File = System.IO.File;
using Path = System.IO.Path;

namespace VelvetTools.Modules.Hardware;

public sealed record TemperatureSample(
    double? CpuTemp, double? GpuTemp, double? DiskTemp,
    double? GpuLoad, string? GpuName, string? CpuName,
    IReadOnlyList<(string Name, double Temp)> Disks,
    string? CpuSource = null, string? GpuSource = null, string? DiskSource = null);

/// <summary>
/// 无内核驱动的硬件温度探测。
///
/// 数据源按安全边界分为三类：
/// 1. Windows ACPI Thermal Zone（并非所有主板都会公开，也不一定等同 CPU 核心温度）；
/// 2. 用户已自行运行的 LibreHardwareMonitor/OpenHardwareMonitor WMI 提供程序（只读查询，
///    本应用不捆绑、不加载也不控制它们的驱动）；
/// 3. NVIDIA 显卡驱动安装在受保护系统目录中的 nvidia-smi，只执行只读查询参数；
/// 4. 硬盘温度经 Windows 存储栈自带的只读 IOCTL（见 DiskTemperatureReader）。
///
/// 不下载、不安装、不解压 WinRing0、PawnIO 或其他第三方内核驱动。
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly System.Timers.Timer _timer = new(5000);
    private readonly string? _nvidiaSmiPath = ResolveNvidiaSmiPath();
    private int _sampling;
    private string? _lastSourceSummary;

    public TemperatureSample Latest { get; private set; } =
        new(null, null, null, null, null, null, Array.Empty<(string, double)>());

    /// <summary>探测器始终可运行；具体电脑没有公开读数时 Latest 中相应项为空。</summary>
    public bool IsAvailable => true;
    public string? UnavailableReason { get; private set; }

    public event Action<TemperatureSample>? Sampled;

    public HardwareMonitorService()
    {
        _timer.Elapsed += (_, _) => QueueSample();
        _timer.AutoReset = true;
        _timer.Start();
        QueueSample();
        Logger.Info("已启用安全温度探测（不捆绑或加载第三方内核驱动）");
    }

    private void QueueSample()
    {
        if (Interlocked.Exchange(ref _sampling, 1) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                ProviderReading providers = ReadExternalWmiProviders();

                double? cpu = providers.CpuTemp;
                string? cpuName = providers.CpuName;
                string? cpuSource = providers.CpuSource;
                if (cpu is null)
                {
                    cpu = TryReadAcpiTemperature();
                    if (cpu is not null)
                    {
                        // ACPI Thermal Zone 是固件热区，不应错误标成 CPU 核心温度。
                        cpuName = "系统热区";
                        cpuSource = "Windows ACPI Thermal Zone";
                    }
                }

                double? gpu = providers.GpuTemp;
                double? gpuLoad = providers.GpuLoad;
                string? gpuName = providers.GpuName;
                string? gpuSource = providers.GpuSource;
                if (gpu is null)
                {
                    NvidiaReading? nvidia = TryReadNvidiaSmi();
                    if (nvidia is not null)
                    {
                        gpu = nvidia.Temperature;
                        gpuLoad = nvidia.Load;
                        gpuName = nvidia.Name;
                        gpuSource = "NVIDIA 驱动 nvidia-smi";
                    }
                }

                double? diskTemp = providers.DiskTemp;
                IReadOnlyList<(string Name, double Temp)> disks = providers.Disks;
                string? diskSource = providers.DiskSource;
                if (diskTemp is null)
                {
                    IReadOnlyList<(string Name, double Temp)> ioctlDisks = DiskTemperatureReader.ReadAll();
                    if (ioctlDisks.Count > 0)
                    {
                        disks = ioctlDisks;
                        diskTemp = ioctlDisks.Max(item => item.Temp);
                        diskSource = "Windows 存储接口（只读 IOCTL）";
                    }
                }

                Latest = new TemperatureSample(
                    cpu,
                    gpu,
                    diskTemp,
                    gpuLoad,
                    gpuName,
                    cpuName,
                    disks,
                    cpuSource,
                    gpuSource,
                    diskSource);

                UnavailableReason = cpu is null && gpu is null && diskTemp is null
                    ? "Windows/厂商驱动未公开温度；未加载第三方内核驱动"
                    : cpu is null
                        ? "Windows 未公开 CPU 温度；其他可用温度仍会正常显示"
                        : null;

                LogSourcesIfChanged(Latest);
                Sampled?.Invoke(Latest);
            }
            catch (Exception ex)
            {
                UnavailableReason = "安全温度读取失败：" + ex.Message;
                Logger.Warn(UnavailableReason);
                Latest = new TemperatureSample(null, null, null, null, null, null,
                    Array.Empty<(string, double)>());
                Sampled?.Invoke(Latest);
            }
            finally
            {
                Volatile.Write(ref _sampling, 0);
            }
        });
    }

    private void LogSourcesIfChanged(TemperatureSample sample)
    {
        string summary =
            $"CPU={sample.CpuSource ?? "不可用"}; " +
            $"GPU={sample.GpuSource ?? "不可用"}; " +
            $"Disk={sample.DiskSource ?? "不可用"}";
        if (string.Equals(summary, _lastSourceSummary, StringComparison.Ordinal)) return;

        _lastSourceSummary = summary;
        Logger.Info("温度数据源：" + summary);
    }

    private static double? TryReadAcpiTemperature()
    {
        try
        {
            var values = new List<double>();
            var scope = new ManagementScope(@"\\.\root\WMI");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"),
                new EnumerationOptions
                {
                    ReturnImmediately = false,
                    Timeout = TimeSpan.FromSeconds(2),
                });
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                if (item["CurrentTemperature"] is null) continue;
                double raw = Convert.ToDouble(item["CurrentTemperature"], CultureInfo.InvariantCulture);
                double celsius = raw / 10.0 - 273.15;
                if (IsPlausibleTemperature(celsius))
                    values.Add(celsius);
            }
            return values.Count == 0 ? null : values.Max();
        }
        catch
        {
            // 许多台式机/笔记本会返回“不支持”；这是正常的能力缺失。
            return null;
        }
    }

    private static ProviderReading ReadExternalWmiProviders()
    {
        foreach ((string scope, string displayName) in new[]
                 {
                     (@"\\.\root\LibreHardwareMonitor", "LibreHardwareMonitor WMI"),
                     (@"\\.\root\OpenHardwareMonitor", "OpenHardwareMonitor WMI"),
                 })
        {
            ProviderReading? reading = TryReadExternalWmiProvider(scope, displayName);
            if (reading is not null && reading.HasAnyReading)
                return reading;
        }

        return ProviderReading.Empty;
    }

    private static ProviderReading? TryReadExternalWmiProvider(string scopePath, string sourceName)
    {
        try
        {
            var cpu = new List<SensorValue>();
            var gpu = new List<SensorValue>();
            var disks = new List<SensorValue>();
            var gpuLoads = new List<double>();

            var scope = new ManagementScope(scopePath);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(
                    "SELECT Name, Identifier, Value, SensorType FROM Sensor " +
                    "WHERE SensorType = 'Temperature' OR SensorType = 'Load'"),
                new EnumerationOptions
                {
                    ReturnImmediately = false,
                    Timeout = TimeSpan.FromSeconds(2),
                });
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                string sensorType = Convert.ToString(item["SensorType"], CultureInfo.InvariantCulture) ?? "";
                string name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture) ?? "传感器";
                string identifier = Convert.ToString(item["Identifier"], CultureInfo.InvariantCulture) ?? "";
                if (!TryReadFiniteDouble(item["Value"], out double value)) continue;

                string id = identifier.ToLowerInvariant();
                if (sensorType.Equals("Load", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsGpuIdentifier(id) && value is >= 0 and <= 100)
                        gpuLoads.Add(value);
                    continue;
                }

                if (!IsPlausibleTemperature(value)) continue;
                var sensor = new SensorValue(name, value);
                if (IsCpuIdentifier(id))
                    cpu.Add(sensor);
                else if (IsGpuIdentifier(id))
                    gpu.Add(sensor);
                else if (IsStorageIdentifier(id))
                    disks.Add(sensor);
            }

            SensorValue? preferredCpu = SelectCpuTemperature(cpu);
            SensorValue? preferredGpu = gpu.Count == 0
                ? null
                : gpu.MaxBy(sensor => sensor.Value);
            var diskReadings = disks
                .GroupBy(sensor => sensor.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => (Name: group.Key, Temp: group.Max(sensor => sensor.Value)))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ProviderReading(
                preferredCpu?.Value,
                preferredGpu?.Value,
                diskReadings.Length == 0 ? null : diskReadings.Max(item => item.Temp),
                gpuLoads.Count == 0 ? null : gpuLoads.Max(),
                preferredGpu is null ? null : sourceName.Replace(" WMI", ""),
                preferredCpu?.Name,
                diskReadings,
                preferredCpu is null ? null : sourceName,
                preferredGpu is null ? null : sourceName,
                diskReadings.Length == 0 ? null : sourceName);
        }
        catch
        {
            // 命名空间只在用户已运行对应监控工具并启用 WMI 时存在。
            return null;
        }
    }

    private NvidiaReading? TryReadNvidiaSmi()
    {
        if (_nvidiaSmiPath is null) return null;

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = _nvidiaSmiPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            info.ArgumentList.Add("--query-gpu=name,temperature.gpu,utilization.gpu");
            info.ArgumentList.Add("--format=csv,noheader,nounits");

            using var process = new Process { StartInfo = info };
            if (!process.Start()) return null;
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(3000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            string output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0) return null;

            var readings = new List<NvidiaReading>();
            foreach (string line in output.Split(
                         new[] { "\r\n", "\n" },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] columns = line.Split(',', 3, StringSplitOptions.TrimEntries);
                if (columns.Length != 3
                    || !double.TryParse(columns[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double temperature)
                    || !IsPlausibleTemperature(temperature))
                {
                    continue;
                }

                double? load = double.TryParse(
                    columns[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsedLoad)
                    && parsedLoad is >= 0 and <= 100
                        ? parsedLoad
                        : null;
                readings.Add(new NvidiaReading(columns[0], temperature, load));
            }

            return readings.Count == 0
                ? null
                : readings.MaxBy(reading => reading.Temperature);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveNvidiaSmiPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation",
                "NVSMI",
                "nvidia-smi.exe"),
        };

        // 不搜索当前目录或普通 PATH，避免启动可由普通用户替换的同名程序。
        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static SensorValue? SelectCpuTemperature(IReadOnlyList<SensorValue> values)
    {
        if (values.Count == 0) return null;

        string[] preferredNames =
        {
            "CPU Package",
            "Core Max",
            "Tctl/Tdie",
            "CPU Die",
            "Core Average",
        };
        foreach (string preferredName in preferredNames)
        {
            SensorValue? match = values
                .Where(sensor => sensor.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                .MaxBy(sensor => sensor.Value);
            if (match is not null) return match;
        }
        return values.MaxBy(sensor => sensor.Value);
    }

    private static bool IsCpuIdentifier(string identifier)
        => identifier.StartsWith("/intelcpu/", StringComparison.Ordinal)
           || identifier.StartsWith("/amdcpu/", StringComparison.Ordinal)
           || identifier.Contains("/cpu/", StringComparison.Ordinal);

    private static bool IsGpuIdentifier(string identifier)
        => identifier.StartsWith("/gpu-", StringComparison.Ordinal)
           || identifier.StartsWith("/nvidiagpu/", StringComparison.Ordinal)
           || identifier.StartsWith("/atigpu/", StringComparison.Ordinal)
           || identifier.StartsWith("/intelgpu/", StringComparison.Ordinal)
           || identifier.Contains("/gpu/", StringComparison.Ordinal);

    private static bool IsStorageIdentifier(string identifier)
        => identifier.StartsWith("/hdd/", StringComparison.Ordinal)
           || identifier.StartsWith("/ssd/", StringComparison.Ordinal)
           || identifier.StartsWith("/nvme/", StringComparison.Ordinal)
           || identifier.Contains("/storage/", StringComparison.Ordinal);

    private static bool TryReadFiniteDouble(object? raw, out double value)
    {
        try
        {
            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return double.IsFinite(value);
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static bool IsPlausibleTemperature(double value)
        => double.IsFinite(value) && value is >= -20 and <= 150;

    /// <summary>温度对应的状态色键（面板/任务栏共用）。</summary>
    public static string ColorKeyFor(double celsius) => celsius switch
    {
        >= 85 => "DangerBrush",
        >= 70 => "WarningBrush",
        _ => "SuccessBrush",
    };

    public void Dispose()
    {
        _timer.Dispose();
    }

    private sealed record SensorValue(string Name, double Value);
    private sealed record NvidiaReading(string Name, double Temperature, double? Load);

    private sealed record ProviderReading(
        double? CpuTemp,
        double? GpuTemp,
        double? DiskTemp,
        double? GpuLoad,
        string? GpuName,
        string? CpuName,
        IReadOnlyList<(string Name, double Temp)> Disks,
        string? CpuSource,
        string? GpuSource,
        string? DiskSource)
    {
        public static ProviderReading Empty { get; } =
            new(null, null, null, null, null, null, Array.Empty<(string, double)>(), null, null, null);

        public bool HasAnyReading => CpuTemp is not null || GpuTemp is not null || DiskTemp is not null;
    }
}
