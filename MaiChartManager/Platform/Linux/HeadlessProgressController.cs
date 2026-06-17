using Microsoft.Extensions.Logging;

namespace MaiChartManager.Platform.Linux;

public class HeadlessProgressController(ILogger<HeadlessProgressController> logger) : IProgressController
{
    public IProgressSession Begin(string title, string description, string cancelMessage)
    {
        logger.LogInformation("{title}: {description}", title, description);
        return new HeadlessProgressSession(logger);
    }
}

public sealed class HeadlessProgressSession(ILogger logger) : IProgressSession
{
    public void Report(ulong value, ulong total, string? detail = null)
    {
        if (detail is not null)
            logger.LogInformation("Progress {value}/{total}: {detail}", value, total, detail);
    }

    public bool IsCancelled => false;
    public void Dispose() { }
}
