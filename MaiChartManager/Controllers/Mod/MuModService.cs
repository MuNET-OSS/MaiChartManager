using System.Diagnostics;
using System.Text.Json;
using MaiChartManager.Utils;
using Tomlyn;

namespace MaiChartManager.Controllers.Mod;

public class MuModService(ILogger<MuModService> logger, IHttpClientFactory httpClientFactory)
{
    private const string CosVersionApiUrl = "https://munet-version-config-1251600285.cos.ap-shanghai.myqcloud.com/aquamai.json";
    private const string CfVersionApiUrl = "https://aquamai-version-config.mumur.net/api/config";
    private const string DefaultCacheRelativePath = "LocalAssets/MuMod.cache";

    private enum VersionSource
    {
        Cos,
        Cf,
    }

    private class VersionInfoModel
    {
        public string Version { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? Url2 { get; set; }
    }

    public class MuModConfigModel
    {
        public string Channel { get; set; } = "slow";
        public string CachePath { get; set; } = DefaultCacheRelativePath;
    }

    public record EnsureCacheResult(bool Success, string? Version, string? Error);

    public MuModConfigModel ReadConfig()
    {
        if (!File.Exists(ModPaths.MuModConfigPath))
        {
            return new MuModConfigModel();
        }

        var text = File.ReadAllText(ModPaths.MuModConfigPath);
        var model = Toml.ToModel<MuModConfigModel>(text, options: new TomlModelOptions { ConvertPropertyName = name => name }) ?? new MuModConfigModel();
        model.Channel = string.IsNullOrWhiteSpace(model.Channel) ? "slow" : model.Channel;
        model.CachePath = string.IsNullOrWhiteSpace(model.CachePath) ? DefaultCacheRelativePath : model.CachePath;
        return model;
    }

