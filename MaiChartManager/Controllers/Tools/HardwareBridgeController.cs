using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
#if WINDOWS
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Runtime.InteropServices;
using HidSharp;
using MaiChartManager.Services.HardwareBridge;
#endif

namespace MaiChartManager.Controllers.Tools;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class HardwareBridgeController(
#if WINDOWS
    HardwareDiscoveryService discoveryService,
    AquaMaiHardwareHintsReader hintsReader,
    ILogger<HardwareBridgeController> logger
#endif
) : ControllerBase
{
    public record HardwareBridgeStatusDto(bool Available, string Platform);

    [HttpGet]
    public ActionResult<HardwareBridgeStatusDto> HardwareBridgeStatus()
    {
        if (!IsAllowedRequest())
            return StatusCode(StatusCodes.Status403Forbidden);

#if WINDOWS
        return Ok(new HardwareBridgeStatusDto(true, "windows"));
#else
        return Ok(new HardwareBridgeStatusDto(false, "linux"));
#endif
    }

#if WINDOWS
    private static readonly ConcurrentDictionary<string, ActiveSerialSession> ActiveSerialSessions =
        new(StringComparer.OrdinalIgnoreCase);

    [HttpGet]
    public ActionResult<DiscoveryResult> HardwareBridgeDiscover()
    {
        if (!IsAllowedRequest())
            return StatusCode(StatusCodes.Status403Forbidden);

        return Ok(discoveryService.Discover());
    }

    [HttpPost]
    public async Task<ActionResult<SerialProbeResult>> HardwareBridgeProbeSerial(
        [FromQuery] string portName,
        CancellationToken ct)
    {
        if (!IsAllowedRequest())
            return StatusCode(StatusCodes.Status403Forbidden);

        if (string.IsNullOrWhiteSpace(portName))
            return BadRequest("portName is required");

        var trimmedPort = portName.Trim();
        if (ActiveSerialSessions.TryGetValue(trimmedPort, out var active))
        {
            return Ok(active.ObservedKind switch
            {
                "touch" => new SerialProbeResult("touch", "当前已连接，检测到触摸屏数据帧"),
                "led" => new SerialProbeResult("led", "当前已连接，检测到灯板应答"),
                _ => new SerialProbeResult("unknown", "端口当前已连接，未收到协议应答"),
            });
        }

        var hints = hintsReader.ReadHints();
        var result = await SerialPortEnumerator.ProbePortAsync(trimmedPort, ct, hints?.TouchBaudRate);
        return Ok(result);
    }

    [HttpGet]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task HardwareBridgeSerialWs(
        [FromQuery] string portName,
        [FromQuery] int baudRate = 9600,
        [FromQuery] int dataBits = 8,
        [FromQuery] int stopBits = 1,
        [FromQuery] string parity = "none",
        CancellationToken ct = default)
    {
        if (!IsAllowedRequest())
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
        using var sendLock = new SemaphoreSlim(1, 1);

        if (string.IsNullOrWhiteSpace(portName))
        {
            await SendErrorAndCloseAsync(ws, sendLock, "portName 不能为空");
            return;
        }

        var trimmedPort = portName.Trim();
        SerialPort? serialPort = null;
        try
        {
            var resolvedParity = parity.ToLowerInvariant() switch
            {
                "even" => Parity.Even,
                "odd" => Parity.Odd,
                _ => Parity.None,
            };
            var resolvedStopBits = stopBits == 2 ? StopBits.Two : StopBits.One;

            serialPort = new SerialPort(trimmedPort, baudRate, resolvedParity, dataBits, resolvedStopBits)
            {
                DtrEnable = true,
                RtsEnable = true,
                // CH340 (CH341SER) 驱动下 BaseStream.ReadAsync 会触发 ERROR_OPERATION_ABORTED 或立即返回 0，
                // 需配合有限 ReadTimeout 使用同步 SerialPort.Read/Write
                ReadTimeout = 200,
                WriteTimeout = 1000,
            };
            serialPort.Open();
        }
        catch (Exception ex)
        {
            serialPort?.Dispose();
            logger.LogWarning(ex, "打开串口失败: {Port}", portName);
            await SendErrorAndCloseAsync(ws, sendLock, DescribeOpenError(ex, $"串口 {portName}"));
            return;
        }

        var session = new ActiveSerialSession();
        ActiveSerialSessions[trimmedPort] = session;

        try
        {
            await SendJsonAsync(ws, sendLock, new { type = "ready" }, ct);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = linkedCts.Token;

            var readTask = Task.Factory.StartNew(() =>
            {
                var buf = new byte[1024];
                try
                {
                    while (!token.IsCancellationRequested && ws.State == WebSocketState.Open && serialPort.IsOpen)
                    {
                        int read;
                        try
                        {
                            read = serialPort.Read(buf, 0, buf.Length);
                        }
                        catch (TimeoutException)
                        {
                            continue;
                        }
                        if (read <= 0)
                        {
                            if (!serialPort.IsOpen)
                            {
                                SendJsonAsync(ws, sendLock, new { type = "disconnected" }, CancellationToken.None).GetAwaiter().GetResult();
                                break;
                            }
                            continue;
                        }
                        session.Feed(buf.AsSpan(0, read));
                        SendBinaryAsync(ws, sendLock, buf.AsMemory(0, read), token).GetAwaiter().GetResult();
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (WebSocketException)
                {
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or UnauthorizedAccessException)
                {
                    if (!token.IsCancellationRequested)
                    {
                        logger.LogDebug(ex, "串口读取断开: {Port}", portName);
                        SendJsonAsync(ws, sendLock, new { type = "disconnected" }, CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
                finally
                {
                    linkedCts.Cancel();
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            var writeTask = ReceiveWsLoopAsync(ws, data =>
            {
                if (MemoryMarshal.TryGetArray(data, out var segment) && segment.Array != null)
                {
                    serialPort.Write(segment.Array, segment.Offset, segment.Count);
                }
                else
                {
                    var arr = data.ToArray();
                    serialPort.Write(arr, 0, arr.Length);
                }
                return Task.CompletedTask;
            }, linkedCts, token);

            await Task.WhenAny(readTask, writeTask);
            linkedCts.Cancel();
            serialPort.Dispose();
            await Task.WhenAll(IgnoreErrors(readTask), IgnoreErrors(writeTask));
        }
        finally
        {
            ActiveSerialSessions.TryRemove(new KeyValuePair<string, ActiveSerialSession>(trimmedPort, session));
            serialPort.Dispose();
            await CloseWebSocketQuietlyAsync(ws);
        }
    }

    private sealed class ActiveSerialSession
    {
        private readonly object _lock = new();
        private volatile string? _observedKind;
        private int _touchFrameLen = -1;
        private readonly List<byte> _ledBuf = new(64);
        private bool _ledEscape;
        private bool _ledSynced;

        public string? ObservedKind => _observedKind;

        public void Feed(ReadOnlySpan<byte> data)
        {
            if (_observedKind != null) return;

            lock (_lock)
            {
                if (_observedKind != null) return;

                foreach (var raw in data)
                {
                    // Touch frame: '(' + 7 bytes + ')'
                    if (raw == (byte)'(')
                    {
                        _touchFrameLen = 0;
                    }
                    else if (raw == (byte)')')
                    {
                        if (_touchFrameLen == 7)
                        {
                            _observedKind = "touch";
                            return;
                        }
                        _touchFrameLen = -1;
                    }
                    else if (_touchFrameLen >= 0)
                    {
                        _touchFrameLen++;
                        if (_touchFrameLen > 16) _touchFrameLen = -1;
                    }

                    // SEGA LED frame: 0xE0 ... checksum
                    if (raw == 0xE0)
                    {
                        _ledBuf.Clear();
                        _ledEscape = false;
                        _ledSynced = true;
                        continue;
                    }
                    if (!_ledSynced) continue;

                    var b = raw;
                    if (b == 0xD0)
                    {
                        _ledEscape = true;
                        continue;
                    }
                    if (_ledEscape)
                    {
                        b++;
                        _ledEscape = false;
                    }
                    _ledBuf.Add(b);
                    if (_ledBuf.Count >= 4)
                    {
                        var expectedLen = _ledBuf[2] + 3;
                        if (_ledBuf.Count == expectedLen + 1)
                        {
                            var sum = 0;
                            for (var j = 0; j < expectedLen; j++)
                                sum = (sum + _ledBuf[j]) & 0xFF;
                            if (sum == _ledBuf[expectedLen])
                            {
                                _observedKind = "led";
                                return;
                            }
                            _ledBuf.Clear();
                            _ledSynced = false;
                        }
                        else if (_ledBuf.Count > 128)
                        {
                            _ledBuf.Clear();
                            _ledSynced = false;
                        }
                    }
                }
            }
        }
    }

    [HttpGet]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task HardwareBridgeHidWs(
        [FromQuery] string deviceId,
        CancellationToken ct = default)
    {
        if (!IsAllowedRequest())
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
        using var sendLock = new SemaphoreSlim(1, 1);

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await SendErrorAndCloseAsync(ws, sendLock, "deviceId 不能为空");
            return;
        }

        HidDevice? device;
        HidStream? stream = null;
        try
        {
            device = DeviceList.Local.GetHidDevices()
                .FirstOrDefault(d => string.Equals(d.DevicePath, deviceId, StringComparison.OrdinalIgnoreCase));
            if (device == null)
            {
                await SendErrorAndCloseAsync(ws, sendLock, "未找到指定的 HID 设备");
                return;
            }

            if (!device.TryOpen(out stream))
            {
                await SendErrorAndCloseAsync(ws, sendLock, "无法打开 HID 设备，可能正被其他程序占用");
                return;
            }
            stream.ReadTimeout = 500;
            stream.WriteTimeout = 1000;
        }
        catch (Exception ex)
        {
            stream?.Dispose();
            logger.LogWarning(ex, "打开 HID 设备失败: {DeviceId}", deviceId);
            await SendErrorAndCloseAsync(ws, sendLock, DescribeOpenError(ex, "HID 设备"));
            return;
        }

        var maxInLen = Math.Max(device.GetMaxInputReportLength(), 64);
        var maxOutLen = device.GetMaxOutputReportLength();
        var maxFeatLen = device.GetMaxFeatureReportLength();

        try
        {
            await SendJsonAsync(ws, sendLock, new { type = "ready" }, ct);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = linkedCts.Token;

            var readTask = Task.Factory.StartNew(async () =>
            {
                var buf = new byte[maxInLen];
                try
                {
                    while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        int read;
                        try
                        {
                            read = stream.Read(buf, 0, buf.Length);
                        }
                        catch (TimeoutException)
                        {
                            continue;
                        }

                        if (read <= 0)
                        {
                            await SendJsonAsync(ws, sendLock, new { type = "disconnected" }, CancellationToken.None);
                            break;
                        }

                        await SendBinaryAsync(ws, sendLock, buf.AsMemory(0, read), token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    if (!token.IsCancellationRequested)
                    {
                        logger.LogDebug(ex, "HID 读取断开: {DeviceId}", deviceId);
                        await SendJsonAsync(ws, sendLock, new { type = "disconnected" }, CancellationToken.None);
                    }
                }
                finally
                {
                    linkedCts.Cancel();
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

            var writeTask = ReceiveWsLoopAsync(ws, async data =>
            {
                if (data.Length < 2) return;
                var op = data.Span[0];
                if (op == 0x01)
                {
                    // [0x01, reportId, ...payload]
                    var reportId = data.Span[1];
                    var payload = data.Span[2..];
                    var outLen = Math.Max(maxOutLen, 1 + payload.Length);
                    var outBuf = new byte[outLen];
                    outBuf[0] = reportId;
                    payload.CopyTo(outBuf.AsSpan(1));
                    stream.Write(outBuf);
                }
                else if (op == 0x02 && data.Length >= 3)
                {
                    // [0x02, reqId, reportId]
                    var reqId = data.Span[1];
                    var reportId = data.Span[2];
                    try
                    {
                        if (maxFeatLen <= 1)
                        {
                            await SendJsonAsync(ws, sendLock, new
                            {
                                type = "featureReport",
                                reqId,
                                ok = false,
                                error = "设备不支持 Feature Report",
                            }, token);
                            return;
                        }

                        var featBuf = new byte[maxFeatLen];
                        featBuf[0] = reportId;
                        stream.GetFeature(featBuf);
                        var base64 = Convert.ToBase64String(featBuf.AsSpan(1));
                        await SendJsonAsync(ws, sendLock, new
                        {
                            type = "featureReport",
                            reqId,
                            ok = true,
                            dataBase64 = base64,
                        }, token);
                    }
                    catch (Exception ex)
                    {
                        await SendJsonAsync(ws, sendLock, new
                        {
                            type = "featureReport",
                            reqId,
                            ok = false,
                            error = ex.Message,
                        }, token);
                    }
                }
            }, linkedCts, token);

            await Task.WhenAny(readTask, writeTask);
            linkedCts.Cancel();
            stream.Dispose();
            await Task.WhenAll(IgnoreErrors(readTask), IgnoreErrors(writeTask));
        }
        finally
        {
            stream.Dispose();
            await CloseWebSocketQuietlyAsync(ws);
        }
    }

    [HttpGet]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task HardwareBridgeUsbWs(
        [FromQuery] string deviceId,
        [FromQuery] int inEndpoint,
        [FromQuery] int? outEndpoint = null,
        [FromQuery] int inPacketSize = 64,
        CancellationToken ct = default)
    {
        if (!IsAllowedRequest())
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
        using var sendLock = new SemaphoreSlim(1, 1);

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await SendErrorAndCloseAsync(ws, sendLock, "deviceId 不能为空");
            return;
        }

        WinUsbBridge.Device? usbDevice = null;
        try
        {
            var resolvedIn = (byte)((inEndpoint & 0x0F) | 0x80);
            byte? resolvedOut = outEndpoint.HasValue ? (byte)(outEndpoint.Value & 0x0F) : null;
            usbDevice = WinUsbBridge.Device.Open(deviceId, resolvedIn, resolvedOut);
        }
        catch (Exception ex)
        {
            usbDevice?.Dispose();
            logger.LogWarning(ex, "打开 WinUSB 设备失败: {DeviceId}", deviceId);
            await SendErrorAndCloseAsync(ws, sendLock, DescribeOpenError(ex, "WinUSB 设备"));
            return;
        }

        var packetSize = Math.Clamp(inPacketSize, 8, 4096);

        try
        {
            await SendJsonAsync(ws, sendLock, new { type = "ready" }, ct);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = linkedCts.Token;

            var readTask = Task.Factory.StartNew(async () =>
            {
                var buf = new byte[packetSize];
                try
                {
                    while (!token.IsCancellationRequested && ws.State == WebSocketState.Open && usbDevice.IsOpen)
                    {
                        var read = usbDevice.Read(buf, 0, buf.Length, 200);
                        if (read > 0)
                        {
                            await SendBinaryAsync(ws, sendLock, buf.AsMemory(0, read), token);
                        }
                        else if (read < 0)
                        {
                            if (!token.IsCancellationRequested)
                            {
                                await SendJsonAsync(ws, sendLock, new { type = "disconnected" }, CancellationToken.None);
                            }
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    linkedCts.Cancel();
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

            var writeTask = ReceiveWsLoopAsync(ws, data =>
            {
                var arr = data.ToArray();
                var written = usbDevice.Write(arr, 0, arr.Length, 1000);
                if (written < 0)
                {
                    throw new IOException("WinUSB 写入失败");
                }
                return Task.CompletedTask;
            }, linkedCts, token);

            await Task.WhenAny(readTask, writeTask);
            linkedCts.Cancel();
            usbDevice.Abort();
            usbDevice.Dispose();
            await Task.WhenAll(IgnoreErrors(readTask), IgnoreErrors(writeTask));
        }
        finally
        {
            usbDevice.Dispose();
            await CloseWebSocketQuietlyAsync(ws);
        }
    }

    private static string DescribeOpenError(Exception ex, string label) => ex switch
    {
        UnauthorizedAccessException => $"{label}正被占用或无权访问",
        _ => ex.Message,
    };

    private static async Task ReceiveWsLoopAsync(
        WebSocket ws,
        Func<ReadOnlyMemory<byte>, Task> onBinaryMessage,
        CancellationTokenSource linkedCts,
        CancellationToken token)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();

        try
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        linkedCts.Cancel();
                        return;
                    }
                    if (result.Count > 0)
                    {
                        ms.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary && ms.Length > 0)
                {
                    await onBinaryMessage(ms.GetBuffer().AsMemory(0, (int)ms.Length));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            linkedCts.Cancel();
        }
    }

    private static async Task SendBinaryAsync(WebSocket ws, SemaphoreSlim sendLock, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await sendLock.WaitAsync(ct);
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.SendAsync(payload, WebSocketMessageType.Binary, true, ct);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task SendJsonAsync(WebSocket ws, SemaphoreSlim sendLock, object obj, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
        await sendLock.WaitAsync(ct);
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
        catch
        {
            // ignore closed socket while notifying
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task SendErrorAndCloseAsync(WebSocket ws, SemaphoreSlim sendLock, string message)
    {
        await SendJsonAsync(ws, sendLock, new { type = "error", message }, CancellationToken.None);
        await CloseWebSocketQuietlyAsync(ws);
    }

    private static async Task CloseWebSocketQuietlyAsync(WebSocket ws)
    {
        try
        {
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static async Task IgnoreErrors(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // ignore
        }
    }
#endif

    private bool IsAllowedRequest()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
            return false;

        var origin = HttpContext.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        if (string.Equals(originUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(originUri.Host, "mcm.invalid", StringComparison.OrdinalIgnoreCase))
            return true;

        if (originUri.Scheme is not ("http" or "https"))
            return false;

        return originUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || originUri.Host.Equals("127.0.0.1", StringComparison.Ordinal)
               || originUri.Host.Equals("[::1]", StringComparison.Ordinal)
               || originUri.Host.Equals("::1", StringComparison.Ordinal);
    }
}
