// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CraftUnifiedBooter.Services;

/// <summary>
/// 拖放文件导入（仿 PCL2）：把 zip 整合包 / mrpack / jar 模组 / json 版本 / litemod
/// 直接拖进启动器窗口即可自动安装。
/// Modrinth 整合包（.mrpack 或含 modrinth.index.json 的 .zip）会自动安装对应的
/// Minecraft + 加载器版本，并下载全部 Mod 与配置文件。
/// </summary>
public static class ImportService
{
    public static async Task<string> ImportAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".zip" => await ImportPackAsync(filePath),
            ".mrpack" => await ImportMrpackAsync(filePath),
            ".jar" => await ImportModAsync(filePath),
            ".litemod" => await ImportLiteModAsync(filePath),
            ".json" => await ImportVersionJsonAsync(filePath),
            _ => throw new System.Exception($"不支持的文件类型：{ext}（支持 zip / mrpack / jar / litemod / json）"),
        };
    }

    // ===================== 整合包 .zip =====================
    private static async Task<string> ImportPackAsync(string zipPath)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "cub_import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tmp));

            // 扩展名是 .zip 但内容是 Modrinth 整合包 → 走完整 mrpack 导入（自动安装版本）
            if (File.Exists(Path.Combine(tmp, "modrinth.index.json")))
            {
                var msg = await ImportMrpackCoreAsync(tmp);
                Directory.Delete(tmp, true);
                return msg;
            }

            // 定位游戏内容根目录（兼容 .minecraft 前缀 / versions 直接裸露 / 单层包名文件夹包裹）
            var source = FindContentRoot(tmp);
            if (source == null)
            {
                // 不是标准整合包结构：退化为把 mods/config 等复制到游戏根目录
                return ImportContentPack(tmp);
            }

            var root = Store.GetActiveGameRoot();
            Store.EnsureDir(root);

            var versionsDir = Path.Combine(source, "versions");
            var hasVersions = Directory.Exists(versionsDir) && Directory.GetDirectories(versionsDir).Length > 0;
            string? newVersion = null;
            if (hasVersions)
            {
                newVersion = Directory.GetDirectories(versionsDir)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(id => !string.IsNullOrEmpty(id));
            }

            CopyDirectory(source, root);
            Directory.Delete(tmp, true);

            return newVersion != null
                ? $"已导入整合包，新版本：{newVersion}（可在版本管理中查看）"
                : "整合包内容已导入（mods / config / 资源包等）";
        }
        catch (Exception ex)
        {
            try { Directory.Delete(tmp, true); } catch { }
            throw new Exception($"导入整合包失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 在解压目录中定位“.minecraft 内容根”：
    /// 直接识别 <see cref="tmp"/>.minecraft / versions / mods；
    /// 也兼容“{包名}/.minecraft”或“{包名}/versions”的单层文件夹包裹结构。
    /// </summary>
    private static string? FindContentRoot(string dir)
    {
        var mc = Path.Combine(dir, ".minecraft");
        if (Directory.Exists(mc)) return mc;
        if (Directory.Exists(Path.Combine(dir, "versions")) || Directory.Exists(Path.Combine(dir, "mods")))
            return dir;

        foreach (var sub in Directory.GetDirectories(dir))
        {
            var subMc = Path.Combine(sub, ".minecraft");
            if (Directory.Exists(subMc)) return subMc;
            if (Directory.Exists(Path.Combine(sub, "versions")) || Directory.Exists(Path.Combine(sub, "mods")))
                return sub;
        }
        return null;
    }

    /// <summary>整合包不含 versions 时，把 mods / config / resourcepacks 等内容复制到游戏根目录。</summary>
    private static string ImportContentPack(string sourceDir)
    {
        var root = Store.GetActiveGameRoot();
        Store.EnsureDir(root);

        // 只复制内容目录，避免把无关的包名文件夹也拷进去
        foreach (var sub in new[] { "mods", "config", "resourcepacks", "shaderpacks", "saves", "scripts", "kubejs" })
        {
            var src = Path.Combine(sourceDir, sub);
            if (Directory.Exists(src))
            {
                var dst = Path.Combine(root, sub);
                Store.EnsureDir(dst);
                CopyDirectory(src, dst);
            }
        }
        return "整合包不包含版本文件，已将 mods / config 等内容导入游戏目录（不会新增版本）";
    }

    // ===================== Modrinth 整合包（.mrpack 或 .zip 内容） =====================

    private static async Task<string> ImportMrpackAsync(string mrpackPath)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "cub_mrpack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(mrpackPath, tmp));
            var msg = await ImportMrpackCoreAsync(tmp);
            Directory.Delete(tmp, true);
            return msg;
        }
        catch (Exception ex)
        {
            try { Directory.Delete(tmp, true); } catch { }
            throw new Exception($"导入 Modrinth 整合包失败：{ex.Message}");
        }
    }

    /// <summary>解析已解压的 mrpack：自动安装 MC+加载器版本，再下载 Mod 与复制 overrides。</summary>
    private static async Task<string> ImportMrpackCoreAsync(string tmp)
    {
        var indexPath = Path.Combine(tmp, "modrinth.index.json");
        if (!File.Exists(indexPath)) throw new Exception("不是有效的 Modrinth 整合包（缺少 modrinth.index.json）");

        var index = JsonSerializer.Deserialize<MrpackIndex>(
            await File.ReadAllTextAsync(indexPath), Store.JsonReadOpts);
        if (index?.Files == null) throw new Exception("整合包描述文件不完整");

        var root = Store.GetActiveGameRoot();
        Store.EnsureDir(root);

        // 1) 根据依赖安装 Minecraft + 加载器版本（生成新版本）
        var mcVersion = index.Dependencies?.Minecraft;
        var loader = DetectLoader(index.Dependencies);
        string? installId = null;

        if (!string.IsNullOrWhiteSpace(mcVersion) && !string.IsNullOrWhiteSpace(loader))
        {
            installId = SanitizeVersionId(string.IsNullOrWhiteSpace(index.Name) ? $"{mcVersion}-{loader}" : index.Name.Trim());
            if (string.IsNullOrWhiteSpace(installId) || installId == "mrpack")
                installId = $"{mcVersion}-{loader}";
            if (string.Equals(installId, mcVersion, StringComparison.OrdinalIgnoreCase))
                installId = $"{mcVersion}-{loader}";

            var (ok, err) = await LoaderService.InstallAsync(mcVersion, loader, installId);
            if (!ok) throw new Exception($"版本安装失败（{mcVersion} + {loader}）：{err}");
        }

        // 2) 确定文件落点：有版本时放入版本目录（版本隔离开启）/ 游戏根目录（关闭）
        var contentRoot = Store.ResolveGamePath("root", installId ?? SettingsStore.Current.SelectedVersion ?? "");

        // 3) 复制 overrides（config、资源包、存档等）
        var overridesDir = Path.Combine(tmp, "overrides");
        if (Directory.Exists(overridesDir)) CopyDirectory(overridesDir, contentRoot);

        // 4) 下载依赖 Mod 文件（仅 client 环境支持），注册到下载管理器显示进度
        var modsDir = Store.ResolveGamePath("mods", installId ?? SettingsStore.Current.SelectedVersion ?? "");
        Store.EnsureDir(modsDir);

        var modFiles = new List<(string Url, string Dest, long Size)>();
        foreach (var f in index.Files)
        {
            if (f.Env?.Client == "unsupported") continue;
            if (string.IsNullOrWhiteSpace(f.Path)) continue;
            var url = f.Downloads?.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
            if (string.IsNullOrEmpty(url)) continue;
            var dest = Path.Combine(modsDir, Path.GetFileName(f.Path)); // mods 目录扁平存放
            if (File.Exists(dest)) continue;
            modFiles.Add((url!, dest, f.FileSize ?? 0));
        }

        var downloaded = 0;
        if (modFiles.Count > 0)
        {
            var taskId = $"import-{(installId ?? "mrpack")}";
            var task = DownloadManager.Begin(taskId, $"{index.Name} 的 Mod", "整合包");
            task.TotalFiles = modFiles.Count;
            long totalSize = modFiles.Sum(m => m.Size);
            long downloadedBytes = 0;
            int failed = 0;

            foreach (var mf in modFiles)
            {
                try
                {
                    await Downloader.DownloadFileWithCandidatesAsync(new[] { mf.Url }, mf.Dest);
                }
                catch
                {
                    failed++; // 单个 Mod 失败不中断整个导入
                }
                finally
                {
                    downloaded++;
                    downloadedBytes += mf.Size;
                    if (DownloadManager.GetTask(taskId) is { } t)
                        t.DownloadedFiles = downloaded;
                    DownloadManager.Report(taskId, totalSize, downloadedBytes);
                }
            }
            DownloadManager.Complete(taskId, failed == 0,
                failed > 0 ? $"{failed} 个 Mod 下载失败" : null);
        }

        return installId != null
            ? $"已导入整合包「{index.Name}」，版本：{installId}（{downloaded} 个 Mod）"
            : $"已导入整合包内容「{index.Name}」（未生成新版本）";
    }

    /// <summary>从 mrpack 依赖中识别加载器类型。</summary>
    private static string? DetectLoader(MrpackDependencies? deps)
    {
        if (deps == null) return null;
        if (!string.IsNullOrWhiteSpace(deps.FabricLoader)) return "fabric";
        if (!string.IsNullOrWhiteSpace(deps.Forge)) return "forge";
        if (!string.IsNullOrWhiteSpace(deps.NeoForge)) return "neoforge";
        if (!string.IsNullOrWhiteSpace(deps.QuiltLoader)) return "quilt";
        return null;
    }

    /// <summary>把整合包名转换为合法的版本 ID（中文保留，非法字符替换为下划线）。</summary>
    private static string SanitizeVersionId(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (Path.GetInvalidFileNameChars().Contains(c) || c == ':' || c == '*' || c == '?')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    // ===================== Mod 模组 .jar =====================
    private static Task<string> ImportModAsync(string jarPath)
    {
        var versionId = SettingsStore.Current.SelectedVersion;
        if (string.IsNullOrWhiteSpace(versionId))
            throw new Exception("请先在主页选择一个版本，再把 Mod 拖进来");
        var modsDir = Store.ResolveGamePath("mods", versionId);
        Store.EnsureDir(modsDir);
        var dest = Path.Combine(modsDir, Path.GetFileName(jarPath));
        File.Copy(jarPath, dest, true);
        return Task.FromResult($"已安装 Mod：{Path.GetFileNameWithoutExtension(jarPath)} → {versionId}");
    }

    // ===================== LiteMod .litemod =====================
    private static Task<string> ImportLiteModAsync(string filePath)
    {
        var versionId = SettingsStore.Current.SelectedVersion;
        if (string.IsNullOrWhiteSpace(versionId))
            throw new Exception("请先在主页选择一个版本，再把 LiteMod 拖进来");
        var modsDir = Store.ResolveGamePath("mods", versionId);
        Store.EnsureDir(modsDir);
        var dest = Path.Combine(modsDir, Path.GetFileName(filePath));
        File.Copy(filePath, dest, true);
        return Task.FromResult($"已安装 LiteMod：{Path.GetFileNameWithoutExtension(filePath)} → {versionId}");
    }

    // ===================== 版本配置 .json =====================
    private static Task<string> ImportVersionJsonAsync(string jsonPath)
    {
        var id = Path.GetFileNameWithoutExtension(jsonPath);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                var v = idProp.GetString();
                if (!string.IsNullOrWhiteSpace(v)) id = v!;
            }
        }
        catch { /* 保持文件名作为 id */ }

        if (string.IsNullOrWhiteSpace(id)) throw new Exception("无法识别版本 ID");
        var versionDir = Path.Combine(Store.GetVersionsDir(), id);
        Store.EnsureDir(versionDir);
        var dest = Path.Combine(versionDir, $"{id}.json");
        File.Copy(jsonPath, dest, true);
        return Task.FromResult($"已导入版本配置：{id}（若缺少 {id}.jar，请把对应 jar 也拖进来）");
    }

    // ===================== 工具 =====================
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Store.EnsureDir(destDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Store.EnsureDir(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }
}

// ===================== mrpack 模型 =====================
public class MrpackIndex
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("versionId")] public string? VersionId { get; set; }
    [JsonPropertyName("files")] public List<MrpackFile>? Files { get; set; }
    [JsonPropertyName("dependencies")] public MrpackDependencies? Dependencies { get; set; }
}

public class MrpackDependencies
{
    [JsonPropertyName("minecraft")] public string? Minecraft { get; set; }
    [JsonPropertyName("fabric-loader")] public string? FabricLoader { get; set; }
    [JsonPropertyName("forge")] public string? Forge { get; set; }
    [JsonPropertyName("neoforge")] public string? NeoForge { get; set; }
    [JsonPropertyName("quilt-loader")] public string? QuiltLoader { get; set; }
}

public class MrpackFile
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("downloads")] public List<string>? Downloads { get; set; }
    [JsonPropertyName("fileSize")] public long? FileSize { get; set; }
    [JsonPropertyName("env")] public MrpackEnv? Env { get; set; }
}

public class MrpackEnv
{
    [JsonPropertyName("client")] public string? Client { get; set; }
    [JsonPropertyName("server")] public string? Server { get; set; }
}