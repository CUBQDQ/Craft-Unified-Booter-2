// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftUnifiedBooter.Services;

/// <summary>Modrinth 资源搜索 / 安装（Mod、整合包、数据包、资源包、光影包）</summary>
public static class ModrinthService
{
    public const string ApiBase = "https://api.modrinth.com/v2";

    public enum ResourceKind { Mod, Modpack, Datapack, Resourcepack, Shader }

    /// <summary>资源类型 → Modrinth project_type facet 值</summary>
    public static string ToFacet(ResourceKind kind) => kind switch
    {
        ResourceKind.Mod => "mod",
        ResourceKind.Modpack => "modpack",
        ResourceKind.Datapack => "datapack",
        ResourceKind.Resourcepack => "resourcepack",
        ResourceKind.Shader => "shader",
        _ => "mod",
    };

    /// <summary>资源类型 → 游戏目录子文件夹</summary>
    public static string ToFolder(ResourceKind kind) => kind switch
    {
        ResourceKind.Mod => "mods",
        ResourceKind.Modpack => "modpacks",
        ResourceKind.Datapack => "datapacks",
        ResourceKind.Resourcepack => "resourcepacks",
        ResourceKind.Shader => "shaderpacks",
        _ => "mods",
    };

    public static string KindLabel(ResourceKind kind) => kind switch
    {
        ResourceKind.Mod => "Mod",
        ResourceKind.Modpack => "整合包",
        ResourceKind.Datapack => "数据包",
        ResourceKind.Resourcepack => "资源包",
        ResourceKind.Shader => "光影包",
        _ => "Mod",
    };

    // ===================== 搜索 =====================

    private static readonly System.Text.RegularExpressions.Regex ChineseRegex =
        new("[\u4e00-\u9fbb]");

