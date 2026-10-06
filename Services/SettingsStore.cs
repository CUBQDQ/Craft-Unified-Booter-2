// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

namespace CraftUnifiedBooter.Services;

/// <summary>启动器设置（对应原版 electron/ipc/settings.ts 的 Settings + DEFAULT_SETTINGS），JSON 持久化到 CUB/settings.json</summary>
public class Settings
{
    public int MinMemory { get; set; } = 1024;
    public int MaxMemory { get; set; } = 4096;
    public int Width { get; set; } = 854;
    public int Height { get; set; } = 480;
    public bool Fullscreen { get; set; }
    public string JvmArgs { get; set; } = "";
    public string Theme { get; set; } = "cub";
    public bool AutoUpdate { get; set; } = true;
    public int DownloadThreads { get; set; } = 8;
    public bool KeepOpen { get; set; } = true;
    public string GameDir { get; set; } = "";
    public string JavaPath { get; set; } = "";
    public bool AutoMemory { get; set; } = true;
    public bool AutoJava { get; set; } = true;
    public bool VersionIsolation { get; set; } = true;
    public string Accent { get; set; } = "blue";
    public string Radius { get; set; } = "default";
    public string FontSize { get; set; } = "medium";
    public string SidebarWidth { get; set; } = "default";
    public string TitleBarHeight { get; set; } = "default";
    public bool PageAnimation { get; set; } = true;
    public bool ShowSidebarVersion { get; set; } = true;
    /// <summary>主题：使用 CUB/backgrounds 下的图片作为窗口背景</summary>
    public bool BackgroundImage { get; set; } = true;
    /// <summary>背景图切换模式：manual 手动切换 / auto 自动轮播</summary>
    public string BackgroundMode { get; set; } = "manual";
    /// <summary>当前背景图索引（多张图片时）</summary>
    public int BackgroundIndex { get; set; }
    /// <summary>自动轮播间隔（秒）</summary>
    public int BackgroundInterval { get; set; } = 30;
    /// <summary>背景图不透明度（0-100，100=完全不透明）</summary>
    public int BackgroundOpacity { get; set; } = 100;
    /// <summary>背景图模糊程度（0-30，0=不模糊）</summary>
    public int BackgroundBlur { get; set; } = 0;
    public int PageSize { get; set; } = 20;
    public bool DownloadNotify { get; set; }
    public bool MinimizeOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; }
    /// <summary>下载源：official 官方 / bmclapi 国内镜像 / auto 自动测速选最快</summary>
    public string DownloadSource { get; set; } = "auto";
    /// <summary>当前选中版本（主页启动用）</summary>
    public string SelectedVersion { get; set; } = "";
    /// <summary>开发者模式：连续点击右下角版本号 5 次开启，解锁图片背景与小程序</summary>
    public bool DeveloperMode { get; set; }
}

public static class SettingsStore
{
    private static readonly object Lock = new();

    public static Settings Current { get; private set; } = Load();

    /// <summary>设置被 Update 修改后触发（可用于即时应用个性化外观）。</summary>
    public static event Action? SettingsChanged;

    private static Settings Load()
    {
        var s = Store.ReadJson<Settings>("settings.json");
        return s ?? new Settings();
    }

    public static void Save()
    {
        lock (Lock) Store.WriteJson("settings.json", Current);
    }

    public static void Update(Action<Settings> action)
    {
        lock (Lock)
        {
            action(Current);
            Save();
        }
        SettingsChanged?.Invoke();
    }
}
