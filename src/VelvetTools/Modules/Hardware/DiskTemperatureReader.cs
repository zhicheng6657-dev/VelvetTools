using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using VelvetTools.Common;

namespace VelvetTools.Modules.Hardware;

/// <summary>
/// 通过 Windows 存储栈自带的只读 IOCTL 查询硬盘温度，代码全部自研（MIT）：
/// 1. NVMe：IOCTL_STORAGE_QUERY_PROPERTY 协议查询 SMART/健康信息日志页，普通权限即可读取；
/// 2. SATA/ATA：SMART_RCV_DRIVE_DATA 读取属性 194/190，Windows 将该 IOCTL 限定为管理员。
/// 只发送标准只读查询命令，不写设备、不改配置，更不安装或加载任何内核驱动。
/// </summary>
internal static class DiskTemperatureReader
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint SmartRcvDriveData = 0x0007C088;

    private const int BusTypeAta = 0x03;
    private const int BusTypeSata = 0x0B;
    private const int BusTypeNvme = 0x11;

    private const int MaxDrives = 16;

    public static IReadOnlyList<(string Name, double Temp)> ReadAll()
    {
        var results = new List<(string Name, double Temp)>();
        bool canUseSmart = Elevation.IsAdmin;
        for (int driveNumber = 0; driveNumber < MaxDrives; driveNumber++)
        {
            try
            {
                (string Name, double Temp)? reading = ReadDrive(driveNumber, canUseSmart);
                if (reading is not null)
                    results.Add(reading.Value);
            }
            catch
            {
                // 单块盘查询失败（外置盒、RAID 卡等）不影响其他盘。
            }
        }

        return results
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, Temp: group.Max(item => item.Temp)))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static (string Name, double Temp)? ReadDrive(int driveNumber, bool canUseSmart)
    {
        string path = @"\\.\PhysicalDrive" + driveNumber;

        // 访问掩码 0：仅做属性/协议查询，普通用户即可打开物理盘句柄。
        using SafeFileHandle probe = CreateFileW(
            path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (probe.IsInvalid) return null;

        (string name, int busType) = QueryDeviceDescriptor(probe);

        if (busType == BusTypeNvme)
        {
            double? temp = QueryNvmeCompositeTemperature(probe);
            if (temp is not null) return (name, temp.Value);
        }

        // SATA 盘（含旧驱动上报的 ATA）走 SMART；该 IOCTL 需要读写句柄，仅管理员可开。
        if (canUseSmart && busType is BusTypeAta or BusTypeSata)
        {
            using SafeFileHandle rw = CreateFileW(
                path, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero,
                OpenExisting, 0, IntPtr.Zero);
            if (!rw.IsInvalid)
            {
                double? temp = QuerySmartTemperature(rw, driveNumber);
                if (temp is not null) return (name, temp.Value);
            }
        }

        return null;
    }

    /// <summary>STORAGE_DEVICE_PROPERTY：取型号与总线类型，用于分流 NVMe/SATA。</summary>
    private static (string Name, int BusType) QueryDeviceDescriptor(SafeFileHandle handle)
    {
        var query = new byte[12]; // PropertyId=StorageDeviceProperty(0)，QueryType=Standard(0)
        var output = new byte[1024];
        if (!DeviceIoControl(handle, IoctlStorageQueryProperty,
                query, query.Length, output, output.Length, out int returned, IntPtr.Zero)
            || returned < 36)
        {
            return ("磁盘", -1);
        }

        // STORAGE_DEVICE_DESCRIPTOR：VendorIdOffset=+12、ProductIdOffset=+16、BusType=+28。
        int busType = BitConverter.ToInt32(output, 28);
        string vendor = ReadAnsiString(output, BitConverter.ToInt32(output, 12), returned);
        string product = ReadAnsiString(output, BitConverter.ToInt32(output, 16), returned);
        string name = (vendor + " " + product).Trim();
        return (name.Length == 0 ? "磁盘" : name, busType);
    }

    /// <summary>NVMe 健康信息日志（Log Page 02h）字节 1-2 为复合温度（开尔文）。</summary>
    private static double? QueryNvmeCompositeTemperature(SafeFileHandle handle)
    {
        const int queryHeader = 8;      // STORAGE_PROPERTY_QUERY 的 PropertyId+QueryType
        const int protocolData = 40;    // STORAGE_PROTOCOL_SPECIFIC_DATA
        const int logPageSize = 512;    // 健康日志页固定 512 字节

        var input = new byte[queryHeader + protocolData + logPageSize];
        BitConverter.GetBytes(50).CopyTo(input, 0);           // StorageDeviceProtocolSpecificProperty
        BitConverter.GetBytes(0).CopyTo(input, 4);            // PropertyStandardQuery
        BitConverter.GetBytes(3).CopyTo(input, 8);            // ProtocolTypeNvme
        BitConverter.GetBytes(2).CopyTo(input, 12);           // NVMeDataTypeLogPage
        BitConverter.GetBytes(2).CopyTo(input, 16);           // NVME_LOG_PAGE_HEALTH_INFO
        BitConverter.GetBytes(0).CopyTo(input, 20);           // SubValue（命名空间 0）
        BitConverter.GetBytes(protocolData).CopyTo(input, 24); // ProtocolDataOffset
        BitConverter.GetBytes(logPageSize).CopyTo(input, 28);  // ProtocolDataLength

        var output = new byte[input.Length];
        if (!DeviceIoControl(handle, IoctlStorageQueryProperty,
                input, input.Length, output, output.Length, out int returned, IntPtr.Zero))
        {
            return null;
        }

        // 返回布局：DATA_DESCRIPTOR 头(8) + STORAGE_PROTOCOL_SPECIFIC_DATA(40) + 日志页。
        int dataOffset = BitConverter.ToInt32(output, 8 + 16);
        int dataLength = BitConverter.ToInt32(output, 8 + 20);
        int logStart = 8 + dataOffset;
        if (dataOffset < protocolData || dataLength < 4 || logStart + 3 > returned)
            return null;

        int kelvin = output[logStart + 1] | (output[logStart + 2] << 8);
        double celsius = kelvin - 273.0;
        return IsPlausibleDiskTemperature(celsius) ? celsius : null;
    }

    /// <summary>ATA SMART READ DATA（B0h/D0h，只读命令），取属性 194，退而求其次 190。</summary>
    private static double? QuerySmartTemperature(SafeFileHandle handle, int driveNumber)
    {
        // SENDCMDINPARAMS（32 字节头）：cBufferSize + IDEREGS + bDriveNumber。
        var input = new byte[32];
        BitConverter.GetBytes(512).CopyTo(input, 0);
        input[4] = 0xD0;               // bFeaturesReg = SMART READ DATA
        input[5] = 1;                  // bSectorCountReg
        input[6] = 1;                  // bSectorNumberReg
        input[7] = 0x4F;               // bCylLowReg（SMART 固定签名）
        input[8] = 0xC2;               // bCylHighReg（SMART 固定签名）
        input[9] = 0xA0;               // bDriveHeadReg
        input[10] = 0xB0;              // bCommandReg = SMART
        input[12] = (byte)driveNumber; // bDriveNumber

        // SENDCMDOUTPARAMS：cBufferSize(4) + DRIVERSTATUS(12) + 512 字节属性表。
        var output = new byte[16 + 512];
        if (!DeviceIoControl(handle, SmartRcvDriveData,
                input, input.Length, output, output.Length, out int returned, IntPtr.Zero))
        {
            return null;
        }
        if (returned < 16 + 362 || output[4] != 0) return null; // bDriverError != 0 视为失败

        double? primary = null;
        double? airflow = null;
        int end = Math.Min(returned, output.Length);
        for (int entry = 16 + 2; entry + 12 <= end; entry += 12)
        {
            byte id = output[entry];
            if (id == 0) continue;
            double raw = output[entry + 5]; // 原始值低字节即摄氏度
            if (!IsPlausibleDiskTemperature(raw)) continue;
            if (id == 194) primary = raw;        // Temperature_Celsius
            else if (id == 190) airflow = raw;   // Airflow_Temperature_Cel
        }
        return primary ?? airflow;
    }

    private static string ReadAnsiString(byte[] buffer, int offset, int limit)
    {
        if (offset <= 0 || offset >= limit) return "";
        int end = offset;
        while (end < limit && buffer[end] != 0) end++;
        return Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
    }

    /// <summary>硬盘正常工作温度范围；0 多为固件未上报，不能当读数。</summary>
    private static bool IsPlausibleDiskTemperature(double value)
        => double.IsFinite(value) && value is >= 1 and <= 120;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode,
        byte[] inBuffer, int inBufferSize,
        byte[] outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);
}
