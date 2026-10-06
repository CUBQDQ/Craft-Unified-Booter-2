// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CraftUnifiedBooter.Services;

/// <summary>
/// 加载器一键安装：Fabric / Forge / NeoForge / OptiFine。
/// 策略：先确保原版父版本已安装，再下载加载器官方元数据/安装器，
/// 生成版本 profile（versions/&lt;id&gt;/&lt;id&gt;.json + .jar）并补齐依赖库。
/// Forge/NeoForge 直接解析官方安装器 zip（提取 version.json + 内嵌 maven 库 + client jar），无需用户安装 Java。
/// </summary>
public static class LoaderService
{
    private const string FabricMetaRoot = "https://meta.fabricmc.net/v2";
    private const string ForgePromosUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string ForgeMavenRoot = "https://maven.minecraftforge.net";
    private const string NeoForgeMavenRoot = "https://maven.neoforged.net/releases";
    private const string OptifineApiRoot = "https://bmclapi2.bangbang93.com/optifine";

    public static async Task<(bool Success, string? Error)> InstallAsync(string mcVersion, string loader, string? nameOverride = null)
    {
        try
        {
            return loader.ToLowerInvariant() switch
            {
                "fabric" => await InstallFabricAsync(mcVersion, nameOverride),
                "forge" => await InstallForgeAsync(mcVersion, nameOverride),
                "neoforge" => await InstallNeoForgeAsync(mcVersion, nameOverride),
                "optifine" => await InstallOptiFineAsync(mcVersion, nameOverride),
                _ => (false, $"未知加载器：{loader}"),
            };
        }
        catch (OperationCanceledException)
        {
            return (false, "已取消");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ===================== 通用工具 =====================

    private static string MavenNameToPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return "";
        var group = parts[0].Replace('.', '/');
        var classifier = parts.Length > 3 ? parts[3] : "";
        var fileName = string.IsNullOrEmpty(classifier) ? $"{parts[1]}-{parts[2]}.jar" : $"{parts[1]}-{parts[2]}-{classifier}.jar";
        return $"{group}/{parts[1]}/{parts[2]}/{fileName}";
    }

    private static string GetMcOsName() => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "osx" : "linux";

    /// <summary>按 Mojang rules 判断库是否允许当前 OS 使用（Fabric profile 的库一般无 rules）。</summary>
    private static bool IsLibraryAllowed(VersionLibrary lib)
    {
        if (lib.Rules == null || lib.Rules.Count == 0) return true;
        var allowed = true;
        var osName = GetMcOsName();
        foreach (var rule in lib.Rules)
        {
            var osMatch = rule.Os?.Name == null || rule.Os.Name == osName;
            if (osMatch) allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private static bool IsValidInstallName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (name.Contains(':') || name.Contains('*') || name.Contains('?')) return false;
        return true;
    }

    /// <summary>替换版本 profile JSON 的 id 字段，其余字段原样保留。</summary>
    private static string ModifyId(string json, string newId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name == "id") writer.WriteString("id", newId);
                else prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>数值感知的版本号降序比较（用于从 maven metadata 选最新版）。</summary>
    private static int CompareVersionsDesc(string a, string b)
    {
        var pa = SplitVersion(a);
        var pb = SplitVersion(b);
        var len = Math.Max(pa.Count, pb.Count);
        for (int i = 0; i < len; i++)
        {
            var x = i < pa.Count ? pa[i] : 0;
            var y = i < pb.Count ? pb[i] : 0;
            if (x != y) return y.CompareTo(x);
        }
        return 0;
    }

    private static List<long> SplitVersion(string v)
    {
        var result = new List<long>();
        foreach (var seg in v.Split('.', '-', '_'))
        {
            if (long.TryParse(seg, out var n)) result.Add(n);
            else result.Add(0);
        }
        return result;
    }

    /// <summary>从安装器 zip 提取 maven/ 目录到 libraries/，返回成功提取的文件数。</summary>
    private static async Task<int> ExtractMavenLibrariesAsync(ZipArchive zip, string skipSuffix = "")
    {
        int count = 0;
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("/"))
                continue;
            var relPath = entry.FullName["maven/".Length..].Replace('/', Path.DirectorySeparatorChar);
            if (!string.IsNullOrEmpty(skipSuffix) && relPath.EndsWith(skipSuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            var destPath = Path.Combine(Store.GetLibrariesDir(), relPath);
            if (File.Exists(destPath)) continue;
            Store.EnsureDir(Path.GetDirectoryName(destPath)!);
            using var src = entry.Open();
            using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true);
            await src.CopyToAsync(dst);
            count++;
        }
        return count;
    }

    /// <summary>下载 profile 中缺失的依赖库（含 Fabric 顶级 url 字段支持）。</summary>
    private static async Task<(int Failed, string? FirstError)> DownloadMissingLibrariesAsync(
        string taskId, List<VersionLibrary> libraries, string defaultMavenRoot)
    {
        var files = new List<(string[] Urls, string Path, long Size)>();
        foreach (var lib in libraries)
        {
            if (!IsLibraryAllowed(lib)) continue;
            var libPath = lib.Downloads?.Artifact?.Path ?? MavenNameToPath(lib.Name);
            if (string.IsNullOrEmpty(libPath)) continue;
            var fullPath = Path.Combine(Store.GetLibrariesDir(), libPath);
            if (File.Exists(fullPath)) continue;
            string url;
            if (lib.Downloads?.Artifact?.Url != null) url = lib.Downloads.Artifact.Url;
            else if (!string.IsNullOrEmpty(lib.Url)) url = $"{lib.Url.TrimEnd('/')}/{libPath}";
            else url = $"{defaultMavenRoot}/{libPath}";
            var size = lib.Downloads?.Artifact?.Size ?? 0;
            files.Add((new[] { url }, fullPath, size));
        }

        if (files.Count == 0)
        {
            var t = DownloadManager.GetTask(taskId);
            if (t != null) t.TotalFiles = 0;
            return (0, null);
        }

        var ct = DownloadManager.GetTask(taskId)?.Cts?.Token ?? default;
        var result = await DownloadManager.DownloadFilesAsync(taskId, files, ct: ct);
        return result;
    }

    // ===================== Fabric =====================

    private static async Task<(bool Success, string? Error)> InstallFabricAsync(string mcVersion, string? nameOverride)
    {
        // 1) 查询最新 loader 版本
        var loaderListRaw = await Downloader.DownloadStringWithCandidatesAsync(new[] { $"{FabricMetaRoot}/versions/loader/{mcVersion}" });
        string loaderVer;
        using (var doc = JsonDocument.Parse(loaderListRaw))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return (false, $"Fabric 暂不支持 Minecraft {mcVersion}");
            var first = doc.RootElement[0];
            loaderVer = first.TryGetProperty("loader", out var l) && l.TryGetProperty("version", out var lv)
                ? lv.GetString()!
                : throw new Exception("Fabric 元数据缺少 loader.version");
        }

        var installId = string.IsNullOrWhiteSpace(nameOverride) ? $"{mcVersion}-fabric-loader-{loaderVer}" : nameOverride.Trim();
        if (!IsValidInstallName(installId)) return (false, "版本名称包含非法字符");
        if (string.Equals(installId, mcVersion, StringComparison.OrdinalIgnoreCase))
            return (false, "版本名称不能与原版版本号相同，请使用其他名称");

        // 2) 确保原版父版本已安装
        if (!VersionsService.IsInstalled(mcVersion))
        {
            var (ok, err) = await VersionsService.InstallAsync(mcVersion);
            if (!ok) return (false, $"原版 {mcVersion} 安装失败：{err}");
        }

        // 3) 获取 profile JSON（官方 meta 直接返回可用的版本 profile，无需 installer 版本号）
        var profileUrl = $"{FabricMetaRoot}/versions/loader/{mcVersion}/{loaderVer}/profile/json";
        var profileRaw = await Downloader.DownloadStringWithCandidatesAsync(new[] { profileUrl });
        var profileJson = ModifyId(profileRaw, installId);

        var versionDir = Path.Combine(Store.GetVersionsDir(), installId);
        Store.EnsureDir(versionDir);
        File.WriteAllText(Path.Combine(versionDir, $"{installId}.json"), profileJson);

        // 4) 下载 Fabric 依赖库（maven.fabricmc.net）
        var detail = JsonSerializer.Deserialize<VersionDetail>(profileJson, Store.JsonReadOpts);
        var taskId = $"loader-{installId}";
        var task = DownloadManager.Begin(taskId, installId, "加载器");
        try
        {
            var (failed, firstError) = await DownloadMissingLibrariesAsync(taskId, detail?.Libraries ?? new(), "https://maven.fabricmc.net");
            if (failed > 0)
            {
                DownloadManager.Complete(taskId, false, $"{failed} 个文件下载失败");
                return (false, $"{failed} 个加载器依赖下载失败：{firstError}");
            }
            DownloadManager.Complete(taskId, true);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            DownloadManager.Cancel(taskId);
            return (false, "已取消");
        }
        catch (Exception ex)
        {
            DownloadManager.Complete(taskId, false, ex.Message);
            return (false, ex.Message);
        }
    }

