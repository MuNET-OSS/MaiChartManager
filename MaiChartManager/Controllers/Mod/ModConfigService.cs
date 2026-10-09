using AquaMai.Config.HeadlessLoader;
using AquaMai.Config.Interfaces;
using MaiChartManager.Utils;

namespace MaiChartManager.Controllers.Mod;

public class ModConfigService
{
    private const string ExclusiveFullscreenSectionPath = "GameSystem.Window";
    private const string ExclusiveFullscreenEntryPath = "GameSystem.Window.ExclusiveFullscreen";
    private readonly MuModService _muModService;

    public ModConfigService(MuModService muModService)
    {
        _muModService = muModService;
    }

    public class UnsupportedConfigApiVersionException() : Exception(Locale.UnsupportedConfigVersion);

    public class ConfigCorruptedException() : Exception(Locale.AquaMaiConfigCorrupted);

    public class AquaMaiNotInstalledException() : Exception(Locale.AquaMaiNotInstalled);

    public class AquaMaiSignatureVerificationFailedException() : Exception("AquaMaiSignatureVerificationFailed");

    public void CheckConfigApiVersion(HeadlessConfigInterface configInterface)
    {
        var currentSupportedApiVersion = new Version(1, 1);
        var configApiVersion = new Version(configInterface.ApiVersion);
        if (currentSupportedApiVersion.Major != configApiVersion.Major)
        {
            throw new UnsupportedConfigApiVersionException();
        }

        if (currentSupportedApiVersion.Minor > configApiVersion.Minor)
        {
            throw new UnsupportedConfigApiVersionException();
        }
    }

    public async Task<string> GetAquaMaiDllPath(CancellationToken ct = default)
    {
        var muModInstalled = _muModService.IsMuModInstalled();
        var aquaMaiInstalled = File.Exists(ModPaths.AquaMaiDllInstalledPath);

        if (muModInstalled && !aquaMaiInstalled)
        {
            var cachePath = _muModService.GetResolvedCachePath();
            if (!File.Exists(cachePath))
            {
                // 缓存不存在，下载（DLL 不大，卡一下没事）
                await _muModService.EnsureCache(ct);
            }
            if (!File.Exists(cachePath))
            {
                throw new AquaMaiNotInstalledException();
            }

            return cachePath;
        }

        if (aquaMaiInstalled)
        {
            return ModPaths.AquaMaiDllInstalledPath;
        }

        throw new AquaMaiNotInstalledException();
    }

    /// <summary>
    /// 独占全屏跳过桌面合成，但前提是 Sinmai.exe 关掉了 Windows 的“全屏优化”，否则系统会用合成窗口模拟独占全屏，延迟反而比默认更高。
    /// 所以勾上这个选项时顺手把这个兼容性标记写上。
    /// 取消勾选时不做处理：不独占全屏的时候留着这个标记没有影响，何况用户可能本来就是自己在兼容性设置里勾的。
    /// </summary>
    public void SyncExclusiveFullscreenCompatFlag(IConfig config)
    {
        try
        {
            if (string.IsNullOrEmpty(StaticSettings.GamePath)) return;
            var exePath = Path.Combine(StaticSettings.GamePath, "Sinmai.exe");
            if (!File.Exists(exePath)) return;

            var entry = config.GetEntryState(ExclusiveFullscreenEntryPath);
            // 这个 AquaMai 版本还没这个选项，就不该由我们来碰这个设置
            if (entry?.Value is not true) return;

            // 选项所在的 section 没启用的话，这个选项也不会生效
            if (config.ReflectionManager.TryGetSection(ExclusiveFullscreenSectionPath, out var section)
                && !config.GetSectionState(section).Enabled) return;

            AppCompatFlags.AddFlag(exePath, AppCompatFlags.DisableFullscreenOptimizations);
        }
        catch (Exception e)
        {
            // 写注册表失败不该让保存配置跟着失败
            Console.WriteLine("同步全屏优化兼容性设置失败");
            Console.WriteLine(e);
        }
    }

    public async Task<IConfig> GetCurrentAquaMaiConfig(bool forceDefault = false, bool skipSignatureCheck = false, CancellationToken ct = default)
    {
        var dllPath = await GetAquaMaiDllPath(ct);

        var binary = await File.ReadAllBytesAsync(dllPath, ct);
        if (!skipSignatureCheck)
        {
            var sigResult = AquaMaiSignatureV2.VerifySignature(binary);
            if (sigResult.Status != AquaMaiSignatureV2.VerifyStatus.Valid)
            {
                throw new AquaMaiSignatureVerificationFailedException();
            }
        }
        var configInterface = HeadlessConfigLoader.LoadFromPacked(binary);
        var config = configInterface.CreateConfig();
        CheckConfigApiVersion(configInterface);
        if (File.Exists(ModPaths.AquaMaiConfigPath) && !forceDefault)
        {
            try
            {
                var view = configInterface.CreateConfigView(await File.ReadAllTextAsync(ModPaths.AquaMaiConfigPath, ct));
                var migrationManager = configInterface.GetConfigMigrationManager();

                if (migrationManager.GetVersion(view) != migrationManager.LatestVersion)
                {
                    Console.WriteLine("Migrating AquaMai config from {0} to {1}", migrationManager.GetVersion(view), migrationManager.LatestVersion);
                    view = migrationManager.Migrate(view);
                }

                var parser = configInterface.GetConfigParser();
                parser.Parse(config, view);
            }
            catch (Exception ex)
            {
                Console.WriteLine("无法加载 AquaMai 配置");
                Console.WriteLine(ex);
                if (ex.Message.Contains("Could not migrate the config"))
                {
                    // 这个应该是，AquaMai 未安装或需要更新
                    throw;
                }
                // 这个的提示是 AquaMai 配置文件损坏
                throw new ConfigCorruptedException();
            }
        }

        return config;
    }
}