    public void WriteChannel(string channel)
    {
        if (channel != "slow" && channel != "fast")
        {
            throw new ArgumentException("Channel 只能为 slow 或 fast", nameof(channel));
        }

        var config = ReadConfig();
        config.Channel = channel;

        var content = Toml.FromModel(config, new TomlModelOptions { ConvertPropertyName = name => name });
        var targetPath = ModPaths.MuModConfigPath;
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, targetPath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public string GetResolvedCachePath()
    {
        var config = ReadConfig();
        var rawPath = string.IsNullOrWhiteSpace(config.CachePath) ? DefaultCacheRelativePath : config.CachePath;

        var expandedPath = Environment.ExpandEnvironmentVariables(rawPath.Trim());
        var candidate = Path.IsPathRooted(expandedPath)
            ? expandedPath
            : Path.Combine(StaticSettings.GamePath, expandedPath);

        return Path.GetFullPath(candidate);
    }

    public async Task<EnsureCacheResult> EnsureCache(CancellationToken ct = default)
    {
        var cachePath = GetResolvedCachePath();
        var hasOldCache = File.Exists(cachePath);
        var oldVersion = hasOldCache ? ReadProductVersion(cachePath) : null;

        try
        {
            var apiType = ResolveApiType(ReadConfig().Channel);
            var (versionInfo, source) = await ResolveVersionInfoAsync(apiType, ct);
            var targetVersion = NormalizeVersion(versionInfo.Version);

            if (hasOldCache && string.Equals(NormalizeVersion(oldVersion), targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("MuMod 缓存已是最新版本：{Version}", oldVersion);
                return new EnsureCacheResult(true, oldVersion, null);
            }

            var downloadUrls = BuildDownloadUrls(versionInfo, source).ToArray();
            if (downloadUrls.Length == 0)
            {
                throw new InvalidOperationException("版本 API 响应中未找到有效的下载地址");
            }

            var data = await DownloadFromUrlsAsync(downloadUrls, ct);
            var verifyResult = AquaMaiSignatureV2.VerifySignature(data);
            if (verifyResult.Status != AquaMaiSignatureV2.VerifyStatus.Valid)
            {
                throw new InvalidOperationException($"MuMod 缓存签名验证失败：{verifyResult.Status}");
            }

            var cacheDir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, data, ct);
                File.Move(tempPath, cachePath, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            var finalVersion = ReadProductVersion(cachePath) ?? targetVersion;
            logger.LogInformation("MuMod 缓存已成功更新至版本 {Version}", finalVersion);
            return new EnsureCacheResult(true, finalVersion, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新 MuMod 缓存失败");
            if (hasOldCache)
            {
                logger.LogWarning("因更新失败，继续使用现有 MuMod 缓存");
                return new EnsureCacheResult(true, oldVersion, null);
            }

            return new EnsureCacheResult(false, null, ex.Message);
        }
    }

    public string? GetCacheInfo()
    {
        var cachePath = GetResolvedCachePath();
        return File.Exists(cachePath) ? ReadProductVersion(cachePath) : null;
    }

    public bool IsMuModInstalled()
    {
        return File.Exists(ModPaths.MuModDllInstalledPath);
    }

    public string? GetMuModVersion()
    {
        return File.Exists(ModPaths.MuModDllInstalledPath) ? ReadProductVersion(ModPaths.MuModDllInstalledPath) : null;
    }

    private static string ResolveApiType(string channel)
    {
        return channel switch
        {
            "slow" => "slow",
            "fast" => "ci",
            _ => throw new InvalidOperationException($"不支持的 MuMod 频道：{channel}"),
        };
    }

    private static string? ReadProductVersion(string path)
    {
        return FileVersionInfo.GetVersionInfo(path).ProductVersion;
    }

    private static string? NormalizeVersion(string? version)
    {
        return version?.Trim().TrimStart('v', 'V');
    }

    private async Task<(VersionInfoModel Info, VersionSource Source)> ResolveVersionInfoAsync(string apiType, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        var cosTask = FetchVersionInfosAsync(CosVersionApiUrl, timeoutCts.Token);
        var cfTask = FetchVersionInfosAsync(CfVersionApiUrl, timeoutCts.Token);

        var firstTask = await Task.WhenAny(cosTask, cfTask);
        var secondTask = firstTask == cosTask ? cfTask : cosTask;
        var firstSource = firstTask == cosTask ? VersionSource.Cos : VersionSource.Cf;
        var secondSource = firstTask == cosTask ? VersionSource.Cf : VersionSource.Cos;

        Exception? firstError = null;
        try
        {
            var firstVersions = await firstTask;
            var firstMatch = firstVersions.FirstOrDefault(v => string.Equals(v.Type, apiType, StringComparison.OrdinalIgnoreCase));
            if (firstMatch != null)
            {
                logger.LogInformation("MuMod 版本元数据已从 {Source} 获取", firstSource);
                return (firstMatch, firstSource);
            }

            throw new InvalidOperationException($"来自 {firstSource} 的版本元数据中没有 '{apiType}' 条目");
        }
        catch (Exception ex)
        {
            firstError = ex;
            logger.LogWarning(ex, "使用来自 {Source} 的版本元数据失败", firstSource);
        }

        var secondVersions = await secondTask;
        var secondMatch = secondVersions.FirstOrDefault(v => string.Equals(v.Type, apiType, StringComparison.OrdinalIgnoreCase));
        if (secondMatch == null)
        {
            throw new InvalidOperationException($"两个来源的版本元数据中均没有 '{apiType}' 条目", firstError);
        }

        logger.LogInformation("MuMod 版本元数据已从备用来源 {Source} 获取", secondSource);
        return (secondMatch, secondSource);
    }

    private static IEnumerable<string> BuildDownloadUrls(VersionInfoModel info, VersionSource source)
    {
        if (source == VersionSource.Cos)
        {
            if (!string.IsNullOrWhiteSpace(info.Url))
            {
                yield return info.Url;
            }

            if (!string.IsNullOrWhiteSpace(info.Url2))
            {
                yield return info.Url2;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(info.Url2))
            {
                yield return info.Url2;
            }

            if (!string.IsNullOrWhiteSpace(info.Url))
            {
                yield return info.Url;
            }
        }
    }

    private async Task<VersionInfoModel[]> FetchVersionInfosAsync(string url, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();

        var json = await client.GetStringAsync(url, ct);
        var result = JsonSerializer.Deserialize<VersionInfoModel[]>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result ?? [];
    }

    private async Task<byte[]> DownloadFromUrlsAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();

        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                return await client.GetByteArrayAsync(url, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("从所有 URL 下载 MuMod 缓存均失败", lastError);
    }
}
