using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using MaiChartManager.Platform;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Mod;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class InstallationController(StaticSettings settings, ILogger<InstallationController> logger, MuModService muModService) : ControllerBase
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
        return System.IO.File.Exists(ModPaths.AquaMaiDllInstalledPath);
    }

    public record GameModInfo(
        bool MelonLoaderInstalled,
        bool AquaMaiInstalled,
        string AquaMaiVersion,
        bool IsJudgeDisplay4BInstalled,
        bool IsHidConflictExist,
        AquaMaiSignatureV2.VerifyResult? Signature,
        bool IsAdxHidIoModAbsent,
        bool IsMmlLegacyLibsInstalled,
        bool MuModInstalled,
        string? MuModVersion,
        string? MuModChannel,
        string? MuModCacheVersion,
        bool IsBothModsPresent
    );

    [HttpGet]
    public GameModInfo GetGameModInfo()
    {
        var aquaMaiInstalled = IsAquaMaiInstalled();
        var aquaMaiVersion = "N/A";
        if (aquaMaiInstalled)
        {
            aquaMaiVersion = FileVersionInfo.GetVersionInfo(ModPaths.AquaMaiDllInstalledPath).ProductVersion ?? "N/A";
        }

        var muModInstalled = muModService.IsMuModInstalled();

        AquaMaiSignatureV2.VerifyResult? sig = null;
        if (aquaMaiInstalled)
        {
            sig = AquaMaiSignatureV2.VerifySignature(System.IO.File.ReadAllBytes(ModPaths.AquaMaiDllInstalledPath));
        }
        else if (muModInstalled)
        {
            sig = AquaMaiSignatureV2.VerifySignature(System.IO.File.ReadAllBytes(ModPaths.MuModDllInstalledPath));
        }
        var muModVersion = muModInstalled ? muModService.GetMuModVersion() : null;
        string? muModChannel = null;
        string? muModCacheVersion = null;
        if (muModInstalled)
        {
            try { muModChannel = muModService.ReadConfig().Channel; } catch { }
            muModCacheVersion = muModService.GetCacheInfo();
        }

        return new GameModInfo(
            IsMelonInstalled(),
            aquaMaiInstalled,
            aquaMaiVersion,
            GetIsJudgeDisplay4BInstalled(),
            GetIsHidConflictExist(),
            sig,
            GetIsAdxHidIoModAbsent(),
            GetIsMmlLegacyLibsInstalled(),
            muModInstalled,
            muModVersion,
            muModChannel,
            muModCacheVersion,
            aquaMaiInstalled && muModInstalled
        );
    }

    [NonAction]
    private static bool GetIsJudgeDisplay4BInstalled()
    {
        if (!Directory.Exists(StaticSettings.SkinAssetsDir)) return false;

        var filesShouldBeInstalled = Directory.EnumerateFiles(judgeDisplay4BPath);
        return filesShouldBeInstalled.Select(file => Path.Combine(StaticSettings.SkinAssetsDir, Path.GetFileName(file))).All(System.IO.File.Exists);
    }

    #region ADX HID 冲突检测和删除

    private static readonly string[] HidModPaths = [Path.Combine("Mods", "Mai2InputMod.dll"), Path.Combine("Mods", "hid_input_lib.dll"), "hid_input_lib.dll", "mai2io.dll"];

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
                PlatformFile.DeleteFile(Path.Combine(StaticSettings.GamePath, mod));
            }
        }
    }

    #endregion

    [NonAction]
    private static bool GetIsAdxHidIoModAbsent()
    {
        return !System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, "Mods", "ADXHIDIOMod.dll"));
    }

    [NonAction]
    private static bool GetIsMmlLegacyLibsInstalled()
    {
        if (!System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, "Sinmai_Data", "Plugins", "hidapi.dll")))
        {
            return false;
        }
        if (!System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, "Sinmai_Data", "Plugins", "libadxhid.dll")))
        {
            return false;
        }

        return true;
    }

    private static void CopyFile(string src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var read = System.IO.File.OpenRead(src);
        using var write = System.IO.File.Open(dest, FileMode.Create);
        read.CopyTo(write);
    }

    public record InstallMmlLibsDto(bool UseLegacy);

    [HttpPost]
    public void InstallMmlLibs([FromBody] InstallMmlLibsDto req)
    {
        var isMaimollerLegacyModeEnabled = req.UseLegacy;
        if (System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, "Mods", "ADXHIDIOMod.dll")))
        {
            System.IO.File.Delete(Path.Combine(StaticSettings.GamePath, "Mods", "ADXHIDIOMod.dll"));
        }
        if (!isMaimollerLegacyModeEnabled || GetIsMmlLegacyLibsInstalled()) return;
        if (!System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, "Sinmai_Data", "Plugins", "hidapi.dll")))
        {
            CopyFile(Path.Combine(StaticSettings.exeDir, @"hidapi.dll"), Path.Combine(StaticSettings.GamePath, "Sinmai_Data", "Plugins", "hidapi.dll"));
        }
        if (!System.IO.File.Exists(Path.Combine(StaticSettings.GamePath, "Sinmai_Data", "Plugins", "libadxhid.dll")))
        {
            CopyFile(Path.Combine(StaticSettings.exeDir, @"libadxhid.dll"), Path.Combine(StaticSettings.GamePath, "Sinmai_Data", "Plugins", "libadxhid.dll"));
        }
    }

    [HttpPost]
    public void InstallJudgeDisplay4B()
    {
        Directory.CreateDirectory(StaticSettings.SkinAssetsDir);

        foreach (var file in Directory.EnumerateFiles(judgeDisplay4BPath))
        {
            CopyFile(file, Path.Combine(StaticSettings.SkinAssetsDir, Path.GetFileName(file)));
        }
    }

    [HttpPost]
    public void InstallMelonLoader()
    {
        if (IsMelonInstalled())
        {
            logger.LogInformation("MelonLoader 已安装，跳过。");
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
            "slow" => CI_KEY,
            "release" => RELEASE_KEY,
            _ => throw new ArgumentException("无效的类型", nameof(req.Type)),
        };
        // 从 URL 下载
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
                    throw new InvalidOperationException("签名无效");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "从 {Url} 下载 AquaMai 失败", url);
                lastException = e;
                continue;
            }
            // 保存到 Mods 目录
            var dest = Path.Combine(StaticSettings.GamePath, "Mods", "AquaMai.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            await System.IO.File.WriteAllBytesAsync(dest, data);
            if (System.IO.File.Exists(ModPaths.MuModDllInstalledPath))
            {
                System.IO.File.Delete(ModPaths.MuModDllInstalledPath);
            }
            return;
        }
        throw new InvalidOperationException("从所有 URL 下载 AquaMai 均失败", lastException);
    }

    [HttpPost]
    public async Task InstallMuMod()
    {
        CopyFile(ModPaths.MuModDllBuiltinPath, ModPaths.MuModDllInstalledPath);

        if (System.IO.File.Exists(ModPaths.AquaMaiDllInstalledPath))
        {
            System.IO.File.Delete(ModPaths.AquaMaiDllInstalledPath);
        }

        if (!System.IO.File.Exists(ModPaths.MuModConfigPath))
        {
            System.IO.File.WriteAllText(ModPaths.MuModConfigPath, "Channel = \"slow\"\nCachePath = \"LocalAssets\\\\MuMod.cache\"\n");
        }

        try
        {
            await muModService.EnsureCache(CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "安装期间下载 MuMod 缓存失败，但 DLL 已成功安装");
        }
    }

    [HttpPost]
    public void DeleteAquaMai()
    {
        if (System.IO.File.Exists(ModPaths.AquaMaiDllInstalledPath))
        {
            PlatformFile.DeleteFile(ModPaths.AquaMaiDllInstalledPath);
        }
    }

    [HttpPost]
    public void DeleteMuMod()
    {
        if (System.IO.File.Exists(ModPaths.MuModDllInstalledPath))
        {
            PlatformFile.DeleteFile(ModPaths.MuModDllInstalledPath);
        }
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
