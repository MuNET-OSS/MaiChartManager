using System.Net;
using MaiChartManager.Controllers.Tools;
using MaiChartManager.Platform;
using MaiChartManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Tests.Controllers.Tools;

[Collection("Static settings")]
public sealed class ResourceJunctionControllerTests
{
    [Fact]
    public void StatusRejectsRemoteRequests()
    {
        var controller = CreateController(IPAddress.Parse("192.0.2.1"));

        var result = controller.GetResourceJunctionStatus();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public void WriteActionRejectsRemoteRequestsBeforeUsingDesktopCapabilities()
    {
        var dialogService = new RecordingDialogService();
        var controller = CreateController(IPAddress.Parse("192.0.2.1"), dialogService);
        var result = controller.SelectResourceJunctionSource();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        Assert.Equal(0, dialogService.PickFolderCalls);
    }

    [Fact]
    public void LocalWriteActionDoesNotRequireAnAuthenticationHeader()
    {
        var dialogService = new RecordingDialogService();
        var controller = CreateController(IPAddress.Loopback, dialogService);

        var result = controller.SelectResourceJunctionSource();

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, dialogService.PickFolderCalls);
    }

    [Fact]
    public void LinuxUnsupportedStatusDoesNotOpenFolderPicker()
    {
        if (OperatingSystem.IsWindows()) return;
        var dialogService = new RecordingDialogService();
        var controller = CreateController(IPAddress.Loopback, dialogService);

        var result = controller.SelectResourceJunctionSource();

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(0, dialogService.PickFolderCalls);
    }

    private static ResourceJunctionController CreateController(
        IPAddress remoteAddress,
        RecordingDialogService? dialogService = null)
    {
        StaticSettings.Config = new Config();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        return new ResourceJunctionController(
            new ResourceJunctionService(() => string.Empty, () => []),
            dialogService ?? new RecordingDialogService())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private sealed class RecordingDialogService : IDesktopDialogService
    {
        public int PickFolderCalls { get; private set; }

        public string? PickFolder(string? title = null)
        {
            PickFolderCalls++;
            return null;
        }

        public string? PickFile(string? title = null, string? filter = null) => null;
        public bool Confirm(string message, string title, bool defaultResult = false) => defaultResult;
        public void ShowError(string message, string title) { }
    }

}
