using System.Net;
using MaiChartManager.Platform;
using MaiChartManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Tools;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class ResourceJunctionController(ResourceJunctionService service, IDesktopDialogService dialogService) : ControllerBase
{
    private const string LocalActionHeader = "X-MCM-Local-Action";
    private const string LocalActionValue = "resource-junction";

    [HttpGet]
    public ActionResult<ResourceJunctionOverview> GetResourceJunctionStatus()
    {
        if (!IsLoopbackRequest() || StaticSettings.Config.Export)
            return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(service.GetOverview());
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> AutoSelectResourceJunctionSource()
    {
        if (RejectUnavailableLocalAction() is { } rejection) return rejection;
        return Ok(service.AutoSelectSource());
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> SelectResourceJunctionSource()
    {
        if (RejectUnavailableLocalAction() is { } rejection) return rejection;

        var path = dialogService.PickFolder(
            Locale.ResourceManager.GetString("SelectResourceJunctionSourceFolder", Locale.Culture));
        if (path is null) return Ok(service.GetOverview());
        try
        {
            return Ok(service.SelectManualSource(path));
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
        if (RejectUnavailableLocalAction() is { } rejection) return rejection;

        var path = dialogService.PickFolder(
            Locale.ResourceManager.GetString("SelectResourceJunctionTargetFolder", Locale.Culture));
        if (path is null) return Ok(service.GetOverview());
        try
        {
            return Ok(service.SelectManualTarget(path));
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> CreateResourceJunctions()
    {
        if (RejectUnavailableLocalAction() is { } rejection) return rejection;
        var items = service.CreateLinks();
        return Ok(service.GetOverview() with { Items = items });
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> RemoveResourceJunctions()
    {
        if (RejectUnavailableLocalAction() is { } rejection) return rejection;
        var items = service.RemoveLinks();
        return Ok(service.GetOverview() with { Items = items });
    }

    private ActionResult? RejectUnavailableLocalAction()
    {
        if (!IsLoopbackRequest() || StaticSettings.Config.Export)
            return StatusCode(StatusCodes.Status403Forbidden);
        return Request.Headers[LocalActionHeader] != LocalActionValue ? BadRequest() : null;
    }

    private bool IsLoopbackRequest()
        => HttpContext.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp);
}
