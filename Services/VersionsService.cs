// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Text.Json;

namespace CraftUnifiedBooter.Services;

/// <summary>版本列表 / 刷新 / 安装 / 卸载（对应原版 electron/ipc/version.ts）</summary>
public static class VersionsService
{
    // ===================== 加载器识别 =====================
    public static string? DetectLoader(VersionDetail vjson)
    {
        if (vjson == null) return null;
        var mainClass = (vjson.MainClass ?? "").ToLowerInvariant();
        var libs = string.Join(" ", (vjson.Libraries ?? new()).Select(l => (l.Name ?? "").ToLowerInvariant()));
        var args = JsonSerializer.Serialize(vjson.Arguments ?? new()).ToLowerInvariant() + " " + (vjson.MinecraftArguments ?? "").ToLowerInvariant();

        if (mainClass.Contains("net.neoforged")) return "neoforge";
        if (mainClass.Contains("cpw.mods") || mainClass.Contains("net.minecraftforge")) return "forge";
        if (mainClass.Contains("net.fabricmc")) return "fabric";
        if (mainClass.Contains("optifine")) return "optifine";
        if (libs.Contains("net.neoforged")) return "neoforge";
        if (libs.Contains("cpw.mods") || libs.Contains("net.minecraftforge")) return "forge";
        if (libs.Contains("net.fabricmc")) return "fabric";
        if (libs.Contains("optifine")) return "optifine";
        if (args.Contains("optifine")) return "optifine";
        if (!string.IsNullOrEmpty(vjson.InheritsFrom))
        {
            var p = vjson.InheritsFrom.ToLowerInvariant();
            if (p.Contains("neoforge")) return "neoforge";
            if (p.Contains("forge")) return "forge";
            if (p.Contains("fabric")) return "fabric";
            if (p.Contains("optifine")) return "optifine";
        }
        return null;
    }

