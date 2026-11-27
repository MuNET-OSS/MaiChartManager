using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace MaiChartManager.Services.HardwareBridge;

public static class WinUsbBridge
{
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int ERROR_MORE_DATA = 234;
    private const int CR_SUCCESS = 0;
    private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0x00000000;
    private const int ERROR_IO_PENDING = 997;
    private const uint DIGCF_ALLCLASSES = 0x04;
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DICS_FLAG_GLOBAL = 0x00000001;
    private const uint DIREG_DEV = 0x00000001;
    private const uint KEY_READ = 0x00020019;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_SERVICE = 0x00000004;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private const uint SPDRP_LOCATION_PATHS = 0x00000023;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint WINUSB_AUTO_SUSPEND = 0x81;

    private static readonly Guid NproGameInterfaceGuid = new("7F2A9C41-5E3B-4D8A-9F16-3C7E5B2D9A48");
    private static readonly Regex UsbPortRegex = new(@"#USB\((\d+)\)", RegexOptions.Compiled);

    private readonly record struct KnownWinUsbTarget(
        ushort Vid,
        ushort Pid,
        byte? InterfaceNumber,
        string DeviceKind,
        string? PlayerSide,
        string? ExpectedName,
        Guid? FallbackGuid,
        string DisplayName);

    private static readonly KnownWinUsbTarget[] KnownTargets =
    [
        new(0x2E3C, 0x5751, 0, "npro", "1P", "NoronDX GAME", NproGameInterfaceGuid, "NPro GAME (1P)"),
        new(0x2E3C, 0x5752, 0, "npro", "2P", "NoronDX GAME", NproGameInterfaceGuid, "NPro GAME (2P)"),
        new(0x3356, 0x3003, null, "pdx", null, null, null, "PDX Touch"),
    ];

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WINUSB_SETUP_PACKET
    {
        public byte RequestType;
        public byte Request;
        public ushort Value;
        public ushort Index;
        public ushort Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, int memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_List_SizeW(
        out int length, ref Guid interfaceClassGuid, string deviceInstanceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid, string deviceInstanceId, [Out] char[] buffer, int bufferLength, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType,
        byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupDiGetCustomDevicePropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, string propertyName, uint flags,
        out uint propertyRegDataType, byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle file, IntPtr overlapped, out uint bytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryInterfaceSettings(
        IntPtr interfaceHandle, byte alternateInterfaceNumber, out USB_INTERFACE_DESCRIPTOR descriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryPipe(
        IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WINUSB_PIPE_INFORMATION pipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(
        IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_AbortPipe(IntPtr interfaceHandle, byte pipeId);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ControlTransfer(
        IntPtr interfaceHandle, WINUSB_SETUP_PACKET setupPacket, IntPtr buffer, uint bufferLength,
        out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPowerPolicy(
        IntPtr interfaceHandle, uint policyType, uint valueLength, ref byte value);

    #endregion

    public static List<UsbDeviceCandidate> EnumerateKnownDevices(AquaMaiHints? hints, ILogger logger)
    {
        var result = new List<UsbDeviceCandidate>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var set = SetupDiGetClassDevsW(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            logger.LogWarning("WinUsbBridge SetupDiGetClassDevs failed: {Err}", Marshal.GetLastWin32Error());
            return result;
        }

        try
        {
            var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (var index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                var hardwareIds = ReadStringArrayProperty(set, ref info, SPDRP_HARDWAREID);
                if (hardwareIds == null || hardwareIds.Length == 0) continue;

                var service = ReadStringProperty(set, ref info, SPDRP_SERVICE);
                if (!string.Equals(service, "WINUSB", StringComparison.OrdinalIgnoreCase)) continue;

                var friendlyName = ReadStringProperty(set, ref info, SPDRP_FRIENDLYNAME);

                foreach (var target in KnownTargets)
                {
                    if (!TryGetInterfaceNumber(hardwareIds, target.Vid, target.Pid, out var ifaceNum)) continue;
                    if (target.InterfaceNumber.HasValue && target.InterfaceNumber.Value != ifaceNum) continue;
                    if (!string.IsNullOrEmpty(target.ExpectedName) &&
                        friendlyName?.StartsWith(target.ExpectedName, StringComparison.OrdinalIgnoreCase) != true)
                        continue;

                    // PDX new is interface 1 (EP 0x82) and old AT32 is interface 0 (EP 0x81)
                    if (target.DeviceKind == "pdx" && ifaceNum > 1) continue;

                    var instanceId = ReadDeviceInstanceId(set, ref info);
                    if (string.IsNullOrEmpty(instanceId)) continue;

                    var guids = ReadDeviceInterfaceGuids(set, ref info);
                    if (guids.Count == 0 && target.FallbackGuid.HasValue)
                    {
                        guids.Add(target.FallbackGuid.Value);
                    }
                    if (guids.Count == 0) continue;

                    var locationPaths = ReadStringArrayProperty(set, ref info, SPDRP_LOCATION_PATHS);
                    var location = ExtractPortChain(locationPaths);
                    var serialNumber = ExtractSerialFromInstanceId(instanceId);

                    foreach (var guid in guids)
                    {
                        foreach (var path in EnumerateInterfaces(guid, instanceId, target.Vid, target.Pid))
                        {
                            if (!seenPaths.Add(path)) continue;

                            string? playerSide = target.PlayerSide;
                            string? reason = target.DeviceKind == "npro"
                                ? "检测到 NPro WinUSB 设备"
                                : "检测到 PDX 独占触摸 (WinUSB)";

                            if (target.DeviceKind == "pdx" && hints?.PdxTouchEnabled == true)
                            {
                                if (MatchesPdxIdentifier(hints.PdxPath1p, serialNumber, location))
                                {
                                    playerSide = "1P";
                                    reason = "AquaMai PDX 1P 配置匹配";
                                }
                                else if (MatchesPdxIdentifier(hints.PdxPath2p, serialNumber, location))
                                {
                                    playerSide = "2P";
                                    reason = "AquaMai PDX 2P 配置匹配";
                                }
                            }

                            result.Add(new UsbDeviceCandidate(
                                Id: path,
                                VendorId: target.Vid,
                                ProductId: target.Pid,
                                ProductName: string.IsNullOrWhiteSpace(friendlyName) ? target.DisplayName : friendlyName,
                                InterfaceNumber: ifaceNum,
                                DeviceKind: target.DeviceKind,
                                PlayerSide: playerSide,
                                Location: location,
                                SerialNumber: serialNumber,
                                RecommendationReason: reason));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "枚举 WinUSB 设备失败");
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static bool MatchesPdxIdentifier(string? configured, string? serial, string? location)
    {
        if (string.IsNullOrWhiteSpace(configured)) return false;
        var target = configured.Trim();
        return string.Equals(serial, target, StringComparison.OrdinalIgnoreCase)
               || string.Equals(location, target, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> EnumerateInterfaces(Guid interfaceGuid, string instanceId, ushort expectedVid, ushort expectedPid)
    {
        var result = new List<string>();
        var guid = interfaceGuid;
        if (CM_Get_Device_Interface_List_SizeW(out var length, ref guid, instanceId, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || length <= 0)
            return result;

        var buffer = new char[length];
        if (CM_Get_Device_Interface_ListW(ref guid, instanceId, buffer, buffer.Length, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
            return result;

        for (var start = 0; start < buffer.Length;)
        {
            var end = Array.IndexOf(buffer, '\0', start);
            if (end < 0 || end == start) break;

            var path = new string(buffer, start, end - start);
            if (TryParseVidPid(path, out var vid, out var pid) && vid == expectedVid && pid == expectedPid)
                result.Add(path);
            start = end + 1;
        }

        return result;
    }

    private static string? ReadDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var sb = new StringBuilder(512);
        return SetupDiGetDeviceInstanceIdW(set, ref info, sb, sb.Capacity, out _) ? sb.ToString() : null;
    }

    private static string? ExtractSerialFromInstanceId(string instanceId)
    {
        var parts = instanceId.Split('\\');
        if (parts.Length < 2) return null;
        var candidate = parts[^1];
        return !string.IsNullOrWhiteSpace(candidate) && !candidate.Contains('&') ? candidate : null;
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

    private static bool TryGetInterfaceNumber(string[] hardwareIds, ushort vid, ushort pid, out byte interfaceNumber)
    {
        interfaceNumber = 0;
        var prefix = $"USB\\VID_{vid:X4}&PID_{pid:X4}";
        var matched = false;
        foreach (var hardwareId in hardwareIds)
        {
            if (!hardwareId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            matched = true;

            const string interfacePart = "&MI_";
            var idx = hardwareId.IndexOf(interfacePart, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 &&
                idx + interfacePart.Length + 2 <= hardwareId.Length &&
                byte.TryParse(hardwareId.AsSpan(idx + interfacePart.Length, 2), NumberStyles.HexNumber, null, out var parsed))
            {
                interfaceNumber = parsed;
                return true;
            }
        }
        return matched;
    }

    private static List<Guid> ReadDeviceInterfaceGuids(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        foreach (var propertyName in new[] { "DeviceInterfaceGUIDs", "DeviceInterfaceGUID" })
        {
            var bytes = ReadCustomDeviceProperty(set, ref info, propertyName) ?? ReadDeviceRegistryValue(set, ref info, propertyName);
            if (bytes == null) continue;

            var guids = ParseGuids(Encoding.Unicode.GetString(bytes));
            if (guids.Count > 0) return guids;
        }
        return [];
    }

    private static List<Guid> ParseGuids(string value)
    {
        var result = new List<Guid>();
        for (var start = value.IndexOf('{'); start >= 0; start = value.IndexOf('{', start + 1))
        {
            var end = value.IndexOf('}', start + 1);
            if (end < 0) break;
            if (Guid.TryParse(value.AsSpan(start, end - start + 1), out var guid))
                result.Add(guid);
            start = end;
        }
        if (result.Count > 0) return result;

        foreach (var part in value.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(part.Trim(), out var guid))
                result.Add(guid);
        }
        return result;
    }

    private static string? ReadStringProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var bytes = ReadDeviceProperty(set, ref info, property);
        return bytes == null ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    private static string[]? ReadStringArrayProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var value = ReadStringProperty(set, ref info, property);
        return value?.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static byte[]? ReadDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var buffer = new byte[1024];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer, buffer.Length, out var requiredSize))
            {
                var result = new byte[requiredSize];
                Array.Copy(buffer, result, requiredSize);
                return result;
            }
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_INSUFFICIENT_BUFFER || requiredSize <= buffer.Length) return null;
            buffer = new byte[requiredSize];
        }
        return null;
    }

    private static byte[]? ReadCustomDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA info, string propertyName)
    {
        var buffer = new byte[1024];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (SetupDiGetCustomDevicePropertyW(set, ref info, propertyName, 0, out _, buffer, buffer.Length, out var requiredSize))
            {
                var result = new byte[requiredSize];
                Array.Copy(buffer, result, requiredSize);
                return result;
            }
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_INSUFFICIENT_BUFFER || requiredSize <= buffer.Length) return null;
            buffer = new byte[requiredSize];
        }
        return null;
    }

    private static byte[]? ReadDeviceRegistryValue(IntPtr set, ref SP_DEVINFO_DATA info, string valueName)
    {
        var key = SetupDiOpenDevRegKey(set, ref info, DICS_FLAG_GLOBAL, 0, DIREG_DEV, KEY_READ);
        if (key == IntPtr.Zero || key == new IntPtr(-1)) return null;

        try
        {
            var buffer = new byte[1024];
            var size = (uint)buffer.Length;
            var result = RegQueryValueExW(key, valueName, IntPtr.Zero, out _, buffer, ref size);
            if (result == ERROR_MORE_DATA)
            {
                buffer = new byte[size];
                result = RegQueryValueExW(key, valueName, IntPtr.Zero, out _, buffer, ref size);
            }
            if (result != ERROR_SUCCESS) return null;
            if (size < buffer.Length) Array.Resize(ref buffer, (int)size);
            return buffer;
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static bool TryParseVidPid(string path, out ushort vid, out ushort pid)
    {
        vid = 0;
        pid = 0;
        var lower = path.ToLowerInvariant();
        var vi = lower.IndexOf("vid_", StringComparison.Ordinal);
        var pi = lower.IndexOf("pid_", StringComparison.Ordinal);
        if (vi < 0 || pi < 0 || vi + 8 > lower.Length || pi + 8 > lower.Length) return false;
        return ushort.TryParse(lower.AsSpan(vi + 4, 4), NumberStyles.HexNumber, null, out vid)
               && ushort.TryParse(lower.AsSpan(pi + 4, 4), NumberStyles.HexNumber, null, out pid);
    }

    public sealed class Device : IDisposable
    {
        private readonly SafeFileHandle _file;
        private readonly IntPtr _winUsb;
        private readonly Lock _readLock = new();
        private readonly Lock _writeLock = new();
        private readonly Lock _controlLock = new();
        private int _disposeState;

        public byte InPipeId { get; }
        public byte OutPipeId { get; }
        public ushort InPacketSize { get; }

        public bool IsOpen => Volatile.Read(ref _disposeState) == 0 && !_file.IsInvalid && !_file.IsClosed;

        private Device(SafeFileHandle file, IntPtr winUsb, byte inPipeId, byte outPipeId, ushort inPacketSize)
        {
            _file = file;
            _winUsb = winUsb;
            InPipeId = inPipeId;
            OutPipeId = outPipeId;
            InPacketSize = inPacketSize;
        }

        public static Device Open(string devicePath, byte inPipeId, byte? outPipeId)
        {
            var file = CreateFileW(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (file.IsInvalid)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateFile 失败 ({err})，设备可能正被占用: {devicePath}");
            }

            if (!WinUsb_Initialize(file, out var winUsb))
            {
                var err = Marshal.GetLastWin32Error();
                file.Dispose();
                throw new InvalidOperationException($"WinUsb_Initialize 失败 ({err}): {devicePath}");
            }

            try
            {
                if (!WinUsb_QueryInterfaceSettings(winUsb, 0, out var desc))
                    throw new InvalidOperationException($"WinUsb_QueryInterfaceSettings 失败 ({Marshal.GetLastWin32Error()})");

                byte resolvedIn = 0;
                byte resolvedOut = 0;
                ushort inPacket = 64;

                for (byte i = 0; i < desc.bNumEndpoints; i++)
                {
                    if (!WinUsb_QueryPipe(winUsb, 0, i, out var pipe))
                        throw new InvalidOperationException($"WinUsb_QueryPipe 失败 ({Marshal.GetLastWin32Error()})");

                    if (pipe.PipeType is not (2 or 3)) continue;

                    if ((pipe.PipeId & 0x80) != 0 && pipe.PipeId == inPipeId)
                    {
                        resolvedIn = pipe.PipeId;
                        inPacket = pipe.MaximumPacketSize > 0 ? pipe.MaximumPacketSize : (ushort)64;
                    }
                    else if (outPipeId.HasValue && (pipe.PipeId & 0x80) == 0 && pipe.PipeId == outPipeId.Value)
                    {
                        resolvedOut = pipe.PipeId;
                    }
                }

                if (resolvedIn == 0)
                    throw new InvalidOperationException($"WinUSB 接口上未找到读端点 0x{inPipeId:X2}");
                if (outPipeId.HasValue && resolvedOut == 0)
                    throw new InvalidOperationException($"WinUSB 接口上未找到写端点 0x{outPipeId.Value:X2}");

                var dev = new Device(file, winUsb, resolvedIn, resolvedOut, inPacket);
                dev.SetAutoSuspend(false);
                return dev;
            }
            catch
            {
                WinUsb_Free(winUsb);
                file.Dispose();
                throw;
            }
        }

        private void SetAutoSuspend(bool enabled)
        {
            if (!IsOpen) return;
            byte value = enabled ? (byte)1 : (byte)0;
            lock (_controlLock)
            {
                if (IsOpen)
                    WinUsb_SetPowerPolicy(_winUsb, WINUSB_AUTO_SUSPEND, 1, ref value);
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (!IsOpen) return -1;

            lock (_readLock)
            {
                if (!IsOpen) return -1;

                using var evt = new ManualResetEvent(false);
                var overlapped = CreateOverlapped(evt.SafeWaitHandle.DangerousGetHandle());
                var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    var ptr = IntPtr.Add(pinned.AddrOfPinnedObject(), offset);
                    var ok = WinUsb_ReadPipe(_winUsb, InPipeId, ptr, (uint)count, out var transferred, overlapped);
                    if (!ok && Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                        return -1;

                    if (ok) return (int)transferred;

                    if (!evt.WaitOne(timeoutMs))
                    {
                        CancelIoEx(_file, overlapped);
                        if (GetOverlappedResult(_file, overlapped, out transferred, true))
                            return (int)transferred;
                        return 0;
                    }

                    if (!GetOverlappedResult(_file, overlapped, out transferred, false))
                        return -1;

                    return (int)transferred;
                }
                finally
                {
                    pinned.Free();
                    Marshal.FreeHGlobal(overlapped);
                }
            }
        }

        public int Write(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (!IsOpen || OutPipeId == 0) return -1;

            lock (_writeLock)
            {
                if (!IsOpen) return -1;

                using var evt = new ManualResetEvent(false);
                var overlapped = CreateOverlapped(evt.SafeWaitHandle.DangerousGetHandle());
                var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    var ptr = IntPtr.Add(pinned.AddrOfPinnedObject(), offset);
                    var ok = WinUsb_WritePipe(_winUsb, OutPipeId, ptr, (uint)count, out var transferred, overlapped);
                    if (!ok && Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                        return -1;

                    if (ok) return (int)transferred;

                    if (!evt.WaitOne(timeoutMs))
                    {
                        CancelIoEx(_file, overlapped);
                        GetOverlappedResult(_file, overlapped, out _, true);
                        return -1;
                    }

                    if (!GetOverlappedResult(_file, overlapped, out transferred, false))
                        return -1;

                    return (int)transferred;
                }
                finally
                {
                    pinned.Free();
                    Marshal.FreeHGlobal(overlapped);
                }
            }
        }

        private static IntPtr CreateOverlapped(IntPtr eventHandle)
        {
            var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<OVERLAPPED>());
            Marshal.StructureToPtr(new OVERLAPPED { hEvent = eventHandle }, overlapped, false);
            return overlapped;
        }

        public void Abort()
        {
            if (Volatile.Read(ref _disposeState) != 0) return;
            try
            {
                if (InPipeId != 0) WinUsb_AbortPipe(_winUsb, InPipeId);
                if (OutPipeId != 0) WinUsb_AbortPipe(_winUsb, OutPipeId);
            }
            catch
            {
                // ignore
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

            if (InPipeId != 0) WinUsb_AbortPipe(_winUsb, InPipeId);
            if (OutPipeId != 0) WinUsb_AbortPipe(_winUsb, OutPipeId);
            CancelIoEx(_file, IntPtr.Zero);

            lock (_readLock)
            lock (_writeLock)
            lock (_controlLock)
            {
                if (_winUsb != IntPtr.Zero) WinUsb_Free(_winUsb);
                _file.Dispose();
            }
        }
    }
}
