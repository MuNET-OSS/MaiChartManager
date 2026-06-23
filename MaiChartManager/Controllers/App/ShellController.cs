using MaiChartManager.Platform;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.App;

/// <summary>
/// 外壳相关接口（用系统能力打开 URL 等）。
/// 主要给 Linux/Photino 用：WebKitGTK 不支持 window.open 弹新窗口，
/// 预览谱面等"开新窗口"的场景改为用系统浏览器（xdg-open）打开 loopback 上的对应页面。
/// </summary>
[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class ShellController(IShellService shellService, ILogger<ShellController> logger) : ControllerBase
{
    public record OpenExternalUrlRequest(string Url);

    [HttpPost]
    public IActionResult OpenExternalUrl([FromBody] OpenExternalUrlRequest request)
    {
        // 只允许 http/https，避免被诱导打开任意协议
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest("Only http/https URLs are allowed");
        }

        try
        {
            shellService.OpenUrl(request.Url);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "打开外部 URL 失败：{Url}", request.Url);
            return StatusCode(500, e.Message);
        }
    }
}
