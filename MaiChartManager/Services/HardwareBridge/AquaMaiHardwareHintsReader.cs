using MaiChartManager.Controllers.Mod;
using Tomlyn;
using Tomlyn.Model;

namespace MaiChartManager.Services.HardwareBridge;

public class AquaMaiHardwareHintsReader(ILogger<AquaMaiHardwareHintsReader> logger)
{
    public AquaMaiHints? ReadHints()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(StaticSettings.GamePath))
                return null;

            var configPath = ModPaths.AquaMaiConfigPath;
            if (!File.Exists(configPath))
                return null;

            var tomlText = File.ReadAllText(configPath);
            var root = Toml.ToModel(tomlText);
            if (!TryGetTable(root, "GameSystem", out var gameSystem) || gameSystem == null)
                return null;

            string? touchPort1p = null;
            string? touchPort2p = null;
            if (IsSectionEnabled(gameSystem, "TouchPanelPort", out var touchSection) == true && touchSection != null)
            {
                touchPort1p = GetString(touchSection, "portName_1P") ?? "COM3";
                touchPort2p = GetString(touchSection, "portName_2P") ?? "COM4";
            }

            int? touchBaudRate = null;
            if (IsSectionEnabled(gameSystem, "TouchPanelBaudRate", out var baudSection) == true && baudSection != null)
            {
                touchBaudRate = GetInt(baudSection, "baudRate");
            }

            string? ledPort1p = null;
            string? ledPort2p = null;
            if (IsSectionEnabled(gameSystem, "LightBoardPort", out var ledSection) == true && ledSection != null)
            {
                ledPort1p = GetString(ledSection, "portName_1PLED") ?? "COM21";
                ledPort2p = GetString(ledSection, "portName_2PLED") ?? "COM23";
            }

            var adxEnabled = IsSectionEnabled(gameSystem, "AdxHidInput", out _);
            var mmlEnabled = IsSectionEnabled(gameSystem, "MaimollerIO", out _);

            string? pdxPath1p = null;
            string? pdxPath2p = null;
            var pdxEnabled = IsSectionEnabled(gameSystem, "PdxTouch", out var pdxSection);
            if (pdxEnabled == true && pdxSection != null)
            {
                pdxPath1p = GetString(pdxSection, "path1p");
                pdxPath2p = GetString(pdxSection, "path2p");
            }

            return new AquaMaiHints(
                TouchPort1p: touchPort1p,
                TouchPort2p: touchPort2p,
                TouchBaudRate: touchBaudRate,
                LedPort1p: ledPort1p,
                LedPort2p: ledPort2p,
                AdxHidEnabled: adxEnabled,
                MaimollerEnabled: mmlEnabled,
                PdxTouchEnabled: pdxEnabled,
                PdxPath1p: pdxPath1p,
                PdxPath2p: pdxPath2p);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取 AquaMai.toml 硬件配置提示失败");
            return null;
        }
    }

    private static bool TryGetValue(TomlTable table, string key, out object? value)
    {
        foreach (var kv in table)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static bool TryGetTable(TomlTable table, string key, out TomlTable? subTable)
    {
        if (TryGetValue(table, key, out var value) && value is TomlTable t)
        {
            subTable = t;
            return true;
        }
        subTable = null;
        return false;
    }

    private static bool? IsSectionEnabled(TomlTable parent, string sectionKey, out TomlTable? sectionTable)
    {
        sectionTable = null;
        if (!TryGetValue(parent, sectionKey, out var value) || value == null)
            return null;

        if (value is TomlTable table)
        {
            sectionTable = table;
            if (TryGetValue(table, "Disabled", out var disabledVal) && TryParseBool(disabledVal, out var disabled))
            {
                return !disabled;
            }
            return true;
        }

        if (TryParseBool(value, out var enabled))
            return enabled;

        return null;
    }

    private static bool TryParseBool(object? value, out bool result)
    {
        switch (value)
        {
            case bool b:
                result = b;
                return true;
            case string s when bool.TryParse(s, out var parsed):
                result = parsed;
                return true;
            case long l:
                result = l != 0;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static string? GetString(TomlTable table, string key)
    {
        if (TryGetValue(table, key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
            return s.Trim();
        return null;
    }

    private static int? GetInt(TomlTable table, string key)
    {
        if (!TryGetValue(table, key, out var val) || val == null) return null;
        return val switch
        {
            long l when l is > 0 and <= int.MaxValue => (int)l,
            int i when i > 0 => i,
            string s when int.TryParse(s, out var parsed) && parsed > 0 => parsed,
            _ => null,
        };
    }
}
