using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Mod;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class PdxController(ILogger<PdxController> logger) : ControllerBase
{
    private static string PdxDriverPath => Path.Combine(StaticSettings.exeDir, "pdx-driver.exe");

    public record PdxDriverStatusDto(bool IsUsingWinusb, int DeviceCount, bool Available, string[] DevicePaths);

    [HttpGet]
    public string[] GetPdxDevicePaths()
    {
        try
        {
            return PdxDeviceHelper.GetDevicePaths();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get pdx device paths");
            return [];
        }
    }

    [HttpGet]
    public PdxDriverStatusDto GetPdxDriverStatus()
    {
        try
        {
            var paths = GetPdxDevicePaths();
            var isWinUsb = PdxDeviceHelper.IsUsingWinUsb();
            return new PdxDriverStatusDto(
                IsUsingWinusb: isWinUsb,
                DeviceCount: paths.Length,
                Available: System.IO.File.Exists(PdxDriverPath),
                DevicePaths: paths
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get pdx driver status");
            return new PdxDriverStatusDto(IsUsingWinusb: false, DeviceCount: 0, Available: false, DevicePaths: []);
        }
    }

    [HttpPost]
    public IActionResult SwitchPdxDriver([FromQuery] string direction)
    {
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = PdxDriverPath,
                Arguments = direction,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (process == null) return StatusCode(500, "无法启动 pdx-driver.exe");

            var exited = process.WaitForExit(300000);
            if (!exited)
            {
                process.Kill();
                return StatusCode(500, "驱动切换超时");
            }

            if (process.ExitCode != 0)
                return StatusCode(500, $"驱动切换失败，退出码: {process.ExitCode}");

            return Ok();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return BadRequest("用户取消了权限请求");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// 使用 Windows SetupAPI 枚举 PDX USB 设备并检查驱动状态的纯 C# 实现。
    /// 替代之前通过 DllImport 调用 pdx-driver.exe 的方式（会导致栈溢出崩溃）。
    /// </summary>
    private static class PdxDeviceHelper
    {
        private readonly record struct SupportedDevice(ushort Vid, ushort Pid, byte Interface);

        private static readonly SupportedDevice[] SupportedDevices =
        [
            new(0x3356, 0x3003, 1),
            new(0x227D, 0x0103, 0)
        ];

        private const int DIGCF_PRESENT = 0x02;
        private const int DIGCF_ALLCLASSES = 0x04;

        private const int SPDRP_HARDWAREID = 0x01;
        private const int SPDRP_SERVICE = 0x04;
        private const int SPDRP_LOCATION_PATHS = 0x23;

        private static readonly Regex UsbPortRegex = new(@"#USB\((\d+)\)", RegexOptions.Compiled);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            IntPtr classGuid,
            string? enumerator,
            IntPtr hwndParent,
            int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            int memberIndex,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            int property,
            out int propertyRegDataType,
            byte[]? propertyBuffer,
            int propertyBufferSize,
            out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        public static string[] GetDevicePaths()
        {
            var paths = new HashSet<string>();
            var deviceInfoSet = SetupDiGetClassDevs(
                IntPtr.Zero, "USB", IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_ALLCLASSES);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                return [];

            try
            {
                var devInfoData = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
                };

                for (var i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, ref devInfoData); i++)
                {
                    var hardwareIds = GetMultiStringProperty(deviceInfoSet, ref devInfoData, SPDRP_HARDWAREID);
                    if (!MatchesSupportedDevice(hardwareIds, matchExclusiveInterface: false))
                        continue;

                    var portChain = GetPortChain(deviceInfoSet, ref devInfoData);
                    if (!string.IsNullOrEmpty(portChain))
                        paths.Add(portChain);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return [.. paths];
        }

        public static bool IsUsingWinUsb()
        {
            var deviceInfoSet = SetupDiGetClassDevs(
                IntPtr.Zero, "USB", IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_ALLCLASSES);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                return false;

            try
            {
                var devInfoData = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
                };

                for (var i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, ref devInfoData); i++)
                {
                    var hardwareIds = GetMultiStringProperty(deviceInfoSet, ref devInfoData, SPDRP_HARDWAREID);
                    if (!MatchesSupportedDevice(hardwareIds, matchExclusiveInterface: true))
                        continue;

                    var service = GetStringProperty(deviceInfoSet, ref devInfoData, SPDRP_SERVICE);
                    return string.Equals(service, "WinUSB", StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return false;
        }

        private static bool MatchesSupportedDevice(string[]? hardwareIds, bool matchExclusiveInterface)
        {
            if (hardwareIds == null) return false;

            foreach (var device in SupportedDevices)
            {
                var vid = $"VID_{device.Vid:X4}";
                var pid = $"PID_{device.Pid:X4}";
                var usbInterface = $"MI_{device.Interface:X2}";

                foreach (var id in hardwareIds)
                {
                    if (!id.Contains(vid, StringComparison.OrdinalIgnoreCase) ||
                        !id.Contains(pid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!matchExclusiveInterface || id.Contains(usbInterface, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从设备的 LocationPaths 属性提取 USB 端口链。
        /// LocationPaths 格式: PCIROOT(0)#PCI(0801)#USBROOT(0)#USB(2)#USB(2)#USBMI(1)
        /// 提取 #USB(\d+) 部分并用 "." 连接，如 "2.2"。
        /// </summary>
        private static string? GetPortChain(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData)
        {
            var locationPaths = GetMultiStringProperty(deviceInfoSet, ref devInfoData, SPDRP_LOCATION_PATHS);
            if (locationPaths == null || locationPaths.Length == 0) return null;

            // 使用第一个 LocationPath
            var path = locationPaths[0];
            var matches = UsbPortRegex.Matches(path);
            if (matches.Count == 0) return null;

            var ports = new string[matches.Count];
            for (var i = 0; i < matches.Count; i++)
                ports[i] = matches[i].Groups[1].Value;

            return string.Join(".", ports);
        }

        /// <summary>
        /// 获取设备的 REG_SZ 类型注册表属性。
        /// </summary>
        private static string? GetStringProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData, int property)
        {
            // 先查询所需大小（预期返回 false，ERROR_INSUFFICIENT_BUFFER）
            SetupDiGetDeviceRegistryProperty(
                deviceInfoSet, ref devInfoData, property,
                out _, null, 0, out int requiredSize);

            if (requiredSize <= 0) return null;

            var buffer = new byte[requiredSize];
            if (!SetupDiGetDeviceRegistryProperty(
                    deviceInfoSet, ref devInfoData, property,
                    out _, buffer, buffer.Length, out _))
                return null;

            return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }

        /// <summary>
        /// 获取设备的 REG_MULTI_SZ 类型注册表属性。
        /// REG_MULTI_SZ 格式: 多个 null 终止的字符串，最后以双 null 结尾。
        /// </summary>
        private static string[]? GetMultiStringProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData, int property)
        {
            // 先查询所需大小（预期返回 false，ERROR_INSUFFICIENT_BUFFER）
            SetupDiGetDeviceRegistryProperty(
                deviceInfoSet, ref devInfoData, property,
                out _, null, 0, out int requiredSize);

            if (requiredSize <= 0) return null;

            var buffer = new byte[requiredSize];
            if (!SetupDiGetDeviceRegistryProperty(
                    deviceInfoSet, ref devInfoData, property,
                    out _, buffer, buffer.Length, out _))
                return null;

            return Encoding.Unicode.GetString(buffer)
                .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
