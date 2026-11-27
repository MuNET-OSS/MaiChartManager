using HidSharp;

namespace MaiChartManager.Services.HardwareBridge;

public static class HidEnumerator
{
    private const int Io4Vid = 0x0CA3;
    private const int AdxVid = 0x2E3C;
    private const int AdxVid2P = 0x2E4C;
    private const int MmlVid = 0x0E8F;
    private const int MmlPid = 0x1224;

    public static List<HidDeviceCandidate> EnumerateKnownDevices(ILogger logger)
    {
        var result = new List<HidDeviceCandidate>();
        IEnumerable<HidDevice> devices;

        try
        {
            devices = DeviceList.Local.GetHidDevices();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HidSharp 枚举 HID 设备失败");
            return result;
        }

        foreach (var dev in devices)
        {
            try
            {
                var vid = dev.VendorID;
                var pid = dev.ProductID;
                var collections = ReadCollections(dev);

                if (vid == Io4Vid && collections.Any(c => c.UsagePage == 0x01 && c.Usage == 0x04))
                {
                    result.Add(new HidDeviceCandidate(
                        Id: dev.DevicePath,
                        VendorId: vid,
                        ProductId: pid,
                        ProductName: SafeProductName(dev, "SEGA IO4"),
                        DeviceKind: "io4",
                        PlayerSide: null,
                        Collections: collections,
                        RecommendationReason: "检测到 IO4 / Onii-mai 按键输入"));
                    continue;
                }

                if (TryMatchAdx(vid, pid, dev.DevicePath, out var adxSide))
                {
                    result.Add(new HidDeviceCandidate(
                        Id: dev.DevicePath,
                        VendorId: vid,
                        ProductId: pid,
                        ProductName: SafeProductName(dev, "ADX / NDX HID"),
                        DeviceKind: "adx",
                        PlayerSide: adxSide,
                        Collections: collections,
                        RecommendationReason: $"检测到 ADX / NDX HID ({adxSide})"));
                    continue;
                }

                if (vid == MmlVid && pid == MmlPid)
                {
                    var playerSide = ProbeMaimollerFeatureReport(dev, logger);
                    if (playerSide != null)
                    {
                        result.Add(new HidDeviceCandidate(
                            Id: dev.DevicePath,
                            VendorId: vid,
                            ProductId: pid,
                            ProductName: SafeProductName(dev, "Maimoller"),
                            DeviceKind: "maimoller",
                            PlayerSide: playerSide,
                            Collections: collections,
                            RecommendationReason: $"检测到 Maimoller 一体控制器 ({playerSide})"));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "读取 HID 设备信息时跳过: {Path}", dev.DevicePath);
            }
        }

        return result;
    }

    private static bool TryMatchAdx(int vid, int pid, string devicePath, out string playerSide)
    {
        playerSide = "1P";
        if (devicePath.EndsWith("kbd", StringComparison.OrdinalIgnoreCase))
            return false;

        if (vid == AdxVid && pid is 0x5750 or 0x5767)
        {
            playerSide = "1P";
            return true;
        }
        if ((vid == AdxVid2P && pid == 0x5750) || (vid == AdxVid && pid == 0x5768))
        {
            playerSide = "2P";
            return true;
        }
        return false;
    }

    private static string? ProbeMaimollerFeatureReport(HidDevice dev, ILogger logger)
    {
        try
        {
            var featureLen = dev.GetMaxFeatureReportLength();
            if (featureLen <= 4) return null;

            if (!dev.TryOpen(out var stream)) return null;
            using (stream)
            {
                var buf = new byte[featureLen];
                buf[0] = 1; // Report ID 1
                stream.GetFeature(buf);
                // buf[0] is Report ID, payload starts at buf[1]; payload[3] == 2 -> 2P (buf[4])
                return buf[4] == 2 ? "2P" : "1P";
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "跳过非控制接口 Maimoller HID: {Path}", dev.DevicePath);
            return null;
        }
    }

    private static HidCollectionInfo[] ReadCollections(HidDevice dev)
    {
        try
        {
            var descriptor = dev.GetReportDescriptor();
            var list = new List<HidCollectionInfo>();
            var seen = new HashSet<(int, int)>();
            foreach (var item in descriptor.DeviceItems)
            {
                foreach (var usage in item.Usages.GetAllValues())
                {
                    var usagePage = (int)(usage >> 16);
                    var usageId = (int)(usage & 0xFFFF);
                    if (seen.Add((usagePage, usageId)))
                        list.Add(new HidCollectionInfo(usagePage, usageId));
                }
            }
            return [.. list];
        }
        catch
        {
            return [];
        }
    }

    private static string SafeProductName(HidDevice dev, string fallback)
    {
        try
        {
            var name = dev.GetProductName();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch
        {
            // ignore
        }
        try
        {
            var friendly = dev.GetFriendlyName();
            if (!string.IsNullOrWhiteSpace(friendly)) return friendly;
        }
        catch
        {
            // ignore
        }
        return fallback;
    }
}
