using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AquaMai.Config.HeadlessLoader;
using AquaMai.Config.Interfaces;
using MaiChartManager.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Mono.Cecil;
using YamlDotNet.Serialization;

namespace MaiChartManager.Controllers;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class ModController(StaticSettings settings, ILogger<ModController> logger) : ControllerBase
{
    private static string judgeDisplay4BPath = Path.Combine(StaticSettings.exeDir, "Resources", "JudgeDisplay4B");

    [HttpGet]
    public bool IsMelonInstalled()
    {
        return System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, "version.dll"))
            && Directory.Exists(Path.Combine(StaticSettings.GamePath, "MelonLoader"));
    }

    [HttpGet]
    public bool IsAquaMaiInstalled()
    {
        return System.IO.File.Exists(AquaMaiDllInstalledPath);
    }

    public record GameModInfo(
        bool MelonLoaderInstalled,
        bool AquaMaiInstalled,
        string AquaMaiVersion,
        string BundledAquaMaiVersion,
        bool IsJudgeDisplay4BInstalled,
        bool IsHidConflictExist
    );

    private static string AquaMaiConfigPath => Path.Combine(StaticSettings.GamePath, "AquaMai.toml");
    private static string AquaMaiConfigBackupDirPath => Path.Combine(StaticSettings.GamePath, "AquaMai.toml.bak");
    private static string AquaMaiDllInstalledPath => Path.Combine(StaticSettings.GamePath, @"Mods\AquaMai.dll");
    private static string AquaMaiDllBuiltinPath => Path.Combine(StaticSettings.exeDir, "AquaMai.dll");
    private static readonly string[] HidModPaths = [@"Mods\Mai2InputMod.dll", @"Mods\hid_input_lib.dll", "hid_input_lib.dll", "mai2io.dll"];

    [HttpGet]
    public GameModInfo GetGameModInfo()
    {
        var aquaMaiInstalled = IsAquaMaiInstalled();
        var aquaMaiVersion = "N/A";
        if (aquaMaiInstalled)
        {
            aquaMaiVersion = FileVersionInfo.GetVersionInfo(AquaMaiDllInstalledPath).ProductVersion ?? "N/A";
        }

        var aquaMaiBuiltinVersion = FileVersionInfo.GetVersionInfo(AquaMaiDllBuiltinPath).ProductVersion;

        return new GameModInfo(IsMelonInstalled(), aquaMaiInstalled, aquaMaiVersion, aquaMaiBuiltinVersion!, GetIsJudgeDisplay4BInstalled(), GetIsHidConflictExist());
    }

    [NonAction]
    private static bool GetIsJudgeDisplay4BInstalled()
    {
        if (!Directory.Exists(StaticSettings.SkinAssetsDir)) return false;

        var filesShouldBeInstalled = Directory.EnumerateFiles(judgeDisplay4BPath);
        return filesShouldBeInstalled.Select(file => Path.Combine(StaticSettings.SkinAssetsDir, Path.GetFileName(file))).All(System.IO.File.Exists);
    }

    [NonAction]
    private static bool GetIsHidConflictExist()
    {
        foreach (var mod in HidModPaths)
        {
            if (System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, mod)))
            {
                return true;
            }
        }

        return false;
    }

    [HttpPost]
    public void DeleteHidConflict()
    {
        foreach (var mod in HidModPaths)
        {
            if (System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, mod)))
            {
                FileSystem.DeleteFile(Path.Combine(StaticSettings.GamePath, mod), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
        }
    }

    [HttpPost]
    public void InstallJudgeDisplay4B()
    {
        Directory.CreateDirectory(StaticSettings.SkinAssetsDir);

        foreach (var file in Directory.EnumerateFiles(judgeDisplay4BPath))
        {
            System.IO.File.Copy(file, Path.Combine(StaticSettings.SkinAssetsDir, Path.GetFileName(file)), true);
        }
    }

    public class UnsupportedConfigApiVersionException() : Exception("无法兼容的配置文件版本");

    public class AquaMaiNotInstalledException() : Exception("AquaMai 没有安装");

    [NonAction]
    private static void CheckConfigApiVersion(HeadlessConfigInterface configInterface)
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

    [NonAction]
    public static IConfig GetCurrentAquaMaiConfig()
    {
        if (!System.IO.File.Exists(AquaMaiDllInstalledPath))
        {
            throw new AquaMaiNotInstalledException();
        }

        var configInterface = HeadlessConfigLoader.LoadFromPacked(AquaMaiDllInstalledPath);
        var config = configInterface.CreateConfig();
        CheckConfigApiVersion(configInterface);
        if (System.IO.File.Exists(AquaMaiConfigPath))
        {
            var view = configInterface.CreateConfigView(System.IO.File.ReadAllText(AquaMaiConfigPath));
            var migrationManager = configInterface.GetConfigMigrationManager();

            if (migrationManager.GetVersion(view) != migrationManager.LatestVersion)
            {
                Console.WriteLine("Migrating AquaMai config from {0} to {1}", migrationManager.GetVersion(view), migrationManager.LatestVersion);
                view = migrationManager.Migrate(view);
            }

            var parser = configInterface.GetConfigParser();
            parser.Parse(config, view);
        }

        return config;
    }

    [HttpGet]
    public AquaMaiConfigDto.ConfigDto GetAquaMaiConfig()
    {
        Dictionary<string, string[]>? configSort = null;
        using (var stream = new FileStream(AquaMaiDllInstalledPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var asm = ModuleDefinition.ReadModule(stream);
            var configSortRes = asm.Resources.FirstOrDefault(it => it is EmbeddedResource { Name: "configSort.yaml.compressed" } or EmbeddedResource { Name: "configSort.yaml" });
            if (configSortRes != null)
            {
                var (name, res) = ResourceLoader.LoadResource(configSortRes);
                var deserializer = new DeserializerBuilder().Build();
                var yaml = new StreamReader(res).ReadToEnd();
                configSort = deserializer.Deserialize<Dictionary<string, string[]>>(yaml);
            }
        }
        var config = GetCurrentAquaMaiConfig();
        // 未解之谜
        // logger.LogInformation("{}", lockCredits.GetType());
        // var type = typeof(ModController).GetField("lockCredits", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        // logger.LogInformation("{}", type.GetType());
        // logger.LogInformation("{}", AquaMaiDllInstalledPath);
        // logger.LogInformation("{}", config.GetEntryState(config.ReflectionManager.Entries.First(it => it.Path == "GameSettings.CreditConfig.LockCredits")).DefaultValue.GetType());
        // logger.LogInformation("{}", config.GetEntryState(config.ReflectionManager.Entries.First(it => it.Path == "GameSettings.CreditConfig.LockCredits")).Value.GetType());
        // logger.LogInformation("{}", config.ReflectionManager.Entries.First(it => it.Path == "GameSettings.CreditConfig.LockCredits").Field.FieldType);
        //
        // logger.LogInformation("{}", config.GetEntryState(config.ReflectionManager.Entries.First(it => it.Path == "GameSettings.CreditConfig.LockCredits")).Value.GetType());
        // logger.LogInformation("{}", config.ReflectionManager.Entries.First(it => it.Path == "GameSettings.CreditConfig.LockCredits").Field.FieldType);

        return new AquaMaiConfigDto.ConfigDto(
            config.ReflectionManager.Sections.Select(section =>
            {
                var entries = section.Entries.Select(entry => new AquaMaiConfigDto.Entry(entry.Path, entry.Name, entry.Attribute, entry.Field.FieldType.FullName ?? entry.Field.FieldType.Name));
                return new AquaMaiConfigDto.Section(section.Path, entries, section.Attribute);
            }),
            config.ReflectionManager.Sections.ToDictionary(section => section.Path, section => config.GetSectionState(section)),
            config.ReflectionManager.Entries.ToDictionary(entry => entry.Path, entry => config.GetEntryState(entry)),
            configSort
        );
    }

    [HttpPut]
    public void SetAquaMaiConfig(AquaMaiConfigDto.ConfigSaveDto config)
    {
        var jsonOptions = new JsonSerializerOptions();
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var configInterface = HeadlessConfigLoader.LoadFromPacked(AquaMaiDllInstalledPath);
        CheckConfigApiVersion(configInterface);
        var configEdit = configInterface.CreateConfig();

        foreach (var section in configEdit.ReflectionManager.Sections)
        {
            var newState = config.SectionStates[section.Path];
            var oldState = configEdit.GetSectionState(section);
            if (newState.Enabled != oldState.Enabled)
                configEdit.SetSectionEnabled(section, newState.Enabled);

            // 看起来现在如果禁用了 section 并且改了选项，选项会被写到上一个 section 里面
            // 两个 section 有名字一样的选项怕不是会出问题
            if (!newState.Enabled) continue;

            foreach (var entry in section.Entries)
            {
                var newEntryState = config.EntryStates[entry.Path];
                var oldEntryState = configEdit.GetEntryState(entry);
                var newEntryValue = newEntryState.Value.Deserialize(entry.Field.FieldType, jsonOptions);
                if (!oldEntryState.Value.Equals(newEntryValue))
                {
                    // logger.LogInformation("Not same: {Path}, {type1}, {newEntryValue}, {type2}, {oldEntryState}", entry.Path, newEntryValue?.GetType(), newEntryValue, oldEntryState.Value?.GetType(),
                    //     oldEntryState.Value);
                    configEdit.SetEntryValue(entry, newEntryValue);
                }
                else if (!newEntryState.IsDefault)
                {
                    // logger.LogInformation("Not default: {Path}", entry.Path);
                    configEdit.SetEntryValue(entry, newEntryValue);
                }
            }
        }

        StaticSettings.UpdateAssetPathsFromAquaMaiConfig(configEdit);
        // 可能修改了歌曲封面目录
        settings.ScanMusicList();
        var serializer = configInterface.CreateConfigSerializer(new IConfigSerializer.Options()
        {
            Lang = "zh",
            IncludeBanner = true
        });

        if (System.IO.File.Exists(AquaMaiConfigPath))
        {
            Directory.CreateDirectory(AquaMaiConfigBackupDirPath);
            var backupPath = Path.Combine(AquaMaiConfigBackupDirPath, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.toml");
            var originalContent = System.IO.File.ReadAllBytes(AquaMaiConfigPath);
            System.IO.File.WriteAllBytes(backupPath, originalContent);
        }

        System.IO.File.WriteAllText(Path.Combine(StaticSettings.GamePath, "AquaMai.toml"), serializer.Serialize(configEdit));
    }

    [HttpPost]
    public void InstallMelonLoader()
    {
        if (IsMelonInstalled())
        {
            logger.LogInformation("MelonLoader is already installed.");
            return;
        }

        using var s = System.IO.File.OpenRead(Path.Combine(StaticSettings.exeDir, "MelonLoader.x64.zip"));
        var zip = new ZipArchive(s, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name == "NOTICE.txt")
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(StaticSettings.GamePath, entry.FullName)));
            entry.ExtractToFile(Path.Combine(StaticSettings.GamePath, entry.FullName), true);
        }
    }

    [HttpPost]
    public void InstallAquaMai()
    {
        var src = Path.Combine(StaticSettings.exeDir, "AquaMai.dll");
        var dest = Path.Combine(StaticSettings.GamePath, @"Mods\AquaMai.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dest));
        FileSystem.CopyFile(src, dest, true);
    }

    [HttpPost]
    public void OpenJudgeAccuracyInfoPdf()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(StaticSettings.exeDir, "Judge Accuracy Info 功能简介.pdf"),
            UseShellExecute = true
        });
    }

    private const string CI_KEY =
        "MIGbMBAGByqGSM49AgEGBSuBBAAjA4GGAAQBmXJFYcg9YMi5i7tcPxc29Lxb/8OwGBUA5zi0cN4Apvp/J+zBaeY7EB9clDcRh8sjXRgSc7/JXYrUn8LDPHoCuuMB2W6HAxV+NtQamITaUNJD89LM0NpubbTUeKYjUThXpr/otDWq8kk5hvwkk62ByqG/2EXnH6WhlrI81I9H4l5adi4=";

    private const string RELEASE_KEY =
        "MIGbMBAGByqGSM49AgEGBSuBBAAjA4GGAAQBysJuWwvK1gK0Da9fnQgaiJpXH495QGEdGW/l3fj9g5X9v7TZCBPetUM7zDbMyMxLL+G4R1KQYvxcfKyvJz0h/woBkQ0nznNTCrkdBiB0xsNQrCBf8DDAJVw9w8X01rQeSxcnOZi2KBzixeMZZEqWnGtU/f3kDThWmRPaZ8Ptz8KynUU=";

    [NonAction]
    private static bool VerifyBinary(byte[] data, string sign, string key)
    {
        byte[] signature = Convert.FromBase64String(sign);
        byte[] publicKey = Convert.FromBase64String(key);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    public record InstallAquaMaiOnlineDto(string[] Urls, string Type, string Sign);

    [HttpPost]
    public async Task InstallAquaMaiOnline(InstallAquaMaiOnlineDto req)
    {
        var key = req.Type switch
        {
            "ci" => CI_KEY,
            "release" => RELEASE_KEY,
            _ => throw new ArgumentException("Invalid type", nameof(req.Type)),
        };
        // Download from url
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        Exception? lastException = null;
        foreach (var url in req.Urls)
        {
            byte[] data;
            try
            {
                data = await client.GetByteArrayAsync(url);

                if (!VerifyBinary(data, req.Sign, key))
                {
                    throw new InvalidOperationException("Invalid signature");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to download AquaMai from {Url}", url);
                lastException = e;
                continue;
            }
            // Save to Mods folder
            var dest = Path.Combine(StaticSettings.GamePath, @"Mods\AquaMai.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            await System.IO.File.WriteAllBytesAsync(dest, data);
            return;
        }
        throw new InvalidOperationException("Failed to download AquaMai from all urls", lastException);
    }

    [HttpPost]
    public void KillGameProcess()
    {
        foreach (var process in Process.GetProcessesByName("Sinmai"))
        {
            try
            {
                process.Kill();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to kill Sinmai process with id {Id}", process.Id);
            }
        }
    }
}