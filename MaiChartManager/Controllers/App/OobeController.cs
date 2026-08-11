using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MaiChartManager.Platform;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.App;

public record CompleteSetupRequest(bool Export, bool UseAuth, string? AuthUsername, string? AuthPassword, bool StartupEnabled = false);

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class OobeController(
    StaticSettings settings,
    ILogger<OobeController> logger,
    IAppShell appShell,
    IDesktopDialogService dialogService) : ControllerBase
{
    private bool IsLoopbackRequest()
        => HttpContext.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp);

    [HttpGet]
    public string? GetGamePath()
    {
        return StaticSettings.Config.GamePath;
    }

    [HttpPost]
    public IActionResult SetGamePath([FromBody] string path, bool save = false)
    {
        if (!Path.Exists(path))
        {
            return BadRequest("Path does not exist");
        }

        StaticSettings.GamePath = path;
        if (!Directory.Exists(StaticSettings.StreamingAssets) && Directory.Exists(Path.Combine(StaticSettings.GamePath, "Package")))
        {
            StaticSettings.GamePath = Path.Combine(StaticSettings.GamePath, "Package");
        }

        if (!Directory.Exists(StaticSettings.StreamingAssets))
        {
            return BadRequest("StreamingAssets not found. Not a valid game directory.");
        }

        StaticSettings.Config.GamePath = StaticSettings.GamePath;
        StaticSettings.Config.HistoryPath.Add(path);
        if (save)
        {
            // oobe 阶段不需要保存，下面会保存。但是在主界面设置里需要保存
            StaticSettings.Config.Save();
        }

        appShell.UpdateMainWindowTitle(StaticSettings.GamePath);

        return Ok();
    }

    [HttpGet]
    public HashSet<string> GetGamePathHistory()
    {
        return StaticSettings.Config.HistoryPath;
    }

    [HttpPost]
    public IActionResult DeleteGamePathHistory([FromBody] string path)
    {
        StaticSettings.Config.HistoryPath.Remove(path);
        StaticSettings.Config.Save();
        return Ok();
    }

    [HttpPost]
    public async Task InitializeGameData()
    {
        await settings.InitializeGameData();
    }

    [HttpGet]
    public string? OpenFolderDialog()
    {
        return dialogService.PickFolder();
    }

    [HttpGet]
    public List<string> GetLanAddresses()
    {
        return Dns.GetHostAddresses(Dns.GetHostName())
            .Where(it => it.AddressFamily == AddressFamily.InterNetwork)
            .Select(it => it.ToString())
            .ToList();
    }

    [HttpPost]
    public async Task<IActionResult> CompleteSetup([FromBody] CompleteSetupRequest request)
    {
        if (!IsLoopbackRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        var exportChanged = request.Export != StaticSettings.Config.Export;
        StaticSettings.Config.Export = request.Export;
        StaticSettings.Config.UseAuth = request.UseAuth;
        StaticSettings.Config.AuthUsername = request.AuthUsername ?? "";
        StaticSettings.Config.AuthPassword = request.AuthPassword ?? "";
        StaticSettings.Config.Save();

        if (exportChanged)
        {
            appShell.DisposeTrayIcon();
            // 管理开机启动
            await appShell.SetStartupEnabledAsync(request.Export && request.StartupEnabled);
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                if (ServerManager.IsRunning)
                {
                    await ServerManager.StopAsync();
                }
                ServerManager.StartApp(request.Export, (url) =>
                {
                    if (StaticSettings.Config.Export)
                    {
                        // 局域网模式：服务器重启后端口变了，需要把新 URL 注入回 OOBE 浏览器
                        appShell.InjectOobeBackendUrl(url);
                        return;
                    }
                    appShell.ShowBrowser(url);
                    appShell.CloseOobeBrowser();
                });
            });
        }
        else if (!request.Export)
        {
            appShell.ShowBrowser(ServerManager.GetLoopbackUrl() ?? throw new InvalidOperationException("Loopback URL is null"));
            appShell.CloseOobeBrowser();
        }

        return Ok();
    }

    [HttpPost]
    public void OpenMainUI()
    {
        appShell.ShowBrowser(ServerManager.GetLoopbackUrl() ?? throw new InvalidOperationException("Loopback URL is null"));
    }

    [HttpPost]
    public void SwitchToSetMode()
    {
        var url = ServerManager.GetLoopbackUrl() ?? throw new InvalidOperationException("Loopback URL is null");
        appShell.GoToModeSwitch(url);
    }
}
