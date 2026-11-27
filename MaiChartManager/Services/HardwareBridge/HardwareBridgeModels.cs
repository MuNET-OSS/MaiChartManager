namespace MaiChartManager.Services.HardwareBridge;

public record SerialDeviceCandidate(
    string Id,
    string PortName,
    string FriendlyName,
    int? VendorId,
    int? ProductId,
    string? Location,
    string[] RecommendedFor,
    string? RecommendationReason);

public record HidCollectionInfo(int UsagePage, int Usage);

public record HidDeviceCandidate(
    string Id,
    int VendorId,
    int ProductId,
    string ProductName,
    string? DeviceKind,
    string? PlayerSide,
    HidCollectionInfo[] Collections,
    string? RecommendationReason);

public record UsbDeviceCandidate(
    string Id,
    int VendorId,
    int ProductId,
    string ProductName,
    byte InterfaceNumber,
    string? DeviceKind,
    string? PlayerSide,
    string? Location,
    string? SerialNumber,
    string? RecommendationReason);

public record RecommendedSetup(
    string Mode,
    string Confidence,
    string Reason,
    bool AutoConnect,
    string? PlayerSide,
    string? TouchSerialId,
    int? TouchBaudRate,
    string? LedSerialId,
    string? HidDeviceId,
    string? UsbDeviceId);

public record AquaMaiHints(
    string? TouchPort1p,
    string? TouchPort2p,
    int? TouchBaudRate,
    string? LedPort1p,
    string? LedPort2p,
    bool? AdxHidEnabled,
    bool? MaimollerEnabled,
    bool? PdxTouchEnabled,
    string? PdxPath1p,
    string? PdxPath2p);

public record DiscoveryResult(
    SerialDeviceCandidate[] SerialPorts,
    HidDeviceCandidate[] HidDevices,
    UsbDeviceCandidate[] UsbDevices,
    RecommendedSetup[] RecommendedSetups,
    AquaMaiHints? AquaMaiHints);

public record SerialProbeResult(string Detected, string? Detail);
