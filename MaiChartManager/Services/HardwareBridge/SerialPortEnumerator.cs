using System.Globalization;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MaiChartManager.Services.HardwareBridge;

public static class SerialPortEnumerator
{
    private static readonly Guid GuidDevInterfaceComPort = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint DICS_FLAG_GLOBAL = 0x00000001;
    private const uint DIREG_DEV = 0x00000001;
    private const uint KEY_READ = 0x00020019;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private const uint SPDRP_LOCATION_PATHS = 0x00000023;
    private const uint DEVPROP_TYPE_STRING = 0x00000012;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int ERROR_MORE_DATA = 234;

    private static readonly Regex VidPidRegex = new(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.Compiled);
    private static readonly Regex UsbPortRegex = new(@"#USB\((\d+)\)", RegexOptions.Compiled);
    private static readonly Regex ComNumRegex = new(@"^COM(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // DEVPKEY_Device_BusReportedDeviceDesc: USB 描述符上报的接口/设备名称
    private static readonly DEVPROPKEY DevpkeyBusReportedDeviceDesc = new()
    {
        fmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"),
        pid = 4,
    };

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, int memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType,
        byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey, out uint propertyType,
        byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint scope, uint hwProfile, uint keyType, uint samDesired);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern int RegQueryValueExW(
        IntPtr hKey, string valueName, IntPtr reserved, out uint type, byte[] data, ref uint dataSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    public sealed record RawPortInfo(
        string PortName,
        string FriendlyName,
        int? VendorId,
        int? ProductId,
        string? Location,
        string? BusReportedDescription = null);

    public static List<RawPortInfo> EnumeratePorts(ILogger logger)
    {
        var byPort = new Dictionary<string, RawPortInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var guid = GuidDevInterfaceComPort;
            var set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set != IntPtr.Zero && set != new IntPtr(-1))
            {
                try
                {
                    var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    for (var i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
                    {
                        var portName = ReadPortName(set, ref info);
                        if (string.IsNullOrWhiteSpace(portName)) continue;

                        var busDesc = ReadDevicePropertyString(set, ref info, DevpkeyBusReportedDeviceDesc);
                        var regFriendly = ReadStringProperty(set, ref info, SPDRP_FRIENDLYNAME);
                        var friendlyName = !string.IsNullOrEmpty(busDesc) ? busDesc : (regFriendly ?? portName);
                        var hardwareIds = ReadStringArrayProperty(set, ref info, SPDRP_HARDWAREID);
                        var (vid, pid) = ParseVidPid(hardwareIds);
                        var location = ExtractPortChain(ReadStringArrayProperty(set, ref info, SPDRP_LOCATION_PATHS));

                        byPort[portName] = new RawPortInfo(portName, friendlyName, vid, pid, location, busDesc);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(set);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SetupAPI 枚举 COM 口失败，回退至 SerialPort.GetPortNames");
        }

        try
        {
            foreach (var name in SerialPort.GetPortNames())
            {
                if (!byPort.ContainsKey(name))
                {
                    byPort[name] = new RawPortInfo(name, name, null, null, null);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SerialPort.GetPortNames 失败");
        }

        return byPort.Values
            .OrderBy(p =>
            {
                var m = ComNumRegex.Match(p.PortName);
                return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : int.MaxValue;
            })
            .ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<SerialProbeResult> ProbePortAsync(
        string portName,
        CancellationToken ct,
        int? touchBaudRate = null)
    {
        int[] touchBauds = touchBaudRate is > 0 and not 9600
            ? [touchBaudRate.Value, 9600]
            : [9600];

        foreach (var baud in touchBauds)
        {
            try
            {
                if (await ProbeTouchAsync(portName, baud, ct))
                {
                    return new SerialProbeResult("touch", $"检测到 {baud} 波特率触摸屏数据帧");
                }
            }
            catch (UnauthorizedAccessException)
            {
                return new SerialProbeResult("unknown", "端口被占用");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                return new SerialProbeResult("unknown", ex.Message);
            }

            await Task.Delay(50, ct);
        }

        try
        {
            if (await ProbeLedAsync(portName, ct))
            {
                return new SerialProbeResult("led", "检测到 115200 波特率 SEGA 灯板应答");
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new SerialProbeResult("unknown", "端口被占用");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new SerialProbeResult("unknown", ex.Message);
        }

        return new SerialProbeResult("unknown", "未收到触摸或灯板协议应答");
    }

    private static async Task<bool> ProbeTouchAsync(string portName, int baudRate, CancellationToken ct)
    {
        using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 100,
            WriteTimeout = 500,
        };
        port.Open();

        try
        {
            port.DiscardInBuffer();
            // ADX 等 MCU 串口需要分包且间隔发送 {HALT}/{RSET}/{STAT}，连发会被忽略
            byte[][] initCmds =
            [
                "{HALT}"u8.ToArray(),
                "{RSET}"u8.ToArray(),
                "{STAT}"u8.ToArray(),
            ];
            for (var i = 0; i < initCmds.Length; i++)
            {
                port.Write(initCmds[i], 0, initCmds[i].Length);
                if (i < initCmds.Length - 1)
                    await Task.Delay(30, ct);
            }

            var buf = new byte[128];
            var frameLen = -1;
            var deadline = Environment.TickCount64 + 450;

            while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = port.Read(buf, 0, buf.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                for (var i = 0; i < read; i++)
                {
                    var b = buf[i];
                    if (b == (byte)'(')
                    {
                        frameLen = 0;
                    }
                    else if (b == (byte)')')
                    {
                        if (frameLen == 7)
                            return true;
                        frameLen = -1;
                    }
                    else if (frameLen >= 0)
                    {
                        frameLen++;
                        if (frameLen > 16) frameLen = -1;
                    }
                }
            }

            return false;
        }
        finally
        {
            try
            {
                var halt = "{HALT}"u8.ToArray();
                port.Write(halt, 0, halt.Length);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static Task<bool> ProbeLedAsync(string portName, CancellationToken ct)
    {
        using var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 80,
            WriteTimeout = 500,
        };
        port.Open();
        port.DiscardInBuffer();

        // SEGA BD15070_4 GetBoardInfo (0xF0), GetProtocolVersion (0xF3), Update (0x3C)
        byte[][] probes =
        [
            [0xE0, 0x11, 0x01, 0x01, 0xF0, 0x03],
            [0xE0, 0x11, 0x01, 0x01, 0xF3, 0x06],
            [0xE0, 0x11, 0x01, 0x01, 0x3C, 0x4F],
        ];

        var recvBuf = new List<byte>(64);
        var escape = false;
        var synced = false;
        var readBuf = new byte[128];

        foreach (var pkt in probes)
        {
            ct.ThrowIfCancellationRequested();
            port.Write(pkt, 0, pkt.Length);

            var deadline = Environment.TickCount64 + 220;
            while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = port.Read(readBuf, 0, readBuf.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                for (var i = 0; i < read; i++)
                {
                    var b = readBuf[i];
                    if (b == 0xE0)
                    {
                        recvBuf.Clear();
                        escape = false;
                        synced = true;
                        continue;
                    }
                    if (!synced) continue;
                    if (b == 0xD0)
                    {
                        escape = true;
                        continue;
                    }
                    if (escape)
                    {
                        b++;
                        escape = false;
                    }
                    recvBuf.Add(b);
                    if (recvBuf.Count >= 4)
                    {
                        var expectedLen = recvBuf[2] + 3;
                        if (recvBuf.Count == expectedLen + 1)
                        {
                            var sum = 0;
                            for (var j = 0; j < expectedLen; j++)
                                sum = (sum + recvBuf[j]) & 0xFF;
                            if (sum == recvBuf[expectedLen])
                                return Task.FromResult(true);

                            recvBuf.Clear();
                            synced = false;
                        }
                        else if (recvBuf.Count > 128)
                        {
                            recvBuf.Clear();
                            synced = false;
                        }
                    }
                }
            }
        }

        return Task.FromResult(false);
    }

    private static string? ReadPortName(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var key = SetupDiOpenDevRegKey(set, ref info, DICS_FLAG_GLOBAL, 0, DIREG_DEV, KEY_READ);
        if (key == IntPtr.Zero || key == new IntPtr(-1)) return null;
        try
        {
            var buf = new byte[256];
            var size = (uint)buf.Length;
            var rc = RegQueryValueExW(key, "PortName", IntPtr.Zero, out _, buf, ref size);
            if (rc == ERROR_MORE_DATA)
            {
                buf = new byte[size];
                rc = RegQueryValueExW(key, "PortName", IntPtr.Zero, out _, buf, ref size);
            }
            if (rc != ERROR_SUCCESS || size == 0) return null;
            return Encoding.Unicode.GetString(buf, 0, (int)size).TrimEnd('\0');
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static (int? vid, int? pid) ParseVidPid(string[]? hardwareIds)
    {
        if (hardwareIds == null) return (null, null);
        foreach (var id in hardwareIds)
        {
            var m = VidPidRegex.Match(id);
            if (m.Success &&
                int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, null, out var vid) &&
                int.TryParse(m.Groups[2].Value, NumberStyles.HexNumber, null, out var pid))
            {
                return (vid, pid);
            }
        }
        return (null, null);
    }

    private static string? ExtractPortChain(string[]? locationPaths)
    {
        if (locationPaths == null || locationPaths.Length == 0) return null;
        var matches = UsbPortRegex.Matches(locationPaths[0]);
        if (matches.Count == 0) return null;
        var ports = new string[matches.Count];
        for (var i = 0; i < matches.Count; i++)
            ports[i] = matches[i].Groups[1].Value;
        return string.Join(".", ports);
    }

    private static string? ReadStringProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var buffer = new byte[1024];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer, buffer.Length, out var requiredSize))
            {
                return Encoding.Unicode.GetString(buffer, 0, requiredSize).TrimEnd('\0');
            }
            if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER || requiredSize <= buffer.Length) return null;
            buffer = new byte[requiredSize];
        }
        return null;
    }

    private static string? ReadDevicePropertyString(IntPtr set, ref SP_DEVINFO_DATA info, DEVPROPKEY propertyKey)
    {
        var buffer = new byte[1024];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (SetupDiGetDevicePropertyW(set, ref info, ref propertyKey, out var propertyType, buffer, buffer.Length, out var requiredSize, 0))
            {
                if (propertyType != DEVPROP_TYPE_STRING || requiredSize <= 0) return null;
                var text = Encoding.Unicode.GetString(buffer, 0, requiredSize).TrimEnd('\0').Trim();
                return string.IsNullOrEmpty(text) ? null : text;
            }
            if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER || requiredSize <= buffer.Length) return null;
            buffer = new byte[requiredSize];
        }
        return null;
    }

    private static string[]? ReadStringArrayProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var value = ReadStringProperty(set, ref info, property);
        return value?.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
