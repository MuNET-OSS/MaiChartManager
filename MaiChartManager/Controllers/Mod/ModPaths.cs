namespace MaiChartManager.Controllers.Mod;

public static class ModPaths
{
    public static string AquaMaiConfigPath => Path.Combine(StaticSettings.GamePath, "AquaMai.toml");
    public static string AquaMaiConfigBackupDirPath => Path.Combine(StaticSettings.GamePath, "AquaMai.toml.bak");
    public static string AquaMaiDllInstalledPath => Path.Combine(StaticSettings.GamePath, "Mods", "AquaMai.dll");

    public static string MuModDllInstalledPath => Path.Combine(StaticSettings.GamePath, "Mods", "MuMod.dll");
    public static string MuModDllBuiltinPath => Path.Combine(StaticSettings.exeDir, "MuMod.dll");
    public static string MuModConfigPath => Path.Combine(StaticSettings.GamePath, "MuMod.toml");
    public static string MuModDefaultCachePath => Path.Combine(StaticSettings.GamePath, "LocalAssets", "MuMod.cache");
}