    // ===================== 已安装版本扫描 =====================
    public static HashSet<string> ScanInstalledSet()
    {
        var set = new HashSet<string>();
        var versionsDir = Store.GetVersionsDir();
        if (!Directory.Exists(versionsDir)) return set;
        foreach (var entry in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(entry);
            var jar = Path.Combine(entry, $"{id}.jar");
            var json = Path.Combine(entry, $"{id}.json");
            if (File.Exists(jar)) set.Add(id);
            else if (File.Exists(json))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(json));
                    if (doc.RootElement.TryGetProperty("inheritsFrom", out var inh))
                    {
                        var parent = inh.GetString();
                        if (!string.IsNullOrEmpty(parent) && set.Contains(parent)) set.Add(id);
                    }
                }
                catch { /* ignore */ }
            }
        }
        return set;
    }

    /// <summary>扫描已安装的 modded 版本（不在清单里）作为附加列表</summary>
    public static List<VersionEntry> ScanExtraVersions(HashSet<string> installedSet, HashSet<string> originalIds)
    {
        var extra = new List<VersionEntry>();
        var versionsDir = Store.GetVersionsDir();
        if (!Directory.Exists(versionsDir)) return extra;
        foreach (var entry in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(entry);
            var jsonPath = Path.Combine(entry, $"{id}.json");
            if (!File.Exists(jsonPath)) continue;
            if (originalIds.Contains(id)) continue;
            string? loader = null;
            string releaseTime = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            try
            {
                var vd = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath), Store.JsonReadOpts);
                if (vd != null)
                {
                    if (!string.IsNullOrEmpty(vd.ReleaseTime)) releaseTime = vd.ReleaseTime;
                    loader = DetectLoader(vd);
                }
            }
            catch { /* ignore */ }
            if (loader == null)
            {
                var idLower = id.ToLowerInvariant();
                if (idLower.Contains("neoforge")) loader = "neoforge";
                else if (idLower.Contains("forge")) loader = "forge";
                else if (idLower.Contains("fabric")) loader = "fabric";
                else if (idLower.Contains("optifine")) loader = "optifine";
            }
            extra.Add(new VersionEntry
            {
                Id = id, Type = "release", Url = "", ReleaseTime = releaseTime,
                Installed = installedSet.Contains(id) || File.Exists(Path.Combine(entry, $"{id}.jar")),
                Loader = loader, IsExtra = true,
            });
        }
        return extra;
    }

    /// <summary>获取版本列表（清单缓存 + 已安装标记 + modded 附加）</summary>
    public static List<VersionEntry> GetVersions()
    {
        var cache = Store.ReadJson<VersionManifest>("versions_cache.json");
        // 缓存为空时返回内置 fallback，确保 UI 始终有内容可显示
        if (cache?.Versions == null || cache.Versions.Count == 0)
        {
            cache = BuildFallbackManifest();
        }
        var installed = ScanInstalledSet();
        var originals = cache.Versions.Select(v => new VersionEntry
        {
            Id = v.Id, Type = v.Type, Url = v.Url, Time = v.Time, ReleaseTime = v.ReleaseTime,
            Installed = installed.Contains(v.Id),
        }).ToList();
        var originalIds = originals.Select(v => v.Id).ToHashSet();
        var extras = ScanExtraVersions(installed, originalIds);
        extras.AddRange(originals);
        return SortVersions(extras);
    }

    /// <summary>内置 fallback 版本清单（网络不可达时的兜底数据）</summary>
    private static VersionManifest BuildFallbackManifest()
    {
        var releaseTime = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var entries = new List<VersionEntry>
        {
            new() { Id = "1.21.4", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.21.3", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.21.2", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.21.1", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.21", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.20.6", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.20.4", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.20.1", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.20", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.19.4", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.19.2", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.18.2", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.16.5", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.15.2", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.14.4", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.12.2", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
            new() { Id = "1.8.9", Type = "release", Url = "https://launchermeta.mojang.com/mc/game/version_manifest.json", Time = releaseTime, ReleaseTime = releaseTime },
        };
        return new VersionManifest { Versions = entries };
    }

    /// <summary>按版本号降序排序（数字比较，最新版在前）</summary>
    private static List<VersionEntry> SortVersions(List<VersionEntry> versions)
    {
        versions.Sort((a, b) =>
        {
            // 先按类型排：snapshot > release > old_beta > old_alpha
            var typeOrder = new Dictionary<string, int>
            {
                ["snapshot"] = 0, ["release"] = 1, ["old_beta"] = 2, ["old_alpha"] = 3
            };
            var aType = typeOrder.GetValueOrDefault(a.Type ?? "", 99);
            var bType = typeOrder.GetValueOrDefault(b.Type ?? "", 99);
            if (aType != bType) return aType.CompareTo(bType);

            // 再按版本号数字降序（1.21.8 > 1.21.7 > 1.20.1 > 1.2）
            return CompareSemanticVersion(b.Id ?? "", a.Id ?? "");
        });
        return versions;
    }

    /// <summary>比较两个 Minecraft 版本号（降序语义：返回负数表示 a > b）</summary>
    private static int CompareSemanticVersion(string a, string b)
    {
        // 提取主版本号段（如 "1.21.8" -> [1, 21, 8]，"23w41a" -> [23, 41]）
        var partsA = ExtractVersionParts(a);
        var partsB = ExtractVersionParts(b);
        var maxLen = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < maxLen; i++)
        {
            int pa = i < partsA.Length ? partsA[i] : 0;
            int pb = i < partsB.Length ? partsB[i] : 0;
            if (pa != pb) return pa.CompareTo(pb);
        }
        return 0;
    }

    private static int[] ExtractVersionParts(string id)
    {
        var parts = new List<int>();
        foreach (var segment in id.Split('.'))
        {
            var nums = segment.Where(char.IsDigit).ToArray();
            if (nums.Length > 0)
                parts.Add(int.Parse(new string(nums)));
        }
        return parts.ToArray();
    }

    /// <summary>刷新版本清单（按当前下载源下载并缓存原始官方链接）</summary>
    public static async Task<(List<VersionEntry>? Result, string? Error)> RefreshVersionsAsync()
    {
        try
        {
            var source = await Mirror.ResolveDownloadSourceAsync();
            var raw = await Downloader.Client.GetStringAsync(Mirror.ManifestUrl(source));
            var manifest = JsonSerializer.Deserialize<VersionManifest>(raw, Store.JsonReadOpts);
            if (manifest?.Versions == null || manifest.Versions.Count == 0)
                return (GetVersions(), null); // 网络返回空数据时用 fallback
            Store.WriteJson("versions_cache.json", manifest);

            var installed = ScanInstalledSet();
            var originals = manifest.Versions.Select(v => new VersionEntry
            {
                Id = v.Id, Type = v.Type, Url = v.Url, Time = v.Time, ReleaseTime = v.ReleaseTime,
                Installed = installed.Contains(v.Id),
            }).ToList();
            // 下载页只显示官方版本，本地自定义版本（如"测试"）只在版本管理页展示，不混入下载列表
            return (SortVersions(originals), null);
        }
        catch (Exception ex)
        {
            // 网络不可达时返回 fallback 列表，而非空列表（仍只显示官方版本，过滤本地自定义版本）
            var fallback = GetVersions().Where(v => !v.IsExtra).ToList();
            if (fallback.Count > 0)
                return (fallback, null);
            return (null, $"无法连接版本服务器：{ex.Message}");
        }
    }

    // ===================== 安装 =====================
    private static string GetMojangOsName() => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "osx" : "linux";

    private static bool IsLibraryAllowed(VersionLibrary lib)
    {
        if (lib.Rules == null || lib.Rules.Count == 0) return true;
        var allowed = true;
        var osName = GetMojangOsName();
        foreach (var rule in lib.Rules)
        {
            var osMatch = rule.Os?.Name == null || rule.Os.Name == osName;
            if (osMatch) allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private static string GetNativeClassifier(VersionLibrary lib)
    {
        if (lib.Natives == null) return "";
        var raw = GetMojangOsName() switch
        {
            "windows" => lib.Natives.GetValueOrDefault("windows") ?? "",
            "osx" => lib.Natives.GetValueOrDefault("osx") ?? lib.Natives.GetValueOrDefault("macos") ?? "",
            _ => lib.Natives.GetValueOrDefault("linux") ?? "",
        };
        if (string.IsNullOrEmpty(raw)) return "";
        var arch = Environment.Is64BitOperatingSystem ? "64" : "32";
        return raw.Replace("${arch}", arch);
    }

    private static string LibraryToPath(string name)
    {
        var parts = name.Split(':');
        var group = parts[0].Replace('.', '/');
        return $"{group}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}.jar";
    }

    /// <summary>安装版本：版本 JSON → asset index → client.jar + libraries + assets</summary>
    public static async Task<(bool Success, string? Error)> InstallAsync(string id, string? name = null)
    {
        var installId = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
        if (installId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || installId.Contains(':') || installId.Contains('*') || installId.Contains('?'))
            return (false, "版本名称不能包含 \\ / : * ? \" < > | 字符");
        var versionsDir = Store.GetVersionsDir();
        var installDir = Path.Combine(versionsDir, installId);
        if (Directory.Exists(installDir))
        {
            // 重复下载：清空旧版本文件（json/jar/natives），保留游戏数据目录（mods/saves/config 等）
            foreach (var f in Directory.GetFiles(installDir))
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
            var nativesOld = Path.Combine(installDir, "natives");
            if (Directory.Exists(nativesOld))
            {
                try { Directory.Delete(nativesOld, true); } catch { /* ignore */ }
            }
        }
        else
        {
            Store.EnsureDir(installDir);
        }

        var taskId = $"version-{installId}";
        var task = DownloadManager.Begin(taskId, installId, "版本");
        try
        {
            // 本次安装涉及的文件清单（用于离线 SHA1 完整性校验）
            var cachedFiles = new List<VersionCachedFile>();
            var manifest = Store.ReadJson<VersionManifest>("versions_cache.json");
            if (manifest == null) return (false, "请先刷新版本列表");
            var entry = manifest.Versions.FirstOrDefault(v => v.Id == id);
            if (entry == null) return (false, $"版本 {id} 未找到");

            var source = await Mirror.ResolveDownloadSourceAsync();

            // Step 1: 版本 JSON
            Store.EnsureDir(Path.Combine(versionsDir, installId));
            var versionJsonPath = Path.Combine(versionsDir, installId, $"{installId}.json");
            var jsonRaw = await Downloader.DownloadStringWithCandidatesAsync(Mirror.CandidateUrls(entry.Url, source));
            File.WriteAllText(versionJsonPath, jsonRaw);
            var detail = JsonSerializer.Deserialize<VersionDetail>(jsonRaw, Store.JsonReadOpts)
                ?? throw new Exception("版本 JSON 解析失败");
            cachedFiles.Add(new VersionCachedFile
            {
                Path = $"versions/{installId}/{installId}.json",
                Sha1 = ComputeFileSha1(versionJsonPath),
                Size = new FileInfo(versionJsonPath).Length,
            });

            // Step 2: asset index
            var assetIndexPath = Path.Combine(Store.GetAssetsDir(), "indexes", $"{detail.AssetIndex?.Id ?? id}.json");
            if (!File.Exists(assetIndexPath))
            {
                Store.EnsureDir(Path.GetDirectoryName(assetIndexPath)!);
                var assetRaw = await Downloader.DownloadStringWithCandidatesAsync(Mirror.CandidateUrls(detail.AssetIndex!.Url, source));
                File.WriteAllText(assetIndexPath, assetRaw);
            }

            // Step 3: 收集文件（每个文件提供多源候选，失败自动切换镜像）
            var files = new List<(string[] Urls, string Path, long Size)>();
            var jarPath = Path.Combine(versionsDir, installId, $"{installId}.jar");
            if (!File.Exists(jarPath) && detail.Downloads?.Client != null)
                files.Add((Mirror.CandidateUrls(detail.Downloads.Client.Url!, source), jarPath, detail.Downloads.Client.Size));
            // 无论 jar 是否已存在，都记录其 SHA1 供离线校验
            if (detail.Downloads?.Client != null)
                cachedFiles.Add(new VersionCachedFile
                {
                    Path = $"versions/{installId}/{installId}.jar",
                    Sha1 = detail.Downloads.Client.Sha1 ?? "",
                    Size = detail.Downloads.Client.Size,
                });

            foreach (var lib in detail.Libraries ?? new())
            {
                if (!IsLibraryAllowed(lib)) continue;
                var nativeClassifier = GetNativeClassifier(lib);
                if (!string.IsNullOrEmpty(nativeClassifier))
                {
                    if (lib.Downloads?.Classifiers?.TryGetValue(nativeClassifier, out var cls) == true)
                    {
                        var fullPath = Path.Combine(Store.GetLibrariesDir(), cls!.Path);
                        if (!File.Exists(fullPath)) files.Add((Mirror.CandidateUrls(cls!.Url!, source), fullPath, cls!.Size));
                        cachedFiles.Add(new VersionCachedFile
                        {
                            Path = $"libraries/{cls.Path}",
                            Sha1 = cls.Sha1 ?? "",
                            Size = cls.Size,
                        });
                    }
                    else
                    {
                        var libPath = LibraryToPath(lib.Name);
                        var nativePath = libPath.Replace(".jar", $"-{nativeClassifier}.jar");
                        var fullPath = Path.Combine(Store.GetLibrariesDir(), nativePath);
                        if (!File.Exists(fullPath)) files.Add((Mirror.CandidateUrls(Mirror.LibraryMirrorUrl(source, nativePath), source), fullPath, 0));
                        cachedFiles.Add(new VersionCachedFile
                        {
                            Path = $"libraries/{nativePath}",
                            Sha1 = "",
                            Size = 0,
                        });
                    }
                }
                if (lib.Downloads?.Artifact != null)
                {
                    var fullPath = Path.Combine(Store.GetLibrariesDir(), lib.Downloads.Artifact!.Path);
                    if (!File.Exists(fullPath)) files.Add((Mirror.CandidateUrls(lib.Downloads.Artifact!.Url!, source), fullPath, lib.Downloads.Artifact.Size));
                    cachedFiles.Add(new VersionCachedFile
                    {
                        Path = $"libraries/{lib.Downloads.Artifact.Path}",
                        Sha1 = lib.Downloads.Artifact.Sha1 ?? "",
                        Size = lib.Downloads.Artifact.Size,
                    });
                }
                else
                {
                    var libPath = LibraryToPath(lib.Name);
                    var fullPath = Path.Combine(Store.GetLibrariesDir(), libPath);
                    if (!File.Exists(fullPath)) files.Add((Mirror.CandidateUrls(Mirror.LibraryMirrorUrl(source, libPath), source), fullPath, 0));
                    cachedFiles.Add(new VersionCachedFile
                    {
                        Path = $"libraries/{libPath}",
                        Sha1 = "",
                        Size = 0,
                    });
                }
            }

            // assets（小对象批量，也用多源候选）
            if (File.Exists(assetIndexPath))
            {
                var assets = new List<(string[] Urls, string Path, long Size)>();
                using var doc = JsonDocument.Parse(File.ReadAllText(assetIndexPath));
                if (doc.RootElement.TryGetProperty("objects", out var objects))
                {
                    foreach (var prop in objects.EnumerateObject())
                    {
                        var hash = prop.Value.GetProperty("hash").GetString();
                        if (string.IsNullOrEmpty(hash)) continue;
                        var subDir = hash[..2];
                        var assetPath = Path.Combine(Store.GetAssetsDir(), "objects", subDir, hash);
                        if (File.Exists(assetPath)) continue;
                        var size = prop.Value.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                        assets.Add((Mirror.CandidateUrls(Mirror.AssetMirrorUrl(source, subDir, hash), source), assetPath, size));
                    }
                }
                files.AddRange(assets);
            }

            // Step 4-5: 下载（使用任务自己的取消令牌，支持 UI 暂停/取消）
            var ct = task.Cts?.Token ?? default;
            var (failed, firstError) = await DownloadManager.DownloadFilesAsync(taskId, files, ct: ct);
            if (failed > 0)
            {
                DownloadManager.Complete(taskId, false, $"{failed} 个文件下载失败，请检查网络后重试");
                return (false, $"{failed} 个文件下载失败，请检查网络后重试");
            }
            DownloadManager.Complete(taskId, true);

            // 保存 SHA1 缓存清单，供离线校验游戏文件完整性
            try
            {
                Store.EnsureDir(FileCacheDir);
                var cache = new VersionFileCache
                {
                    Version = installId,
                    SavedAt = DateTimeOffset.Now.ToUnixTimeSeconds(),
                    Files = cachedFiles,
                };
                File.WriteAllText(Path.Combine(FileCacheDir, $"{installId}.json"),
                    JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 缓存写入失败不影响安装 */ }

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

    /// <summary>版本文件 SHA1 缓存目录</summary>
    private static string FileCacheDir => Path.Combine(Store.CubDir, "file_cache");

    /// <summary>计算文件 SHA1（小写 hex）。</summary>
    private static string ComputeFileSha1(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA1.Create();
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
        catch { return ""; }
    }

    /// <summary>离线校验版本文件完整性：对照安装时保存的 SHA1 缓存，返回缺失与损坏文件列表。</summary>
    public static (bool HasCache, List<string> Missing, List<string> Corrupted) VerifyVersionFiles(string id)
    {
        var missing = new List<string>();
        var corrupted = new List<string>();
        var cachePath = Path.Combine(FileCacheDir, $"{id}.json");
        if (!File.Exists(cachePath)) return (false, missing, corrupted);

        try
        {
            var cache = JsonSerializer.Deserialize<VersionFileCache>(File.ReadAllText(cachePath), Store.JsonReadOpts);
            if (cache?.Files == null) return (false, missing, corrupted);
            foreach (var f in cache.Files)
            {
                var abs = Path.Combine(Store.GetActiveGameRoot(), f.Path);
                if (!File.Exists(abs)) { missing.Add(f.Path); continue; }
                if (string.IsNullOrEmpty(f.Sha1)) continue; // 没有参考哈希的文件跳过
                var actual = ComputeFileSha1(abs);
                if (!string.Equals(actual, f.Sha1, StringComparison.OrdinalIgnoreCase))
                    corrupted.Add(f.Path);
            }
            return (true, missing, corrupted);
        }
        catch { return (false, missing, corrupted); }
    }

    public static bool Uninstall(string id)
    {
        try
        {
            var dir = Path.Combine(Store.GetVersionsDir(), id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            return true;
        }
        catch { return false; }
    }

    public static bool IsInstalled(string id)
    {
        var jsonPath = Path.Combine(Store.GetVersionsDir(), id, $"{id}.json");
        if (!File.Exists(jsonPath)) return false;
        var jarPath = Path.Combine(Store.GetVersionsDir(), id, $"{id}.jar");
        if (File.Exists(jarPath)) return true;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("inheritsFrom", out var inh))
            {
                var parent = inh.GetString();
                if (!string.IsNullOrEmpty(parent) && File.Exists(Path.Combine(Store.GetVersionsDir(), parent, $"{parent}.jar")))
                    return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }
}
