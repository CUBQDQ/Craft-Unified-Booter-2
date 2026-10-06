// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

namespace CraftUnifiedBooter.Services;

/// <summary>皮肤库预设：名字 + 本地贴图路径（皮肤文件统一保存在 CUB/skins）</summary>
public class SkinPreset
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>classic | slim</summary>
    public string Variant { get; set; } = "classic";
    /// <summary>内置（史蒂夫/艾利克斯）不可删除</summary>
    public bool Builtin { get; set; }
    /// <summary>来自微软账户同步</summary>
    public bool Synced { get; set; }
}

/// <summary>皮肤库预设：披风</summary>
public class CapePreset
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Builtin { get; set; }
    public bool Synced { get; set; }
}

/// <summary>皮肤库存储：预设皮肤/披风列表（CUB/skins.json），文件本体存 CUB/skins、CUB/capes</summary>
public static class SkinStore
{
    private class Data
    {
        public List<SkinPreset> Skins { get; set; } = new();
        public List<CapePreset> Capes { get; set; } = new();
    }

    private static readonly object Lock = new();
    private static Data _data = Load();

    public static event Action? Changed;

    private static Data Load()
    {
        var d = Store.ReadJson<Data>("skins.json") ?? new Data();
        d.Skins ??= new();
        d.Capes ??= new();
        return d;
    }

    public static void Save()
    {
        lock (Lock)
        {
            Store.WriteJson("skins.json", _data);
            Changed?.Invoke();
        }
    }

    /// <summary>皮肤预设（内置排前）</summary>
    public static List<SkinPreset> Skins
    {
        get { EnsureBuiltins(); return _data.Skins; }
    }

    /// <summary>披风预设</summary>
    public static List<CapePreset> Capes
    {
        get { EnsureBuiltins(); return _data.Capes; }
    }

    /// <summary>确保内置官方默认皮肤（9 个角色）预设存在</summary>
    public static void EnsureBuiltins()
    {
        var changed = false;
        void Add(string name, string path, string variant)
        {
            // 清理历史遗留：早期版本的默认皮肤曾存放在 skin_cache 下，会与新的内置项重名重复
            var stale = _data.Skins.Where(s => s.Builtin && s.Name == name && s.Path != path).ToList();
            foreach (var s in stale)
            {
                _data.Skins.Remove(s);
                changed = true;
            }

            if (_data.Skins.Any(s => s.Path == path)) return;
            _data.Skins.Insert(0, new SkinPreset
            {
                Name = name, Path = path, Variant = variant, Builtin = true,
            });
            changed = true;
        }

        foreach (var d in SkinService.DefaultSkins)
            Add(d.Name, SkinService.EnsureDefaultSkin(d.FileName), d.Variant);

        if (changed) Save();
    }

    /// <summary>添加本地皮肤文件（复制进 CUB/skins），返回新预设</summary>
    public static SkinPreset ImportSkin(string sourceFile, string? name, string variant)
    {
        Store.EnsureDir(Store.CubSkinsDir);
        var ext = Path.GetExtension(sourceFile);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
        var dest = Path.Combine(Store.CubSkinsDir, fileName);
        File.Copy(sourceFile, dest, true);

        var preset = new SkinPreset
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(sourceFile) : name.Trim(),
            Path = dest,
            Variant = variant,
        };
        lock (Lock)
        {
            _data.Skins.Add(preset);
            Store.WriteJson("skins.json", _data);
            Changed?.Invoke();
        }
        return preset;
    }

    /// <summary>添加本地披风文件（复制进 CUB/capes），返回新预设</summary>
    public static CapePreset ImportCape(string sourceFile, string? name)
    {
        Store.EnsureDir(Store.CubCapesDir);
        var fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}.png";
        var dest = Path.Combine(Store.CubCapesDir, fileName);
        File.Copy(sourceFile, dest, true);

        var preset = new CapePreset
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(sourceFile) : name.Trim(),
            Path = dest,
        };
        lock (Lock)
        {
            _data.Capes.Add(preset);
            Store.WriteJson("skins.json", _data);
            Changed?.Invoke();
        }
        return preset;
    }

    /// <summary>登记微软同步下来的皮肤（同一路径只登记一次）</summary>
    public static void RegisterSyncedSkin(string path, string variant)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (_data.Skins.Any(s => s.Path == path)) return;
        lock (Lock)
        {
            _data.Skins.Add(new SkinPreset
            {
                Name = "微软皮肤", Path = path, Variant = variant, Synced = true,
            });
            Store.WriteJson("skins.json", _data);
        }
    }

    /// <summary>登记微软同步下来的披风（同一路径只登记一次，多个披风按序号命名）</summary>
    public static void RegisterSyncedCape(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (_data.Capes.Any(c => c.Path == path)) return;
        lock (Lock)
        {
            var n = _data.Capes.Count(c => c.Synced);
            _data.Capes.Add(new CapePreset
            {
                Name = n == 0 ? "微软披风" : $"微软披风 {n + 1}",
                Path = path,
                Synced = true,
            });
            Store.WriteJson("skins.json", _data);
        }
    }

    /// <summary>删除预设（内置不可删）</summary>
    public static void RemoveSkin(SkinPreset preset)
    {
        if (preset.Builtin) return;
        lock (Lock)
        {
            _data.Skins.RemoveAll(s => s.Path == preset.Path);
        }
        Save();
    }

    public static void RemoveCape(CapePreset preset)
    {
        if (preset.Builtin) return;
        lock (Lock)
        {
            _data.Capes.RemoveAll(c => c.Path == preset.Path);
        }
        Save();
    }
}