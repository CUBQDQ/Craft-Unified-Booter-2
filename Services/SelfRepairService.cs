// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Text.Json;
using System.Text.RegularExpressions;

namespace CraftUnifiedBooter.Services;

/// <summary>
/// 启动失败自愈（仿 PCL2 思路）：分析崩溃日志，自动补全缺失的游戏依赖库 /
/// 自动添加缺失的 Fabric API 模组，供启动器修复后自动重新启动。
/// </summary>
public static class SelfRepairService
{
    /// <summary>
    /// 合并控制台输出 + 游戏 latest.log + 最新 crash-report，供错误识别与自愈使用。
    /// 崩溃的详细堆栈往往只写在 crash-reports 或 latest.log 中，仅看进程 stdout 会漏掉。
    /// </summary>
    public static string BuildFullLog(string versionId, string consoleLog)
    {
        var sb = new StringBuilder(consoleLog ?? "");
        var gameDir = Store.ResolveGamePath("root", versionId);

        // latest.log（文件大时只读末尾，错误堆栈通常在尾部）
        try
        {
            var latestLog = Path.Combine(gameDir, "logs", "latest.log");
            if (File.Exists(latestLog))
            {
                sb.AppendLine("\n===== latest.log =====");
                var fi = new FileInfo(latestLog);
                const int maxBytes = 300 * 1024;
                if (fi.Length > maxBytes)
                {
                    using var fs = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fs.Seek(-maxBytes, SeekOrigin.End);
                    using var sr = new StreamReader(fs);
                    sb.Append(sr.ReadToEnd());
                }
                else
                {
                    sb.Append(File.ReadAllText(latestLog));
                }
            }
        }
        catch { /* 忽略 */ }

        // 最新 crash-report
        try
        {
            var crashDir = Path.Combine(gameDir, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                var latest = Directory.GetFiles(crashDir, "crash-*.txt", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();
                if (latest != null)
                {
                    sb.AppendLine("\n===== crash-report =====");
                    sb.Append(File.ReadAllText(latest));
                }
            }
        }
        catch { /* 忽略 */ }

        return sb.ToString();
    }

    /// <summary>
    /// PCL2 风格错误识别：从崩溃日志中匹配常见错误模式，返回用户可读的中文原因。
    /// 用于在启动失败时告知用户具体问题，并配合自动修复。
    /// </summary>
    public static string AnalyzeError(string logContent)
    {
        if (string.IsNullOrWhiteSpace(logContent)) return "未知错误";

        // 内存不足
        if (Regex.IsMatch(logContent,
                @"OutOfMemoryError|Could not reserve enough space|Invalid maximum heap size|unable to create new native thread",
                RegexOptions.IgnoreCase))
            return "内存分配失败：请调低游戏内存，或检查系统可用内存是否充足。";

        // Java 版本过低
        if (Regex.IsMatch(logContent,
                @"UnsupportedClassVersionError|Unsupported major\.minor version|class file has wrong version",
                RegexOptions.IgnoreCase))
            return "Java 版本过低：请使用更高版本的 Java 运行此游戏（建议开启自动选择 Java）。";

        // 无法加载主类（依赖损坏/缺失）
        if (Regex.IsMatch(logContent,
                @"Could not find or load main class|Error: A JNI error has occurred|NoClassDefFoundError",
                RegexOptions.IgnoreCase))
            return "无法加载游戏主类：游戏依赖可能缺失或损坏。";

        // 缺少依赖库
        if (Regex.IsMatch(logContent,
                @"ClassNotFoundException|NoClassDefFoundError|A required class was missing",
                RegexOptions.IgnoreCase))
            return "缺少游戏依赖库：正在尝试自动补全…";

        // 显卡 / 窗口创建失败
        if (Regex.IsMatch(logContent,
                @"GLFW error|Failed to create window|Pixel format not accelerated|Bad video driver|Couldn't set pixel format|GLXBadFBConfig|eglGetError",
                RegexOptions.IgnoreCase))
            return "显卡或窗口创建失败：请更新显卡驱动，或检查显卡是否支持当前渲染要求。";

        // 模组依赖缺失 / 冲突
        if (Regex.IsMatch(logContent,
                @"Missing mod|Missing required mods|ModResolutionException|Incompatible mods|fabric-api|requires mod|requires fabric",
                RegexOptions.IgnoreCase))
            return "模组依赖缺失或冲突：正在尝试自动添加缺失模组…";

        // 登录失效
        if (Regex.IsMatch(logContent,
                @"Invalid session|Authentication failed|Please log in|User not authenticated|Invalid token",
                RegexOptions.IgnoreCase))
            return "账户登录已失效：请重新登录账户后再启动。";

        // 文件被占用
        if (Regex.IsMatch(logContent,
                @"Access is denied|Sharing violation|File already in use|IOException.*being used by another process|The process cannot access.*because it is being used",
                RegexOptions.IgnoreCase))
            return "游戏文件被占用：请关闭可能占用文件的程序（如残留的游戏进程）后重试。";

        // 磁盘空间不足
        if (Regex.IsMatch(logContent,
                @"No space left on device|磁盘空间不足|There is not enough space",
                RegexOptions.IgnoreCase))
            return "磁盘空间不足：请清理磁盘空间后重试。";

        // 文件损坏
        if (Regex.IsMatch(logContent,
                @"ZipException|CRC check failed|Invalid or corrupt jarfile|Bad magic number|Premature end of file",
                RegexOptions.IgnoreCase))
            return "游戏文件损坏：可能需要重新安装对应文件。";

        return "启动失败，请查看完整日志定位原因。";
    }

    public static async Task<(bool Repaired, string Message)> TryRepairAsync(string logContent, string versionId)
    {
        if (string.IsNullOrWhiteSpace(logContent)) return (false, "");

        // 1) 缺少依赖库 → 补全 libraries
        if (Regex.IsMatch(logContent,
                @"NoClassDefFoundError|ClassNotFoundException|Could not find or load main class|A required class was missing",
                RegexOptions.IgnoreCase))
        {
            var (ok, _) = await RepairMissingLibrariesAsync(versionId);
            if (ok) return (true, "补全了缺失的游戏依赖库");
        }

        // 2) 缺少 Fabric API / 其它模组依赖 → 自动识别并下载缺失模组
        if (Regex.IsMatch(logContent,
                @"fabric-api|Missing mod|Missing required mods|ModResolutionException|Incompatible mods|requires mod|requires fabric",
                RegexOptions.IgnoreCase))
        {
            var (ok, msg) = await TryRepairMissingModsAsync(logContent, versionId);
            if (ok) return (true, msg);
        }

        return (false, "");
    }

    // ===================== 补全缺失依赖库 =====================

    private static async Task<(bool Ok, string Msg)> RepairMissingLibrariesAsync(string versionId)
    {
        try
        {
            var libraries = LoadVersionLibraries(versionId);
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
                else url = $"https://libraries.minecraft.net/{libPath}";
                files.Add((new[] { url }, fullPath, lib.Downloads?.Artifact?.Size ?? 0));
            }

            if (files.Count == 0) return (false, "依赖库已齐全");

            var taskId = $"repair-libs-{versionId}";
            DownloadManager.Begin(taskId, $"{versionId} 依赖修复", "自愈");
            var ct = DownloadManager.GetTask(taskId)?.Cts?.Token ?? default;
            var result = await DownloadManager.DownloadFilesAsync(taskId, files, ct: ct);
            DownloadManager.Complete(taskId, result.Failed == 0, result.Failed > 0 ? result.FirstError : null);
            return (result.Failed == 0, result.FirstError ?? "");
        }
        catch
        {
            return (false, "补全依赖库失败");
        }
    }

