// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Diagnostics;

namespace CraftUnifiedBooter.Services;

/// <summary>下载源与镜像 URL 重写（对应原版 electron/mirror.ts）</summary>
public static class Mirror
{
    public const string BmclapiHost = "bmclapi2.bangbang93.com";
    /// <summary>备用镜像域名（主镜像失败时自动切换）</summary>
    public const string BmclapiHost2 = "bmclapi.bangbang93.com";

    private static readonly HashSet<string> MojangHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "launchermeta.mojang.com",
        "piston-meta.mojang.com",
        "piston-data.mojang.com",
        "launcher.mojang.com",
    };

    public static string ManifestUrl(string source) =>
        source == "bmclapi" ? $"https://{BmclapiHost}/mc/game/version_manifest.json"
                            : "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    /// <summary>把旧镜像/代理地址还原为官方 URL（兼容旧 versions_cache.json）</summary>
    public static string NormalizeOfficial(string raw)
    {
        try
        {
            var u = new Uri(raw);
            if (u.Host == "localhost" && u.Port == 19876)
                return $"https://launchermeta.mojang.com{u.AbsolutePath}{u.Query}";
            if (u.Host == BmclapiHost)
            {
                var p = u.AbsolutePath;
                if (p.StartsWith("/maven/")) return $"https://libraries.minecraft.net{p["/maven".Length..]}";
                if (p.StartsWith("/assets/")) return $"https://resources.download.minecraft.net{p["/assets".Length..]}";
                return $"https://launchermeta.mojang.com{p}{u.Query}";
            }
        }
        catch { /* ignore */ }
        return raw;
    }

    /// <summary>将（官方）URL 重写为当前下载源对应的 URL；非 Mojang 域名原样返回</summary>
    public static string RewriteUrl(string raw, string source)
    {
        var url = NormalizeOfficial(raw);
        if (source == "official") return url;
        try
        {
            var u = new Uri(url);
            if (u.Host == "resources.download.minecraft.net") return $"https://{BmclapiHost}/assets{u.AbsolutePath}{u.Query}";
            if (u.Host == "libraries.minecraft.net") return $"https://{BmclapiHost}/maven{u.AbsolutePath}{u.Query}";
            if (MojangHosts.Contains(u.Host)) return $"https://{BmclapiHost}{u.AbsolutePath}{u.Query}";
        }
        catch { /* ignore */ }
        return url;
    }

    public static string LibraryMirrorUrl(string source, string libPath) =>
        source == "bmclapi" ? $"https://{BmclapiHost}/maven/{libPath}" : $"https://libraries.minecraft.net/{libPath}";

    public static string AssetMirrorUrl(string source, string subDir, string hash) =>
        source == "bmclapi" ? $"https://{BmclapiHost}/assets/{subDir}/{hash}" : $"https://resources.download.minecraft.net/{subDir}/{hash}";

    // ============ 多源候选 URL（PCL2 DlSource*Get 思路：镜像优先，官方兜底） ============

    /// <summary>
    /// 将任意官方 URL 展开为一组候选下载地址（镜像优先、官方兜底），用于下载失败时自动切换源。
    /// 对应 PCL2 的 DlSourceResourceGet / DlSourceLibraryGet / DlSourceLauncherOrMetaGet。
    /// </summary>
    public static string[] CandidateUrls(string raw, string source)
    {
        var url = NormalizeOfficial(raw);
        if (source == "official") return new[] { url };
        try
        {
            var u = new Uri(url);
            // 资源文件（assets）：主/备 BMCLAPI /assets 镜像 + 官方
            if (u.Host == "resources.download.minecraft.net")
                return new[]
                {
                    $"https://{BmclapiHost}/assets{u.AbsolutePath}{u.Query}",
                    $"https://{BmclapiHost2}/assets{u.AbsolutePath}{u.Query}",
                    url,
                };
            // 依赖库：主/备 BMCLAPI /maven 与主镜像 /libraries + 官方
            if (u.Host == "libraries.minecraft.net")
                return new[]
                {
                    $"https://{BmclapiHost}/maven{u.AbsolutePath}{u.Query}",
                    $"https://{BmclapiHost2}/maven{u.AbsolutePath}{u.Query}",
                    $"https://{BmclapiHost}/libraries{u.AbsolutePath}{u.Query}",
                    url,
                };
            // 版本 JSON / client.jar / 元数据：主/备镜像 + 官方
            if (MojangHosts.Contains(u.Host))
                return new[]
                {
                    $"https://{BmclapiHost}{u.AbsolutePath}{u.Query}",
                    $"https://{BmclapiHost2}{u.AbsolutePath}{u.Query}",
                    url,
                };
        }
        catch { /* 非标准 URL 原样返回 */ }
        return new[] { url };
    }

    /// <summary>测速：下载版本清单前 512KB，返回 KB/s；失败/超时返回 0</summary>
    public static async Task<int> MeasureSourceSpeed(string source, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        long bytes = 0;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var req = new HttpRequestMessage(HttpMethod.Get, ManifestUrl(source));
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 524287);
            using var resp = await Downloader.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!resp.IsSuccessStatusCode) return 0;
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[65536];
            int n;
            while ((n = await stream.ReadAsync(buf, cts.Token)) > 0)
            {
                bytes += n;
                if (bytes >= 524288) break;
            }
        }
        catch { return 0; }
        var ms = sw.ElapsedMilliseconds;
        return ms > 0 ? (int)(bytes / 1024.0 / (ms / 1000.0)) : 0;
    }

    // ============ 自动测速选择（缓存 10 分钟） ============
    private static (string Source, DateTime At)? _autoCache;

    /// <summary>解析当前实际使用的下载源（auto 模式下测速选最快）</summary>
    public static async Task<string> ResolveDownloadSourceAsync(bool force = false)
    {
        var mode = SettingsStore.Current.DownloadSource;
        if (mode is "official" or "bmclapi") return mode;
        if (!force && _autoCache.HasValue && DateTime.Now - _autoCache.Value.At < TimeSpan.FromMinutes(10))
            return _autoCache.Value.Source;

        var speeds = new Dictionary<string, int>
        {
            ["official"] = await MeasureSourceSpeed("official"),
            ["bmclapi"] = await MeasureSourceSpeed("bmclapi"),
        };
        var best = speeds.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).FirstOrDefault();
        var source = best.Value > 0 ? best.Key : "official";
        _autoCache = (source, DateTime.Now);
        return source;
    }

    /// <summary>全部源测速（设置页展示）</summary>
    public static async Task<List<(string Id, string Name, bool Ok, long Ms, int SpeedKBps)>> TestAllSourcesAsync()
    {
        var results = new List<(string, string, bool, long, int)>();
        var sw = Stopwatch.StartNew();
        var speed = await MeasureSourceSpeed("official");
        results.Add(("official", "官方源", speed > 0, sw.ElapsedMilliseconds, speed));
        sw.Restart();
        speed = await MeasureSourceSpeed("bmclapi");
        results.Add(("bmclapi", "BMCLAPI", speed > 0, sw.ElapsedMilliseconds, speed));
        return results;
    }
}
