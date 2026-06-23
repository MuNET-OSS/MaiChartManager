using Spectre.Console.Cli;

namespace MaiChartManager.CLI.Commands;

public class DebugCommand : Command
{
    public override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
#if WINDOWS
        // WebView2 (COM) 要求 STA 线程；CLI 的 async 入口运行在 MTA 上下文中，
        // 而 Program.Main 上的 [STAThread] 作为普通方法调用时不生效，需手动建 STA 线程
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                Program.Main();
            }
            catch (Exception e)
            {
                exception = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            Console.Error.WriteLine($"发生错误: {exception}");
            throw exception;
        }
        return 0;
#else
        // Linux：直接启动 Photino 主程序（无 COM/STA 限制），控制台可见日志
        MaiChartManager.LinuxProgram.Main([]);
        return 0;
#endif
    }
}
