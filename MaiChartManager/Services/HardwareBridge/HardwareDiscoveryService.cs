using System.Text.RegularExpressions;

namespace MaiChartManager.Services.HardwareBridge;

public class HardwareDiscoveryService(
    AquaMaiHardwareHintsReader hintsReader,
    ILogger<HardwareDiscoveryService> logger)
{
    private static readonly Regex TouchNameRegex = new(@"\btouch\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LedNameRegex = new(@"\bled\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public DiscoveryResult Discover()
    {
        var hints = hintsReader.ReadHints();
        var rawPorts = SerialPortEnumerator.EnumeratePorts(logger);
        var hidDevices = HidEnumerator.EnumerateKnownDevices(logger);
        var usbDevices = WinUsbBridge.EnumerateKnownDevices(hints, logger);

        var serialPorts = AnnotateSerialPorts(rawPorts, hints);
        var setups = BuildRecommendations(serialPorts, hidDevices, usbDevices, hints);

        return new DiscoveryResult(
            SerialPorts: [.. serialPorts],
            HidDevices: [.. hidDevices],
            UsbDevices: [.. usbDevices],
            RecommendedSetups: [.. setups],
            AquaMaiHints: hints);
    }

    private static List<SerialDeviceCandidate> AnnotateSerialPorts(
        List<SerialPortEnumerator.RawPortInfo> rawPorts,
        AquaMaiHints? hints)
    {
        var list = new List<SerialDeviceCandidate>(rawPorts.Count);
        foreach (var p in rawPorts)
        {
            var roles = new List<string>();
            var reasons = new List<string>();

            if (!string.IsNullOrEmpty(hints?.TouchPort1p) && string.Equals(p.PortName, hints.TouchPort1p, StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("touch");
                reasons.Add("AquaMai 1P 触摸配置");
            }
            else if (!string.IsNullOrEmpty(hints?.TouchPort2p) && string.Equals(p.PortName, hints.TouchPort2p, StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("touch");
                reasons.Add("AquaMai 2P 触摸配置");
            }
            else if (string.Equals(p.PortName, "COM3", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("touch");
                reasons.Add("默认 1P 触摸串口");
            }
            else if (string.Equals(p.PortName, "COM4", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("touch");
                reasons.Add("默认 2P 触摸串口");
            }

            if (!string.IsNullOrEmpty(hints?.LedPort1p) && string.Equals(p.PortName, hints.LedPort1p, StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("led");
                reasons.Add("AquaMai 1P 灯板配置");
            }
            else if (!string.IsNullOrEmpty(hints?.LedPort2p) && string.Equals(p.PortName, hints.LedPort2p, StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("led");
                reasons.Add("AquaMai 2P 灯板配置");
            }
            else if (string.Equals(p.PortName, "COM21", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("led");
                reasons.Add("默认 1P 灯板串口");
            }
            else if (string.Equals(p.PortName, "COM23", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add("led");
                reasons.Add("默认 2P 灯板串口");
            }

            var nameSource = p.BusReportedDescription ?? p.FriendlyName;
            if (!string.IsNullOrEmpty(nameSource))
            {
                if (!roles.Contains("touch") && TouchNameRegex.IsMatch(nameSource))
                {
                    roles.Add("touch");
                    reasons.Add("设备名称匹配触摸屏");
                }
                if (!roles.Contains("led") && LedNameRegex.IsMatch(nameSource))
                {
                    roles.Add("led");
                    reasons.Add("设备名称匹配灯板");
                }
            }

            list.Add(new SerialDeviceCandidate(
                Id: p.PortName,
                PortName: p.PortName,
                FriendlyName: p.FriendlyName,
                VendorId: p.VendorId,
                ProductId: p.ProductId,
                Location: p.Location,
                RecommendedFor: [.. roles],
                RecommendationReason: reasons.Count > 0 ? string.Join(" / ", reasons) : null));
        }
        return list;
    }

    private static List<RecommendedSetup> BuildRecommendations(
        List<SerialDeviceCandidate> serialPorts,
        List<HidDeviceCandidate> hidDevices,
        List<UsbDeviceCandidate> usbDevices,
        AquaMaiHints? hints)
    {
        var candidates = new List<(int Score, RecommendedSetup Setup)>();

        string? FindSerial(string? preferred1, string fallback, string role, int? preferVid = null, int? preferPid = null)
        {
            if (!string.IsNullOrEmpty(preferred1))
            {
                var match = serialPorts.FirstOrDefault(p => string.Equals(p.PortName, preferred1, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Id;
            }

            var oppositeCom = fallback.ToUpperInvariant() switch
            {
                "COM3" => "COM4",
                "COM4" => "COM3",
                "COM21" => "COM23",
                "COM23" => "COM21",
                _ => null,
            };

            if (preferVid.HasValue)
            {
                var devMatch = serialPorts.FirstOrDefault(p =>
                    p.VendorId == preferVid &&
                    (!preferPid.HasValue || p.ProductId == preferPid) &&
                    p.RecommendedFor.Contains(role) &&
                    !string.Equals(p.PortName, oppositeCom, StringComparison.OrdinalIgnoreCase));
                if (devMatch != null) return devMatch.Id;
            }

            var fallbackMatch = serialPorts.FirstOrDefault(p => string.Equals(p.PortName, fallback, StringComparison.OrdinalIgnoreCase));
            if (fallbackMatch != null) return fallbackMatch.Id;

            return serialPorts.FirstOrDefault(p =>
                p.RecommendedFor.Contains(role) &&
                !string.Equals(p.PortName, oppositeCom, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        // 1. NPro WinUSB
        foreach (var npro in usbDevices.Where(d => d.DeviceKind == "npro"))
        {
            var score = 100;
            if (hints?.AdxHidEnabled == true) score += 20;
            if (npro.PlayerSide == "1P") score += 5;
            candidates.Add((score, new RecommendedSetup(
                Mode: "npro",
                Confidence: "high",
                Reason: "NPro WinUSB",
                AutoConnect: false,
                PlayerSide: npro.PlayerSide,
                TouchSerialId: null,
                TouchBaudRate: null,
                LedSerialId: null,
                HidDeviceId: null,
                UsbDeviceId: npro.Id)));
        }

        // 2. Maimoller
        foreach (var mml in hidDevices.Where(d => d.DeviceKind == "maimoller"))
        {
            var score = 100;
            if (hints?.MaimollerEnabled == true) score += 25;
            if (mml.PlayerSide == "1P") score += 5;
            candidates.Add((score, new RecommendedSetup(
                Mode: "maimoller",
                Confidence: "high",
                Reason: "Maimoller 控制器",
                AutoConnect: false,
                PlayerSide: mml.PlayerSide,
                TouchSerialId: null,
                TouchBaudRate: null,
                LedSerialId: null,
                HidDeviceId: mml.Id,
                UsbDeviceId: null)));
        }

        // 3. PDX WinUSB
        foreach (var pdx in usbDevices.Where(d => d.DeviceKind == "pdx"))
        {
            var side = pdx.PlayerSide ?? "1P";
            var io4 = hidDevices.FirstOrDefault(d => d.DeviceKind == "io4");
            var ledId = side == "2P"
                ? FindSerial(hints?.LedPort2p, "COM23", "led", io4?.VendorId, io4?.ProductId)
                : FindSerial(hints?.LedPort1p, "COM21", "led", io4?.VendorId, io4?.ProductId);
            var score = hints?.PdxTouchEnabled == true ? 115 : 60;
            if (side == "1P") score += 5;
            candidates.Add((score, new RecommendedSetup(
                Mode: "pdx",
                Confidence: hints?.PdxTouchEnabled == true ? "high" : "medium",
                Reason: "PDX 独占触摸",
                AutoConnect: false,
                PlayerSide: pdx.PlayerSide,
                TouchSerialId: null,
                TouchBaudRate: null,
                LedSerialId: ledId,
                HidDeviceId: io4?.Id,
                UsbDeviceId: pdx.Id)));
        }

        // 4. ADX / NDX HID
        foreach (var adx in hidDevices.Where(d => d.DeviceKind == "adx"))
        {
            var side = adx.PlayerSide ?? "1P";
            var touchId = side == "2P"
                ? FindSerial(hints?.TouchPort2p, "COM4", "touch", adx.VendorId, adx.ProductId)
                : FindSerial(hints?.TouchPort1p, "COM3", "touch", adx.VendorId, adx.ProductId);
            var ledId = side == "2P"
                ? FindSerial(hints?.LedPort2p, "COM23", "led", adx.VendorId, adx.ProductId)
                : FindSerial(hints?.LedPort1p, "COM21", "led", adx.VendorId, adx.ProductId);
            var score = 95;
            if (hints?.AdxHidEnabled == true) score += 15;
            if (side == "1P") score += 5;
            candidates.Add((score, new RecommendedSetup(
                Mode: "adx",
                Confidence: "high",
                Reason: "ADX / NDX HID",
                AutoConnect: false,
                PlayerSide: side,
                TouchSerialId: touchId,
                TouchBaudRate: hints?.TouchBaudRate,
                LedSerialId: ledId,
                HidDeviceId: adx.Id,
                UsbDeviceId: null)));
        }

        // 5. IO4 HID
        foreach (var io4 in hidDevices.Where(d => d.DeviceKind == "io4"))
        {
            var touchId = FindSerial(hints?.TouchPort1p, "COM3", "touch", io4.VendorId, io4.ProductId);
            var ledId = FindSerial(hints?.LedPort1p, "COM21", "led", io4.VendorId, io4.ProductId);
            candidates.Add((90, new RecommendedSetup(
                Mode: "io4",
                Confidence: "high",
                Reason: "Onii-mai / HDX / IO4",
                AutoConnect: false,
                PlayerSide: "1P",
                TouchSerialId: touchId,
                TouchBaudRate: hints?.TouchBaudRate,
                LedSerialId: ledId,
                HidDeviceId: io4.Id,
                UsbDeviceId: null)));
        }

        // 6. Fallback: serial-only default / AquaMai ports when no HID/USB controller detected
        if (candidates.Count == 0)
        {
            var touch1 = FindSerial(hints?.TouchPort1p, "COM3", "touch");
            var led1 = FindSerial(hints?.LedPort1p, "COM21", "led");
            if (touch1 != null || led1 != null)
            {
                candidates.Add((40, new RecommendedSetup(
                    Mode: "io4",
                    Confidence: "medium",
                    Reason: "检测到标准触摸/灯板串口",
                    AutoConnect: false,
                    PlayerSide: "1P",
                    TouchSerialId: touch1,
                    TouchBaudRate: hints?.TouchBaudRate,
                    LedSerialId: led1,
                    HidDeviceId: null,
                    UsbDeviceId: null)));
            }
        }

        var ordered = candidates
            .OrderByDescending(c => c.Score)
            .Select(c => c.Setup)
            .ToList();

        // Auto-connect only when unambiguous:
        // single high-confidence setup, OR all high-confidence setups share the same mode (e.g. 1P/2P pair) with a single 1P.
        var highSetups = ordered.Where(s => s.Confidence == "high").ToList();
        if (highSetups.Count == 1)
        {
            var idx = ordered.IndexOf(highSetups[0]);
            ordered[idx] = highSetups[0] with { AutoConnect = true };
        }
        else if (highSetups.Count > 1 && highSetups.Select(s => s.Mode).Distinct().Count() == 1)
        {
            var p1Setups = highSetups.Where(s => s.PlayerSide == "1P").ToList();
            if (p1Setups.Count == 1)
            {
                var idx = ordered.IndexOf(p1Setups[0]);
                ordered[idx] = p1Setups[0] with { AutoConnect = true };
            }
        }

        return ordered;
    }
}