    public static async Task<(List<ModrinthHit>? Hits, string? Error)> SearchAsync(
        string query, ResourceKind kind, int limit = 20, int offset = 0)
    {
        try
        {
            var realQuery = (query ?? "").Trim();
            // 中文搜索（仿照 PCL2）：先在本地模组译名库中匹配中文关键词。
            // 若匹配到的模组有 Modrinth slug，直接精确获取该项目并放在结果首位，
            // 避免“多词长关键词”导致在线搜索无结果。
            if (ChineseRegex.IsMatch(realQuery) &&
                ModTranslationService.TryGetTopMatch(realQuery, out var directSlug, out var engKeywords))
            {
                if (!string.IsNullOrEmpty(directSlug))
                {
                    var direct = await GetProjectAsync(directSlug);
                    var related = await SearchInternalAsync(directSlug, kind, limit, offset);
                    if (direct != null)
                    {
                        var list = new List<ModrinthHit> { direct };
                        if (related.Hits != null)
                        {
                            foreach (var h in related.Hits)
                            {
                                if (h.ProjectId.Equals(direct.ProjectId, StringComparison.OrdinalIgnoreCase)) continue;
                                if (list.Count >= limit) break;
                                list.Add(h);
                            }
                        }
                        return (list, null);
                    }
                    if (related.Hits is { Count: > 0 }) return (related.Hits, null);
                    // 精确查询失败也无相关结果：降级继续用原始查询
                    realQuery = directSlug;
                }
                else if (!string.IsNullOrEmpty(engKeywords))
                {
                    realQuery = engKeywords;
                }
            }

            return await SearchInternalAsync(realQuery, kind, limit, offset);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static async Task<(List<ModrinthHit>? Hits, string? Error)> SearchInternalAsync(
        string query, ResourceKind kind, int limit = 20, int offset = 0)
    {
        try
        {
            var facets = Uri.EscapeDataString($"[[\"project_type:{ToFacet(kind)}\"]]");
            var url = $"{ApiBase}/search?query={Uri.EscapeDataString(query ?? "")}" +
                      $"&facets={facets}&limit={limit}&offset={offset}&index=relevance";
            var raw = await Downloader.Client.GetStringAsync(url);
            var resp = JsonSerializer.Deserialize<ModrinthSearchResponse>(raw, Store.JsonReadOpts);
            return (resp?.Hits ?? new List<ModrinthHit>(), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>按 slug 精确获取单个项目（中文搜索时保证目标模组一定出现在结果中）。</summary>
    public static async Task<ModrinthHit?> GetProjectAsync(string slug)
    {
        try
        {
            var url = $"{ApiBase}/project/{Uri.EscapeDataString(slug)}";
            var raw = await Downloader.Client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            return new ModrinthHit
            {
                ProjectId = GetStr(root, "id"),
                Slug = GetStr(root, "slug") is { Length: > 0 } s ? s : slug,
                Title = GetStr(root, "title"),
                Description = GetStr(root, "description"),
                Author = GetStr(root, "author"),
                Downloads = GetLong(root, "downloads"),
                Follows = GetLong(root, "followers"),
                IconUrl = root.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() : null,
                ProjectType = GetStr(root, "project_type"),
                Versions = GetStrList(root, "game_versions"),
                Categories = GetStrList(root, "categories"),
                DateModified = GetStr(root, "updated"),
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long GetLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static List<string> GetStrList(JsonElement el, string name)
    {
        var list = new List<string>();
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    list.Add(item.GetString() ?? "");
            }
        }
        return list;
    }

    // ===================== 版本查询 =====================

    public static async Task<List<ModrinthVersion>> GetVersionsAsync(string projectId)
    {
        try
        {
            var url = $"{ApiBase}/project/{Uri.EscapeDataString(projectId)}/version";
            var raw = await Downloader.Client.GetStringAsync(url);
            return JsonSerializer.Deserialize<List<ModrinthVersion>>(raw, Store.JsonReadOpts) ?? new();
        }
        catch
        {
            return new List<ModrinthVersion>();
        }
    }

    // ===================== 安装 =====================

    /// <summary>安装资源：挑选匹配游戏版本的发布文件并下载到对应目录</summary>
    public static async Task<(bool Success, string? Error)> InstallAsync(
        ModrinthHit hit, ResourceKind kind, string? gameVersion = null)
    {
        var label = KindLabel(kind);
        var taskId = $"modrinth-{hit.ProjectId}";
        DownloadManager.Begin(taskId, hit.DisplayTitle, label);
        var stage = "查询版本";
        try
        {
            var versions = await GetVersionsAsync(hit.ProjectId);
            if (versions.Count == 0)
            {
                DownloadManager.Complete(taskId, false, "未找到可用文件");
                return (false, "未找到可用文件");
            }

            ModrinthVersion? chosen = null;
            if (!string.IsNullOrEmpty(gameVersion))
                chosen = versions.FirstOrDefault(v => v.GameVersions.Contains(gameVersion));
            chosen ??= versions.FirstOrDefault(v => v.VersionType == "release") ?? versions[0];

            var file = chosen.Files.FirstOrDefault(f => f.Primary) ?? chosen.Files.FirstOrDefault();
            if (file == null)
            {
                DownloadManager.Complete(taskId, false, "该项目没有可下载的文件");
                return (false, "该项目没有可下载的文件");
            }

            var dir = Store.ResolveGamePath(ToFolder(kind), gameVersion ?? "");
            Store.EnsureDir(dir);
            var fileName = Path.GetFileName(file.Filename);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = $"{hit.Slug}.jar";
            var dest = Path.Combine(dir, fileName);

            stage = $"下载 {dest}";
            var task = DownloadManager.GetTask(taskId);
            if (task != null) task.Total = file.Size;
            await Downloader.DownloadFileAsync(file.Url, dest, file.Size, (total, downloaded) =>
            {
                DownloadManager.Report(taskId, total, downloaded);
            });

            DownloadManager.Complete(taskId, true);
            return (true, null);
        }
        catch (Exception ex)
        {
            var msg = $"[{stage}] {ex.GetType().Name}: {ex.Message}";
            DownloadManager.Complete(taskId, false, msg);
            return (false, msg);
        }
    }
}

// ===================== 数据模型 =====================

public class ModrinthSearchResponse
{
    [JsonPropertyName("hits")] public List<ModrinthHit> Hits { get; set; } = new();
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
}

public class ModrinthHit
{
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("downloads")] public long Downloads { get; set; }
    [JsonPropertyName("follows")] public long Follows { get; set; }
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "";
    [JsonPropertyName("versions")] public List<string> Versions { get; set; } = new();
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("date_modified")] public string? DateModified { get; set; }

    /// <summary>显示名称：数据库有中文译名则用中文，否则为英文标题（仿照 PCL2 的模组译名机制）。</summary>
    public string DisplayTitle => ModTranslationService.GetName(Slug, Title);
}

public class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
    [JsonPropertyName("version_type")] public string VersionType { get; set; } = "";
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = new();
    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = new();
}

public class ModrinthFile
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("primary")] public bool Primary { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}