    // ===================== Forge =====================

    private static async Task<(bool Success, string? Error)> InstallForgeAsync(string mcVersion, string? nameOverride)
    {
        // 1) 查询推荐/最新 Forge 版本
        string? forgeVer = null;
        try
        {
            var promosRaw = await Downloader.DownloadStringWithCandidatesAsync(new[] { ForgePromosUrl });
            using var doc = JsonDocument.Parse(promosRaw);
            if (doc.RootElement.TryGetProperty("promos", out var promos))
            {
                if (promos.TryGetProperty($"{mcVersion}-recommended", out var rec)) forgeVer = rec.GetString();
                else if (promos.TryGetProperty($"{mcVersion}-latest", out var lat)) forgeVer = lat.GetString();
            }
        }
        catch { /* promos 失败则回退 maven metadata */ }

        if (string.IsNullOrEmpty(forgeVer))
        {
            var metaRaw = await Downloader.DownloadStringWithCandidatesAsync(new[] { $"{ForgeMavenRoot}/net/minecraftforge/forge/maven-metadata.xml" });
            var versions = XDocument.Parse(metaRaw).Descendants("version").Select(e => e.Value)
                .Where(v => v.StartsWith($"{mcVersion}-", StringComparison.OrdinalIgnoreCase)).ToList();
            versions.Sort((a, b) => CompareVersionsDesc(a, b));
            forgeVer = versions.FirstOrDefault()?.Split('-').LastOrDefault();
            if (string.IsNullOrEmpty(forgeVer)) return (false, $"Forge 暂不支持 Minecraft {mcVersion}");
        }

        var installId = string.IsNullOrWhiteSpace(nameOverride) ? $"{mcVersion}-forge-{forgeVer}" : nameOverride.Trim();
        if (!IsValidInstallName(installId)) return (false, "版本名称包含非法字符");
        if (string.Equals(installId, mcVersion, StringComparison.OrdinalIgnoreCase))
            return (false, "版本名称不能与原版版本号相同，请使用其他名称");

        // 2) 确保原版父版本已安装
        if (!VersionsService.IsInstalled(mcVersion))
        {
            var (ok, err) = await VersionsService.InstallAsync(mcVersion);
            if (!ok) return (false, $"原版 {mcVersion} 安装失败：{err}");
        }

        var taskId = $"loader-{installId}";
        var task = DownloadManager.Begin(taskId, installId, "加载器");
        try
        {
            // 3) 下载官方安装器
            var installerUrl = $"{ForgeMavenRoot}/net/minecraftforge/forge/{mcVersion}-{forgeVer}/forge-{mcVersion}-{forgeVer}-installer.jar";
            var installerPath = Path.Combine(Store.CubDir, "loaders", $"forge-{mcVersion}-{forgeVer}-installer.jar");
            Store.EnsureDir(Path.GetDirectoryName(installerPath)!);
            await Downloader.DownloadFileWithCandidatesAsync(new[] { installerUrl }, installerPath, 0, null, task.Cts?.Token ?? default);

            // 4) 解析安装器：读取 version.json + 提取内嵌 maven 库 + client jar
            string profileJson;
            string? clientJarPathInZip = null;
            using (var zip = ZipFile.OpenRead(installerPath))
            {
                var versionEntry = zip.GetEntry("version.json") ?? throw new Exception("Forge 安装器缺少 version.json");
                using (var sr = new StreamReader(versionEntry.Open()))
                    profileJson = await sr.ReadToEndAsync();

                // 提取内嵌库（跳过 client jar，它会被复制为版本 jar）
                await ExtractMavenLibrariesAsync(zip);

                clientJarPathInZip = zip.Entries
                    .Where(e => e.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.Contains("/forge/", StringComparison.OrdinalIgnoreCase)
                        && (e.FullName.EndsWith("-universal.jar", StringComparison.OrdinalIgnoreCase)
                            || e.FullName.EndsWith("-client.jar", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.FullName.Length)
                    .Select(e => e.FullName)
                    .FirstOrDefault();
            }

            var versionDir = Path.Combine(Store.GetVersionsDir(), installId);
            Store.EnsureDir(versionDir);

            // 5) 复制 client jar 为版本 jar
            if (!string.IsNullOrEmpty(clientJarPathInZip))
            {
                using var zip = ZipFile.OpenRead(installerPath);
                var entry = zip.GetEntry(clientJarPathInZip)!;
                var destJar = Path.Combine(versionDir, $"{installId}.jar");
                if (!File.Exists(destJar))
                {
                    using var src = entry.Open();
                    using var dst = new FileStream(destJar, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true);
                    await src.CopyToAsync(dst);
                }
            }

            // 6) 写版本 profile
            File.WriteAllText(Path.Combine(versionDir, $"{installId}.json"), ModifyId(profileJson, installId));

            // 7) 下载 profile 中仍缺失的依赖库
            var detail = JsonSerializer.Deserialize<VersionDetail>(profileJson, Store.JsonReadOpts);
            var (failed, firstError) = await DownloadMissingLibrariesAsync(taskId, detail?.Libraries ?? new(), ForgeMavenRoot);
            if (failed > 0)
            {
                DownloadManager.Complete(taskId, false, $"{failed} 个文件下载失败");
                return (false, $"{failed} 个加载器依赖下载失败：{firstError}");
            }
            DownloadManager.Complete(taskId, true);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            DownloadManager.Cancel(taskId);
            return (false, "已取消");
        }
        catch (Exception ex)
        {
            DownloadManager.Complete(taskId, false, ex.Message);
            return (false, ex.Message);
        }
    }

    // ===================== NeoForge =====================

    private static async Task<(bool Success, string? Error)> InstallNeoForgeAsync(string mcVersion, string? nameOverride)
    {
        // 1) 从 maven metadata 选择对应 MC 的最新稳定版
        var metaRaw = await Downloader.DownloadStringWithCandidatesAsync(new[] { $"{NeoForgeMavenRoot}/net/neoforged/neoforge/maven-metadata.xml" });
        var versions = XDocument.Parse(metaRaw).Descendants("version").Select(e => e.Value).ToList();
        var prefixes = GetNeoForgeVersionPrefixes(mcVersion);
        if (prefixes == null || prefixes.Count == 0) return (false, $"NeoForge 暂不支持 Minecraft {mcVersion}");

        var candidates = versions.Where(v => prefixes.Any(p => v.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
        if (candidates.Count == 0) return (false, $"NeoForge 暂不支持 Minecraft {mcVersion}");
        var stable = candidates.Where(v => !Regex.IsMatch(v, @"(beta|alpha|rc|snapshot|preview)", RegexOptions.IgnoreCase)).ToList();
        var pool = stable.Count > 0 ? stable : candidates;
        pool.Sort((a, b) => CompareVersionsDesc(a, b));
        var neoVer = pool[0];

        var installId = string.IsNullOrWhiteSpace(nameOverride) ? $"{mcVersion}-neoforge-{neoVer}" : nameOverride.Trim();
        if (!IsValidInstallName(installId)) return (false, "版本名称包含非法字符");
        if (string.Equals(installId, mcVersion, StringComparison.OrdinalIgnoreCase))
            return (false, "版本名称不能与原版版本号相同，请使用其他名称");

        // 2) 确保原版父版本已安装
        if (!VersionsService.IsInstalled(mcVersion))
        {
            var (ok, err) = await VersionsService.InstallAsync(mcVersion);
            if (!ok) return (false, $"原版 {mcVersion} 安装失败：{err}");
        }

        var taskId = $"loader-{installId}";
        var task = DownloadManager.Begin(taskId, installId, "加载器");
        try
        {
            // 3) 下载官方安装器
            var installerUrl = $"{NeoForgeMavenRoot}/net/neoforged/neoforge/{neoVer}/neoforge-{neoVer}-installer.jar";
            var installerPath = Path.Combine(Store.CubDir, "loaders", $"neoforge-{neoVer}-installer.jar");
            Store.EnsureDir(Path.GetDirectoryName(installerPath)!);
            await Downloader.DownloadFileWithCandidatesAsync(new[] { installerUrl }, installerPath, 0, null, task.Cts?.Token ?? default);

            // 4) 解析安装器
            string profileJson;
            string? clientJarPathInZip = null;
            using (var zip = ZipFile.OpenRead(installerPath))
            {
                var versionEntry = zip.GetEntry("version.json") ?? throw new Exception("NeoForge 安装器缺少 version.json");
                using (var sr = new StreamReader(versionEntry.Open()))
                    profileJson = await sr.ReadToEndAsync();

                await ExtractMavenLibrariesAsync(zip);

                clientJarPathInZip = zip.Entries
                    .Where(e => e.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.Contains("/neoforge/", StringComparison.OrdinalIgnoreCase)
                        && (e.FullName.EndsWith("-universal.jar", StringComparison.OrdinalIgnoreCase)
                            || e.FullName.EndsWith("-client.jar", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.FullName.Length)
                    .Select(e => e.FullName)
                    .FirstOrDefault();
            }

            var versionDir = Path.Combine(Store.GetVersionsDir(), installId);
            Store.EnsureDir(versionDir);

            // 5) 复制 client jar 为版本 jar
            if (!string.IsNullOrEmpty(clientJarPathInZip))
            {
                using var zip = ZipFile.OpenRead(installerPath);
                var entry = zip.GetEntry(clientJarPathInZip)!;
                var destJar = Path.Combine(versionDir, $"{installId}.jar");
                if (!File.Exists(destJar))
                {
                    using var src = entry.Open();
                    using var dst = new FileStream(destJar, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true);
                    await src.CopyToAsync(dst);
                }
            }

            // 6) 写版本 profile
            File.WriteAllText(Path.Combine(versionDir, $"{installId}.json"), ModifyId(profileJson, installId));

            // 7) 下载缺失依赖库
            var detail = JsonSerializer.Deserialize<VersionDetail>(profileJson, Store.JsonReadOpts);
            var (failed, firstError) = await DownloadMissingLibrariesAsync(taskId, detail?.Libraries ?? new(), NeoForgeMavenRoot);
            if (failed > 0)
            {
                DownloadManager.Complete(taskId, false, $"{failed} 个文件下载失败");
                return (false, $"{failed} 个加载器依赖下载失败：{firstError}");
            }
            DownloadManager.Complete(taskId, true);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            DownloadManager.Cancel(taskId);
            return (false, "已取消");
        }
        catch (Exception ex)
        {
            DownloadManager.Complete(taskId, false, ex.Message);
            return (false, ex.Message);
        }
    }

    /// <summary>根据 MC 版本推导 NeoForge maven 版本前缀（覆盖 1.x 旧命名与 26.x 新命名）。</summary>
    private static List<string>? GetNeoForgeVersionPrefixes(string mcVersion)
    {
        var prefixes = new List<string>();
        var m = Regex.Match(mcVersion, @"^1\.(\d+)(?:\.(\d+))?$");
        if (m.Success)
        {
            var major = int.Parse(m.Groups[1].Value);
            var minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            if (major == 20 && minor == 1) prefixes.Add("1.20.1-");
            else if (major == 20 && minor >= 2) prefixes.Add($"20.{minor}.");
            else if (major == 21) prefixes.Add($"21.{minor}.");
            else prefixes.Add($"{mcVersion}-");
        }
        else
        {
            // 新版 Minecraft 版本号（如 26.1 / 26.2）：NeoForge 版本号以 MC 版本号开头
            prefixes.Add($"{mcVersion}.");
        }
        return prefixes;
    }

    // ===================== OptiFine =====================

    private static async Task<(bool Success, string? Error)> InstallOptiFineAsync(string mcVersion, string? nameOverride)
    {
        // 1) 查询 OptiFine 版本列表（BMCLAPI 镜像）
        string type, patch;
        var listRaw = await Downloader.DownloadStringWithCandidatesAsync(new[] { $"{OptifineApiRoot}/{mcVersion}" });
        using (var doc = JsonDocument.Parse(listRaw))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return (false, $"OptiFine 暂不支持 Minecraft {mcVersion}");
            JsonElement entry = default;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("type", out var t) && string.Equals(t.GetString(), "HD_U", StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    break;
                }
            }
            if (entry.ValueKind == JsonValueKind.Undefined) entry = doc.RootElement[0];
            type = entry.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "HD_U" : "HD_U";
            patch = entry.TryGetProperty("patch", out var p) ? p.GetString()!
                : entry.TryGetProperty("version", out var v) ? v.GetString()!
                : throw new Exception("OptiFine 版本信息不完整");
        }

        var optifineVer = $"{type}_{patch}";
        var installId = string.IsNullOrWhiteSpace(nameOverride) ? $"{mcVersion}-OptiFine_{optifineVer}" : nameOverride.Trim();
        if (!IsValidInstallName(installId)) return (false, "版本名称包含非法字符");
        if (string.Equals(installId, mcVersion, StringComparison.OrdinalIgnoreCase))
            return (false, "版本名称不能与原版版本号相同，请使用其他名称");

        // 2) 确保原版父版本已安装
        if (!VersionsService.IsInstalled(mcVersion))
        {
            var (ok, err) = await VersionsService.InstallAsync(mcVersion);
            if (!ok) return (false, $"原版 {mcVersion} 安装失败：{err}");
        }

        var taskId = $"loader-{installId}";
        var task = DownloadManager.Begin(taskId, installId, "加载器");
        try
        {
            // 3) 下载 OptiFine 安装器 jar
            var downloadUrl = $"{OptifineApiRoot}/{mcVersion}/{type}/{patch}";
            var optifineJarPath = Path.Combine(Store.CubDir, "loaders", $"OptiFine_{mcVersion}_{optifineVer}.jar");
            Store.EnsureDir(Path.GetDirectoryName(optifineJarPath)!);
            await Downloader.DownloadFileWithCandidatesAsync(new[] { downloadUrl }, optifineJarPath, 0, null, task.Cts?.Token ?? default);

            // 4) 解析版本 profile（version.json 或 install_profile.json 的 versionInfo）
            string? profileJson = null;
            var launchwrapperEntryNames = new List<string>();
            using (var zip = ZipFile.OpenRead(optifineJarPath))
            {
                var versionEntry = zip.GetEntry("version.json");
                if (versionEntry != null)
                {
                    using var sr = new StreamReader(versionEntry.Open());
                    profileJson = await sr.ReadToEndAsync();
                }
                else
                {
                    var installProfile = zip.GetEntry("install_profile.json");
                    if (installProfile != null)
                    {
                        using var sr = new StreamReader(installProfile.Open());
                        var ipRaw = await sr.ReadToEndAsync();
                        using var ipDoc = JsonDocument.Parse(ipRaw);
                        if (ipDoc.RootElement.TryGetProperty("versionInfo", out var vi))
                            profileJson = vi.GetRawText();
                    }
                }
                launchwrapperEntryNames = zip.Entries
                    .Where(e => e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.ToLowerInvariant().Contains("launchwrapper"))
                    .Select(e => e.FullName)
                    .ToList();
            }
            if (profileJson == null) return (false, "OptiFine 安装器缺少版本配置（version.json），暂不支持该版本自动安装");

            // 5) 写 profile + 复制 OptiFine jar 为版本 jar
            var versionDir = Path.Combine(Store.GetVersionsDir(), installId);
            Store.EnsureDir(versionDir);
            File.WriteAllText(Path.Combine(versionDir, $"{installId}.json"), ModifyId(profileJson, installId));
            var destJar = Path.Combine(versionDir, $"{installId}.jar");
            if (!File.Exists(destJar)) File.Copy(optifineJarPath, destJar);

            // 6) 提取内嵌 launchwrapper 等 jar 到 libraries，其余缺失库加入下载
            var detail = JsonSerializer.Deserialize<VersionDetail>(profileJson, Store.JsonReadOpts);
            var libs = detail?.Libraries ?? new();
            var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(optifineJarPath))
            {
                foreach (var lib in libs)
                {
                    var libPath = lib.Downloads?.Artifact?.Path ?? MavenNameToPath(lib.Name);
                    if (string.IsNullOrEmpty(libPath)) continue;
                    var fullPath = Path.Combine(Store.GetLibrariesDir(), libPath);
                    if (File.Exists(fullPath)) { extractedPaths.Add(libPath); continue; }
                    var fileName = Path.GetFileName(libPath);
                    var entry = zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                        (Path.GetFileName(e.FullName).Equals(fileName, StringComparison.OrdinalIgnoreCase)
                         || (fileName.ToLowerInvariant().Contains("launchwrapper") && e.FullName.ToLowerInvariant().Contains("launchwrapper"))
                         || (fileName.ToLowerInvariant().Contains("optifine") && e.FullName.ToLowerInvariant().Contains("optifine"))));
                    if (entry != null)
                    {
                        Store.EnsureDir(Path.GetDirectoryName(fullPath)!);
                        using var src = entry.Open();
                        using var dst = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true);
                        await src.CopyToAsync(dst);
                        extractedPaths.Add(libPath);
                    }
                }
            }

            var files = new List<(string[] Urls, string Path, long Size)>();
            foreach (var lib in libs)
            {
                if (!IsLibraryAllowed(lib)) continue;
                var libPath = lib.Downloads?.Artifact?.Path ?? MavenNameToPath(lib.Name);
                if (string.IsNullOrEmpty(libPath) || extractedPaths.Contains(libPath)) continue;
                var fullPath = Path.Combine(Store.GetLibrariesDir(), libPath);
                if (File.Exists(fullPath)) continue;
                var url = lib.Downloads?.Artifact?.Url ?? (!string.IsNullOrEmpty(lib.Url) ? $"{lib.Url.TrimEnd('/')}/{libPath}" : $"https://library.optifine.net/{libPath}");
                files.Add((new[] { url }, fullPath, lib.Downloads?.Artifact?.Size ?? 0));
            }

            var (failed, firstError) = await DownloadManager.DownloadFilesAsync(taskId, files, ct: task.Cts?.Token ?? default);
            if (failed > 0)
            {
                DownloadManager.Complete(taskId, false, $"{failed} 个文件下载失败");
                return (false, $"{failed} 个加载器依赖下载失败：{firstError}");
            }
            DownloadManager.Complete(taskId, true);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            DownloadManager.Cancel(taskId);
            return (false, "已取消");
        }
        catch (Exception ex)
        {
            DownloadManager.Complete(taskId, false, ex.Message);
            return (false, ex.Message);
        }
    }
}