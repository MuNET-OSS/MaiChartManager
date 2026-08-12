using System.Net;
using MaiChartManager.Platform;
using MaiChartManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Tools;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class ResourceJunctionController(ResourceJunctionService service, IDesktopDialogService dialogService) : ControllerBase
{
    private const string SessionHeader = "X-MCM-Resource-Junction-Session";

    [HttpGet]
    public ActionResult<ResourceJunctionOverview> GetResourceJunctionStatus()
    {
        if (!IsLoopbackRequest())
            return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(service.GetOverview(GetSessionId()));
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> AutoSelectResourceJunctionSource()
    {
        if (RejectRemoteRequest() is { } rejection) return rejection;
        return Ok(service.AutoSelectSource(GetSessionId()));
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> SelectResourceJunctionSource()
    {
        if (RejectRemoteRequest() is { } rejection) return rejection;

        var path = dialogService.PickFolder(
            Locale.ResourceManager.GetString("SelectResourceJunctionSourceFolder", Locale.Culture));
        if (path is null) return Ok(service.GetOverview(GetSessionId()));
        try
        {
            return Ok(service.SelectManualSource(path, GetSessionId()));
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> SelectResourceJunctionTarget()
    {
        if (RejectRemoteRequest() is { } rejection) return rejection;

        var path = dialogService.PickFolder(
            Locale.ResourceManager.GetString("SelectResourceJunctionTargetFolder", Locale.Culture));
        if (path is null) return Ok(service.GetOverview(GetSessionId()));
        try
        {
            return Ok(service.SelectManualTarget(path, GetSessionId()));
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> CreateResourceJunctions()
    {
        if (RejectRemoteRequest() is { } rejection) return rejection;
        var sessionId = GetSessionId();
        var items = service.CreateLinks(sessionId);
        return Ok(service.GetOverview(sessionId) with { Items = items });
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> RemoveResourceJunctions()
    {
        if (RejectRemoteRequest() is { } rejection) return rejection;
        var sessionId = GetSessionId();
        var items = service.RemoveLinks(sessionId);
        return Ok(service.GetOverview(sessionId) with { Items = items });
    }

    private ActionResult? RejectRemoteRequest()
    {
        if (!IsLoopbackRequest())
            return StatusCode(StatusCodes.Status403Forbidden);
        return null;
    }

    private bool IsLoopbackRequest()
        => HttpContext.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp);

    private string GetSessionId()
        => Request.Headers[SessionHeader].FirstOrDefault() ?? "default";
}
