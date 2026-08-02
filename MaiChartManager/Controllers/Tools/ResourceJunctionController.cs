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
        if (StaticSettings.Config.Export) return Forbid();
        return Ok(service.GetOverview());
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> AutoSelectResourceJunctionSource()
    {
        if (StaticSettings.Config.Export) return Forbid();
        if (Request.Headers[LocalActionHeader] != LocalActionValue) return BadRequest();
        return Ok(service.AutoSelectSource());
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> SelectResourceJunctionSource()
    {
        if (StaticSettings.Config.Export) return Forbid();
        if (Request.Headers[LocalActionHeader] != LocalActionValue) return BadRequest();

        var path = dialogService.PickFolder("Select a source game directory or Package directory");
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
        if (StaticSettings.Config.Export) return Forbid();
        if (Request.Headers[LocalActionHeader] != LocalActionValue) return BadRequest();

        var path = dialogService.PickFolder("Select a target game directory or Package directory");
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
        if (StaticSettings.Config.Export) return Forbid();
        if (Request.Headers[LocalActionHeader] != LocalActionValue) return BadRequest();
        var items = service.CreateLinks();
        return Ok(service.GetOverview() with { Items = items });
    }

    [HttpPost]
    public ActionResult<ResourceJunctionOverview> RemoveResourceJunctions()
    {
        if (StaticSettings.Config.Export) return Forbid();
        if (Request.Headers[LocalActionHeader] != LocalActionValue) return BadRequest();
        var items = service.RemoveLinks();
        return Ok(service.GetOverview() with { Items = items });
    }
}
