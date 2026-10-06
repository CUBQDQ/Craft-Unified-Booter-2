// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Text;

namespace CraftUnifiedBooter.Services;

/// <summary>
/// 模组中文名翻译（仿照 PCL2 的处理方式：内置 ModData 数据库，按 slug 匹配，
/// 显示时优先返回中文译名，未收录的模组保持英文标题）。
/// 同时支持中文搜索：把用户输入的中文关键词，先在数据库中匹配中文译名，
/// 转换成英文关键词后交给在线 API 搜索（与 PCL2 的中文搜索方法一致）。
/// </summary>
public static class ModTranslationService
{
    private static Dictionary<string, string>? _modNames;
    private static List<ModNameEntry>? _entries;
    /// <summary>英文标题 → 中文名（用于 slug 对不上时的回退匹配，覆盖只有 CurseForge slug 的条目）。</summary>
    private static Dictionary<string, string>? _englishNames;
    private static readonly object LockObj = new();

    private static void EnsureLoaded()
    {
        if (_modNames != null) return;
        lock (LockObj)
        {
            if (_modNames != null) return;
            var (map, list) = LoadDatabase();
            _modNames = map;
            _entries = list;

            var engMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in list)
            {
                var eng = ExtractEnglishName(e.ChineseName);
                if (!string.IsNullOrEmpty(eng) && !engMap.ContainsKey(eng))
                    engMap[eng] = e.ChineseName;
            }
            _englishNames = engMap;
        }
    }

    private static (Dictionary<string, string> Map, List<ModNameEntry> Entries) LoadDatabase()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ModNameEntry>(60000);
        try
        {
            // 数据库作为嵌入资源打包进 exe（单文件发布后仍可读取，不依赖外部文件）
            using var stream = typeof(ModTranslationService).Assembly
                .GetManifestResourceStream("CraftUnifiedBooter.Resources.ModData.txt");
            if (stream == null) return (map, list);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // 同一行可用 ¨ 分隔多个条目（同一模组的 CurseForge / Modrinth 两种记录）
                foreach (var entry in line.Split('¨'))
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    var parts = entry.Split('|');
                    if (parts.Length < 2) continue;
                    var slugPart = parts[0].Trim();
                    var chineseName = parts[1].Trim();
                    if (slugPart.Length == 0 || chineseName.Length == 0) continue;

                    // slug 格式（与 PCL2 一致）：
                    //   @slug  → 仅 Modrinth
                    //   slug@  → CurseForge 与 Modrinth 相同
                    //   a@b    → CurseForge=a，Modrinth=b
                    //   slug   → 仅 CurseForge
                    string? curseForge = null;
                    string? modrinth = null;
                    if (slugPart.StartsWith("@"))
                    {
                        modrinth = slugPart.TrimStart('@');
                    }
                    else if (slugPart.EndsWith("@"))
                    {
                        curseForge = slugPart.TrimEnd('@');
                        modrinth = curseForge;
                    }
                    else if (slugPart.Contains('@'))
                    {
                        var sp = slugPart.Split('@');
                        curseForge = sp[0];
                        modrinth = sp[1];
                    }
                    else
                    {
                        curseForge = slugPart;
                    }

                    // 中文名中的 * 表示用 slug 拆词补充英文名，如 林业* → 林业 (Forestry)
                    if (chineseName.Contains('*'))
                    {
                        var source = modrinth ?? curseForge ?? "";
                        var english = string.Join(" ", source.Split('-').Select(w =>
                            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w));
                        chineseName = chineseName.Replace("*", $" ({english})");
                    }

                    var entryObj = new ModNameEntry
                    {
                        ChineseName = chineseName,
                        CurseForgeSlug = curseForge,
                        ModrinthSlug = modrinth,
                    };
                    list.Add(entryObj);

                    if (!string.IsNullOrEmpty(curseForge) && !map.ContainsKey(curseForge))
                        map[curseForge] = chineseName;
                    if (!string.IsNullOrEmpty(modrinth) && !map.ContainsKey(modrinth))
                        map[modrinth] = chineseName;
                }
            }
        }
        catch
        {
            // 数据库加载失败时静默降级为英文显示
        }
        return (map, list);
    }

    /// <summary>返回模组显示名称：优先按 slug 匹配中文译名；slug 对不上时用英文标题回退匹配。</summary>
    public static string GetName(string? slug, string fallbackTitle)
    {
        EnsureLoaded();
        if (!string.IsNullOrEmpty(slug) && _modNames!.TryGetValue(slug, out var zh) && zh is not null)
            return zh;
        // 回退匹配：很多模组在数据库里只有 CurseForge slug（Modrinth slug 对不上），
        // 但中文名括号里带了英文标题（如 "钠 (Sodium)"），用英文标题同样能翻译。
        var title = fallbackTitle.Trim();
        if (title.Length > 0 && _englishNames!.TryGetValue(title, out var zh2) && zh2 is not null)
            return zh2;
        return fallbackTitle;
    }

    /// <summary>
    /// 中文搜索取最佳匹配（仿照 PCL2 的中文搜索思路）：
    /// 在本地译名库中找到与中文关键词最匹配的一个模组条目。
    /// 若该条目有 Modrinth slug 则直接返回它（可精确查询项目）；
    /// 否则返回由 CurseForge slug + 英文名构成的简短英文关键词。
    /// </summary>
    public static bool TryGetTopMatch(string chineseQuery, out string? modrinthSlug, out string? englishKeywords)
    {
        modrinthSlug = null;
        englishKeywords = null;
        if (string.IsNullOrWhiteSpace(chineseQuery)) return false;
        EnsureLoaded();
        var entries = _entries;
        if (entries == null || entries.Count == 0) return false;

        var query = chineseQuery.Trim();
        ModNameEntry? best = null;
        double bestScore = 0;
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.ChineseName)) continue;
            var score = ScoreMatch(e.ChineseName, query);
            if (score > bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        if (best == null) return false;

        modrinthSlug = best.ModrinthSlug;
        if (string.IsNullOrEmpty(modrinthSlug))
        {
            // 没有 Modrinth slug：用 CurseForge slug + 英文名生成简短关键词（避免长串导致在线搜索无结果）
            var words = new List<string>();
            if (!string.IsNullOrEmpty(best.CurseForgeSlug))
                words.Add(best.CurseForgeSlug.Replace('-', ' ').Replace('/', ' '));
            var eng = ExtractEnglishName(best.ChineseName);
            if (!string.IsNullOrEmpty(eng)) words.Add(eng);
            englishKeywords = string.Join(' ', words.Distinct(StringComparer.OrdinalIgnoreCase)).Trim();
        }
        return true;
    }

    /// <summary>
    /// 中文搜索转换（仿照 PCL2）：在本地译名库中匹配中文关键词，
    /// 把命中的模组 slug 与英文名拼接成英文关键词，供在线 API 使用。
    /// 只取最高分的 1 个条目，避免拼接过长关键词导致在线搜索无结果。
    /// </summary>
    public static bool TryBuildEnglishKeywords(string chineseQuery, out string keywords)
    {
        keywords = "";
        if (TryGetTopMatch(chineseQuery, out var modrinthSlug, out var engKeywords))
        {
            // 优先用 Modrinth slug 作为关键词（短且精确）
            keywords = (modrinthSlug ?? engKeywords ?? "").Replace('-', ' ').Replace('/', ' ');
            return keywords.Length > 0;
        }
        return false;
    }

    /// <summary>中文名与查询词的匹配评分（越高越精确）。</summary>
    private static double ScoreMatch(string chineseName, string query)
    {
        if (chineseName == query) return 100;
        if (chineseName.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 60;
        if (chineseName.Contains(query, StringComparison.OrdinalIgnoreCase)) return 50;
        if (query.Contains(chineseName)) return 30;

        var eng = ExtractEnglishName(chineseName);
        if (eng != null && eng.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 40;
        return 0;
    }

    /// <summary>从"中文名 (English Name)"格式中提取括号内的英文名。</summary>
    private static string? ExtractEnglishName(string chineseName)
    {
        var idx = chineseName.LastIndexOf('(');
        if (idx >= 0 && chineseName.EndsWith(')'))
        {
            var inner = chineseName.Substring(idx + 1, chineseName.Length - idx - 2);
            if (inner.Length > 0) return inner.Trim();
        }
        return null;
    }

    private sealed class ModNameEntry
    {
        public string ChineseName { get; init; } = "";
        public string? CurseForgeSlug { get; init; }
        public string? ModrinthSlug { get; init; }
    }
}