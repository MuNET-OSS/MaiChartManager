using System.Text;
using MaiChartManager;
using MaiChartManager.CLI.Commands;
using MaiChartManager.CLI.Utils;
using Spectre.Console.Cli;

Console.OutputEncoding = Encoding.UTF8;
Console.CancelKeyPress += (_, _) => TerminalProgress.Clear();

try
{
    SentrySdk.Init(o =>
    {
        // Tells which project in Sentry to send events to:
        o.Dsn = "https://be7a9ae3a9a88f4660737b25894b3c20@sentry.c5y.moe/3";
        // Set TracesSampleRate to 1.0 to capture 100% of transactions for tracing.
        // We recommend adjusting this value in production.
        o.TracesSampleRate = 0.5;
#if DEBUG
        o.Environment = "development";
#endif
    });

    AppMain.InitConfiguration(true);
    await IapManager.Init();

    var app = new CommandApp();

    app.Configure(config =>
    {
        config.SetApplicationName("mcm");

        config.AddCommand<MakeUsmCommand>("makeusm")
            .WithDescription("将视频文件转换为 USM 格式")
            .WithExample("makeusm", "video.mp4")
            .WithExample("makeusm", "video.mp4", "-O", "output.dat")
            .WithExample("makeusm", "video1.mp4", "video2.mp4", "video3.mp4");

        config.AddCommand<MakeMp4Command>("makemp4")
            .WithDescription("将 USM/DAT 文件转换为 MP4 格式")
            .WithExample("makemp4", "movie.dat")
            .WithExample("makemp4", "movie.dat", "-O", "output.mp4")
            .WithExample("makemp4", "movie1.dat", "movie2.dat", "movie3.dat");

        config.AddCommand<MakeAcbCommand>("makeacb")
            .WithDescription("将音频文件转换为 ACB 格式")
            .WithExample("makeacb", "audio.wav")
            .WithExample("makeacb", "audio.mp3", "-O", "output.acb")
            .WithExample("makeacb", "audio1.wav", "audio2.mp3", "--padding", "0.5");

        config.AddCommand<MakeAbCommand>("makeab")
            .WithDescription("将文件夹中的封面图片批量转换为 AssetBundle 格式")
            .WithExample("makeab", "D:\\images");

        config.AddCommand<DebugCommand>("debug")
            .WithDescription("以控制台模式启动主程序，用于查看日志输出");
    });

    return await app.RunAsync(args);
}
catch (Exception e)
{
    SentrySdk.CaptureException(e);
    Console.Error.WriteLine($"发生错误: {e.Message}");
    return 1;
}