    // ===================== 缺失模组自动下载 =====================

    /// <summary>从日志中提取缺失的模组标识（modid / slug），过滤掉 minecraft 等非模组项。</summary>
    private static HashSet<string> ExtractMissingModIds(string logContent)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 形如 "Missing mod: xxx" / "Missing required mods:" / "requires mod 'xxx'" / "Could not find a matching version of xxx"
        var patterns = new[]
        {
            @"Missing required mods?:[:\s]+[-•*]?\s*['""]?([\w\-_.]+)",
            @"Missing mods?:[:\s]+[-•*]?\s*['""]?([\w\-_.]+)",
            @"requires mods?:?\s*['""]?([\w\-_.]+)",
            @"Could not find a matching version of ['""]?([\w\-_.]+)",
        };
        foreach (var p in patterns)
        {
            foreach (Match m in Regex.Matches(logContent, p, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var id = m.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(id) && !IsSystemMod(id)) ids.Add(id);
            }
        }

        // "Missing mods:" 之后每行 "- xxx" 的列表
        var head = Regex.Match(logContent, @"Missing mods?:", RegexOptions.IgnoreCase);
        if (head.Success)
        {
            var rest = logContent[head.Index..];
            foreach (Match line in Regex.Matches(rest, @"^\s*[-•*]\s*['""]?([\w\-_.]+)", RegexOptions.Multiline))
            {
                var id = line.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(id) && !IsSystemMod(id)) ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Minecraft 加载器相关的系统组件，不是需要下载的模组。</summary>
    private static bool IsSystemMod(string id)
    {
        var lower = id.ToLowerInvariant();
        return lower is "minecraft" or "fabricloader" or "fabric-loader" or "forge" or "neoforge"
            or "java" or "quilt-loader" or "minecraftjava";
    }

    /// <summary>尝试自动下载日志中缺失的全部模组，返回是否修复成功及说明。</summary>
    private static async Task<(bool Ok, string Msg)> TryRepairMissingModsAsync(string logContent, string versionId)
    {
        try
        {
            var missingIds = ExtractMissingModIds(logContent);
            if (missingIds.Count == 0) return (false, "未从日志中识别到缺失模组");

            var mcVersion = ResolveMcVersion(versionId);
            if (string.IsNullOrEmpty(mcVersion)) return (false, "无法确定游戏版本");

            var loader = DetectLoader(versionId);
            if (string.IsNullOrEmpty(loader)) return (false, "无法确定模组加载器");

            var modsDir = Store.ResolveGamePath("mods", versionId);
            Store.EnsureDir(modsDir);

            var repaired = 0;
            var skipped = new List<string>();
            foreach (var id in missingIds)
            {
                var ok = await DownloadMissingModAsync(id, modsDir, mcVersion, loader);
                if (ok) repaired++;
                else skipped.Add(id);
            }

            if (repaired > 0)
                return (true, $"已自动添加 {repaired} 个缺失模组" + (skipped.Count > 0 ? $"；{skipped.Count} 个未找到适配版本" : ""));
            return (false, "");
        }
        catch
        {
            return (false, "自动添加模组失败");
        }
    }

    /// <summary>搜索并下载单个缺失模组到 mods 目录（精确 slug → 模糊搜索）。</summary>
    private static async Task<bool> DownloadMissingModAsync(string id, string modsDir, string mcVersion, string loader)
    {
        try
        {
            // 已安装同名模组则跳过
            if (Directory.Exists(modsDir) && Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly)
                .Any(f => Path.GetFileNameWithoutExtension(f).IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0))
                return false;

            var project = await ModrinthService.GetProjectAsync(id)
                ?? (await ModrinthService.SearchAsync(id, ModrinthService.ResourceKind.Mod, 5)).Hits?.FirstOrDefault();
            if (project == null) return false;

            var versions = await ModrinthService.GetVersionsAsync(project.ProjectId);
            var chosen = versions.FirstOrDefault(v => v.GameVersions.Contains(mcVersion) && v.Loaders.Contains(loader))
                ?? versions.FirstOrDefault(v => v.Loaders.Contains(loader) && v.VersionType == "release")
                ?? versions.FirstOrDefault(v => v.GameVersions.Contains(mcVersion));
            if (chosen == null) return false;

            var file = chosen.Files.FirstOrDefault(f => f.Primary) ?? chosen.Files.FirstOrDefault();
            if (file == null) return false;

            var fileName = string.IsNullOrWhiteSpace(file.Filename) ? $"{project.Slug}.jar" : Path.GetFileName(file.Filename);
            var dest = Path.Combine(modsDir, fileName);
            if (File.Exists(dest)) return false;
            await Downloader.DownloadFileAsync(file.Url, dest, file.Size);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>确定版本使用的模组加载器（fabric / forge / neoforge 等）。</summary>
    private static string? DetectLoader(string versionId)
    {
        var jsonPath = Path.Combine(Store.GetVersionsDir(), versionId, $"{versionId}.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var vd = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath), Store.JsonReadOpts);
                if (vd != null) return VersionsService.DetectLoader(vd);
            }
            catch { /* 忽略 */ }
        }
        return null;
    }

    // ===================== 工具 =====================

    /// <summary>确定版本对应的原版 Minecraft 版本号（Fabric profile 通过 inheritsFrom 指向原版）。</summary>
    private static string ResolveMcVersion(string versionId)
    {
        var jsonPath = Path.Combine(Store.GetVersionsDir(), versionId, $"{versionId}.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var vd = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath), Store.JsonReadOpts);
                if (!string.IsNullOrEmpty(vd?.InheritsFrom)) return vd.InheritsFrom;
            }
            catch { /* 忽略 */ }
        }
        return versionId;
    }

    /// <summary>加载版本依赖库列表（含父版本继承合并）。</summary>
    private static List<VersionLibrary> LoadVersionLibraries(string versionId)
    {
        var jsonPath = Path.Combine(Store.GetVersionsDir(), versionId, $"{versionId}.json");
        if (!File.Exists(jsonPath)) return new();
        var data = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath), Store.JsonReadOpts);
        if (data == null) return new();

        var merged = new List<VersionLibrary>(data.Libraries ?? new());
        if (!string.IsNullOrEmpty(data.InheritsFrom))
        {
            var parentJsonPath = Path.Combine(Store.GetVersionsDir(), data.InheritsFrom, $"{data.InheritsFrom}.json");
            if (File.Exists(parentJsonPath))
            {
                try
                {
                    var parent = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(parentJsonPath), Store.JsonReadOpts);
                    if (parent != null)
                    {
                        var childNames = merged.Select(l => l.Name).ToHashSet();
                        foreach (var pl in parent.Libraries ?? new())
                            if (!childNames.Contains(pl.Name)) merged.Add(pl);
                    }
                }
                catch { /* 忽略 */ }
            }
        }
        return merged;
    }

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
}