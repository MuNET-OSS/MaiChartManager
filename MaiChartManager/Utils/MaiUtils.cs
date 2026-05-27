using System.Text.RegularExpressions;

namespace MaiChartManager.Utils;

public static partial class MaiUtils
{
    [GeneratedRegex(@"^-?\d+(\.\d+)?")]
    private static partial Regex LeadingFloatRegex();
    
    // 尝试解析字符串开头的浮点数部分，忽略后续无法解析的字符（如 "13?" -> 13）。
    public static bool ParseLevelStr(string? input, out float value, out string utageKanji)
    {
        utageKanji = "";
        value = 0;
        input = input?.Trim();
        if (string.IsNullOrWhiteSpace(input)) return false;
        
        if (!char.IsDigit(input[0]))
        { // 如果不以数字开头，说明是utageKanji的情况
            utageKanji = input.Substring(0, 1);
            input = input.Substring(1);
        }
        input = input.Replace("+", ".7");
        
        var match = LeadingFloatRegex().Match(input); // 结尾有可能会有一个?，或者是其他的什么东西。为了让这些东西不要影响处理，所以通过正则匹配开头的数字部分，不然如果直接送进float.TryParse的话，类似"13?"这种的就解析不出来、返回false了。
        if (!match.Success) return false;
        return float.TryParse(match.Value, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    public static int GetLevelId(int levelX10)
    {
        return levelX10 switch
        {
            >= 156 => 24,
            >= 150 => 23,
            >= 146 => 22,
            >= 140 => 21,
            >= 136 => 20,
            >= 130 => 19,
            >= 126 => 18,
            >= 120 => 17,
            >= 116 => 16,
            >= 110 => 15,
            >= 106 => 14,
            >= 100 => 13,
            >= 96 => 12,
            >= 90 => 11,
            >= 86 => 10,
            >= 80 => 9,
            >= 76 => 8,
            >= 0 => levelX10 / 10,
            _ => 0
        };
    }
}