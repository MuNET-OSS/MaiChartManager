using Microsoft.Win32;

namespace MaiChartManager.Utils;

/// <summary>
/// 给程序加 Windows 兼容性标记（HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers），
/// 也就是 Explorer 里兼容性选项卡改的那个地方。值名是 exe 的完整路径，数据是一串空格分隔的标记，
/// 形如 "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE"，所以这里只加上自己的标记，其他标记原样保留。
/// 改完立即对新启动的进程生效。
/// </summary>
public static class AppCompatFlags
{
    private const string LayersKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private static readonly char[] FlagSeparators = [' ', '\t', '\r', '\n'];

    /// <summary>“禁用全屏优化”对应的标记</summary>
    public const string DisableFullscreenOptimizations = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    public static void AddFlag(string exePath, string flag)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Registry.CurrentUser.CreateSubKey(LayersKeyPath);
        if (key is null) return;

        // 值名就是 exe 路径，注册表本身不区分大小写，这里也按不区分来找，避免同一个 exe 写出两条
        var valueName = key.GetValueNames().FirstOrDefault(it => string.Equals(it, exePath, StringComparison.OrdinalIgnoreCase));
        var raw = valueName is null ? null : key.GetValue(valueName) as string;
        var flags = ParseFlags(raw).ToList();
        if (flags.Contains(flag, StringComparer.OrdinalIgnoreCase)) return;
        flags.Add(flag);

        // 手动写进注册表的值可能没有 "~" 前缀，保持它原来的写法
        var tilde = raw is null || raw.Contains('~') ? "~ " : "";
        key.SetValue(valueName ?? exePath, tilde + string.Join(' ', flags), RegistryValueKind.String);
    }

    private static IEnumerable<string> ParseFlags(string? raw)
        => (raw ?? "").Split(FlagSeparators, StringSplitOptions.RemoveEmptyEntries).Where(it => it != "~");
}
