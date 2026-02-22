using System.Globalization;

namespace MaiChartManager.WannaCRI;

public static class WannaCRI
{
    private const string DefaultKey = "0x7F4551499DF55E68";

    public static void CreateUsm(string src, string key = DefaultKey)
    {
        CreateUsm(src, output: null, key);
    }

    public static void CreateUsm(string src, string? output, string key = DefaultKey)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            throw new ArgumentException("Input path is required.", nameof(src));
        }

        if (!File.Exists(src))
        {
            throw new FileNotFoundException("Input file not found.", src);
        }

        if (!TryParseHexKey(key, out var keyValue))
        {
            throw new ArgumentException($"Invalid key format: {key}", nameof(key));
        }

        UsmCreator.Create(src, output, keyValue);
    }

    public static void UnpackUsm(string src, string output, string key = DefaultKey)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            throw new ArgumentException("Input path is required.", nameof(src));
        }

        if (!File.Exists(src))
        {
            throw new FileNotFoundException("USM input file not found.", src);
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("Output directory is required.", nameof(output));
        }

        Directory.CreateDirectory(output);

        if (!TryParseHexKey(key, out var keyValue))
        {
            throw new ArgumentException($"Invalid key format: {key}", nameof(key));
        }

        UsmExtractor.Extract(src, output, keyValue);
    }

    private static bool TryParseHexKey(string key, out ulong keyValue)
    {
        var normalized = key.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return ulong.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out keyValue);
    }
}