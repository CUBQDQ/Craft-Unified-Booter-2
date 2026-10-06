// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Text.Json;

namespace CraftUnifiedBooter.Services;

/// <summary>启动器目录与 JSON 读写（对应原版 electron/store.ts）</summary>
public static class Store
{
    /// <summary>exe 所在目录（开发/发布一致），CUB 与 .minecraft 都建在这里</summary>
    public static readonly string AppDir = AppContext.BaseDirectory;
    public static readonly string CubDir = Path.Combine(AppDir, "CUB");
    public static readonly string MinecraftDir = Path.Combine(AppDir, ".minecraft");
    public static readonly string CubJavaDir = Path.Combine(CubDir, "java");
    public static readonly string CubSkinCache = Path.Combine(CubDir, "skin_cache");
    public static readonly string CubLogsDir = Path.Combine(CubDir, "logs");
    /// <summary>皮肤库：本地皮肤文件</summary>
    public static readonly string CubSkinsDir = Path.Combine(CubDir, "skins");
    /// <summary>皮肤库：本地披风文件</summary>
    public static readonly string CubCapesDir = Path.Combine(CubDir, "capes");
    /// <summary>主题：用户自定义背景图片目录</summary>
    public static readonly string CubBackgroundsDir = Path.Combine(CubDir, "backgrounds");

    static Store()
    {
        EnsureDir(CubDir);
        EnsureDir(MinecraftDir);
        foreach (var sub in new[] { "versions", "libraries", "assets", "resourcepacks", "shaderpacks", "mods", "saves", "screenshots", "logs" })
            EnsureDir(Path.Combine(MinecraftDir, sub));
        EnsureDir(Path.Combine(CubDir, "loaders"));
        EnsureDir(CubSkinCache);
        EnsureDir(CubSkinsDir);
        EnsureDir(CubCapesDir);
        EnsureDir(CubJavaDir);
        EnsureDir(CubLogsDir);
        EnsureDir(CubBackgroundsDir);
    }

    /// <summary>返回 CUB/backgrounds 目录下所有背景图（jpg/png/bmp/webp），按文件名排序。</summary>
    public static List<string> GetBackgroundImagePaths()
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(CubBackgroundsDir)) return list;
            var patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" };
            foreach (var p in patterns)
                list.AddRange(Directory.GetFiles(CubBackgroundsDir, p));
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch { /* ignore */ }
        return list;
    }

    public static void EnsureDir(string dir)
    {
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>反序列化统一选项：外部 JSON（Mojang 清单/版本详情）为 camelCase，需大小写不敏感</summary>
    public static readonly JsonSerializerOptions JsonReadOpts = new() { PropertyNameCaseInsensitive = true };

    public static T? ReadJson<T>(string filename) where T : class
    {
        var path = Path.Combine(CubDir, filename);
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonReadOpts);
        }
        catch { /* 损坏时回退默认 */ }
        return null;
    }

    public static void WriteJson(string filename, object data)
    {
        EnsureDir(CubDir);
        var path = Path.Combine(CubDir, filename);
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>当前激活的游戏根目录（.minecraft 级，含 versions/libraries/assets）</summary>
    public static string GetActiveGameRoot()
    {
        var root = SettingsStore.Current.GameDir;
        if (!string.IsNullOrWhiteSpace(root)) return root.Trim();
        return MinecraftDir;
    }

    public static string GetVersionsDir() => Path.Combine(GetActiveGameRoot(), "versions");
    public static string GetLibrariesDir() => Path.Combine(GetActiveGameRoot(), "libraries");
    public static string GetAssetsDir() => Path.Combine(GetActiveGameRoot(), "assets");

    /// <summary>解析游戏子目录（版本隔离开启时定位到 versions/&lt;ver&gt;/&lt;type&gt;）</summary>
    public static string ResolveGamePath(string type, string versionId)
    {
        var isolation = SettingsStore.Current.VersionIsolation;
        var root = GetActiveGameRoot();
        if (!string.IsNullOrEmpty(versionId) && isolation)
        {
            var vRoot = Path.Combine(root, "versions", versionId);
            return type == "root" ? vRoot : Path.Combine(vRoot, type);
        }
        return type == "root" ? root : Path.Combine(root, type);
    }
}
