using System.Management;
using System.Runtime.InteropServices;
using VelvetTools.Common;

namespace VelvetTools.Modules.Brightness;

/// <summary>一个可调亮度的显示器（外接 DDC/CI 或笔记本内屏 WMI）。</summary>
public sealed class MonitorItem : ObservableObject
{
    private int _percent;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsInternal { get; init; }
    internal IntPtr PhysicalHandle { get; init; }
    internal uint DdcMin { get; init; }
    internal uint DdcMax { get; init; }
    internal string? WmiInstance { get; init; }

    /// <summary>0-100。UI 绑定用；设置时会异步下发到硬件（防抖）。</summary>
    public int Percent
    {
        get => _percent;
        set
        {
            if (Set(ref _percent, Math.Clamp(value, 0, 100)))
                Owner?.OnUserChanged(this);
        }
    }

    internal void SetPercentSilently(int value) => Set(ref _percent, Math.Clamp(value, 0, 100), nameof(Percent));

    internal BrightnessService? Owner { get; set; }
}

/// <summary>
/// 显示器亮度控制。思路与 Twinkle Tray 一致（该项目为 MIT），实现为自研 C#：
/// 外接显示器走 DDC/CI（dxva2.dll），笔记本内屏走 WMI。
/// </summary>
public sealed class BrightnessService : IDisposable
{
    // ---------- dxva2 DDC/CI ----------
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint value);

    private readonly object _lock = new();
    private readonly Dictionary<string, System.Timers.Timer> _debounce = new();
    private List<MonitorItem> _monitors = new();

    /// <summary>多显示器同步调节。</summary>
    public bool LinkMonitors { get; set; }

    public IReadOnlyList<MonitorItem> Monitors
    {
        get { lock (_lock) return _monitors; }
    }

    public event Action? MonitorsChanged;

    /// <summary>重新枚举显示器（异步执行，DDC/CI 较慢）。</summary>
    public void RefreshAsync() => Task.Run(Refresh);

    public void Refresh()
    {
        var found = new List<MonitorItem>();

        // 1) 外接显示器 DDC/CI
        try
        {
            var handles = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
            {
                handles.Add(hMon);
                return true;
            }, IntPtr.Zero);

            int index = 0;
            foreach (var hMon in handles)
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) || count == 0) continue;
                var phys = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, phys)) continue;

                foreach (var pm in phys)
                {
                    index++;
                    if (GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out uint cur, out uint max) && max > min)
                    {
                        string desc = string.IsNullOrWhiteSpace(pm.szPhysicalMonitorDescription)
                            ? $"显示器 {index}" : pm.szPhysicalMonitorDescription.Trim();
                        var item = new MonitorItem
                        {
                            Id = $"ddc:{index}",
                            Name = $"{desc}",
                            IsInternal = false,
                            PhysicalHandle = pm.hPhysicalMonitor,
                            DdcMin = min,
                            DdcMax = max,
                            Owner = null,
                        };
                        item.SetPercentSilently((int)Math.Round((cur - min) * 100.0 / (max - min)));
                        item.Owner = this;
                        found.Add(item);
                    }
                    else
                    {
                        DestroyPhysicalMonitor(pm.hPhysicalMonitor);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DDC/CI 枚举失败", ex);
        }

        // 2) 笔记本内屏 WMI
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active=TRUE");
            foreach (ManagementObject mo in searcher.Get())
            {
                string instance = (string)mo["InstanceName"];
                byte current = (byte)mo["CurrentBrightness"];
                var item = new MonitorItem
                {
                    Id = $"wmi:{instance}",
                    Name = "内置显示器",
                    IsInternal = true,
                    WmiInstance = instance,
                    Owner = null,
                };
                item.SetPercentSilently(current);
                item.Owner = this;
                found.Add(item);
            }
        }
        catch
        {
            // 台式机没有 WMI 亮度接口，正常
        }

        lock (_lock)
        {
            foreach (var old in _monitors.Where(m => !m.IsInternal && m.PhysicalHandle != IntPtr.Zero))
                DestroyPhysicalMonitor(old.PhysicalHandle);
            _monitors = found;
        }
        MonitorsChanged?.Invoke();
        Logger.Info($"检测到 {found.Count} 台可调亮度显示器");
    }

    internal void OnUserChanged(MonitorItem source)
    {
        if (LinkMonitors)
        {
            foreach (var m in Monitors)
            {
                if (!ReferenceEquals(m, source) && m.Percent != source.Percent)
                    m.SetPercentSilently(source.Percent);
                ScheduleApply(m);
            }
        }
        else
        {
            ScheduleApply(source);
        }
    }

    /// <summary>防抖 120ms 后真正下发（DDC/CI 写入较慢，拖动滑块时避免刷爆）。</summary>
    private void ScheduleApply(MonitorItem item)
    {
        lock (_debounce)
        {
            if (!_debounce.TryGetValue(item.Id, out var timer))
            {
                timer = new System.Timers.Timer(120) { AutoReset = false };
                timer.Elapsed += (_, _) => Apply(item);
                _debounce[item.Id] = timer;
            }
            timer.Stop();
            timer.Start();
        }
    }

    private void Apply(MonitorItem item)
    {
        try
        {
            if (item.IsInternal && item.WmiInstance is not null)
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi",
                    $"SELECT * FROM WmiMonitorBrightnessMethods WHERE InstanceName='{item.WmiInstance.Replace(@"\", @"\\")}'");
                foreach (ManagementObject mo in searcher.Get())
                    mo.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)item.Percent });
            }
            else if (item.PhysicalHandle != IntPtr.Zero)
            {
                uint raw = item.DdcMin + (uint)Math.Round(item.Percent / 100.0 * (item.DdcMax - item.DdcMin));
                SetMonitorBrightness(item.PhysicalHandle, raw);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"设置亮度失败 {item.Name}", ex);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var m in _monitors.Where(m => !m.IsInternal && m.PhysicalHandle != IntPtr.Zero))
                DestroyPhysicalMonitor(m.PhysicalHandle);
            _monitors.Clear();
        }
    }
}
