// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Windows;
using System.Windows.Media;

namespace CraftUnifiedBooter.Services;

/// <summary>主题工具：按当前"圆角风格"换算圆角、按当前"主色"提供画刷，供 XAML 动态资源与代码共用。</summary>
public static class UiTheme
{
    /// <summary>当前圆角缩放系数：直角 0 / 标准 1.0 / 大圆角 1.6。</summary>
    public static double RadiusScale => SettingsStore.Current.Radius switch
    {
        "none" => 0,
        "large" => 1.6,
        _ => 1.0,
    };

    /// <summary>按基准圆角换算当前风格下的圆角值。</summary>
    public static CornerRadius Radius(double baseRadius)
        => new CornerRadius(baseRadius * RadiusScale);

    /// <summary>按基准圆角换算当前风格下的圆角值。</summary>
    public static CornerRadius Radius(int baseRadius)
        => new CornerRadius(baseRadius * RadiusScale);

    /// <summary>主色 → 四档色板（主色 / 深色 / 浅色 / 极浅色）。</summary>
    public static (Color Main, Color Dark, Color Light, Color Soft) AccentColors(string accent) => accent switch
    {
        "violet" => (Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0xC4, 0xB5, 0xFD), Color.FromRgb(0xF5, 0xF3, 0xFF)),
        "green" => (Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0x16, 0xA3, 0x4A), Color.FromRgb(0x86, 0xEF, 0xAC), Color.FromRgb(0xEC, 0xFD, 0xF5)),
        "orange" => (Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xFD, 0xBA, 0x74), Color.FromRgb(0xFF, 0xF7, 0xED)),
        "pink" => (Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0xDB, 0x27, 0x77), Color.FromRgb(0xF9, 0xA8, 0xD4), Color.FromRgb(0xFD, 0xF2, 0xF8)),
        "cyan" => (Color.FromRgb(0x06, 0xB6, 0xD4), Color.FromRgb(0x08, 0x91, 0xB2), Color.FromRgb(0x67, 0xE8, 0xF9), Color.FromRgb(0xEC, 0xFE, 0xFF)),
        "red" => (Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0xDC, 0x26, 0x26), Color.FromRgb(0xFC, 0xA5, 0xA5), Color.FromRgb(0xFE, 0xF2, 0xF2)),
        "amber" => (Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0xFC, 0xD3, 0x4D), Color.FromRgb(0xFF, 0xFB, 0xEB)),
        _ => (Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x1D, 0x4E, 0xD8), Color.FromRgb(0x93, 0xC5, 0xFD), Color.FromRgb(0xEF, 0xF6, 0xFF)),
    };

    /// <summary>当前主色画刷（按钮、高亮）。</summary>
    public static Brush AccentBrush => Frozen(AccentColors(SettingsStore.Current.Accent).Main);

    /// <summary>当前主色深色画刷（悬停）。</summary>
    public static Brush AccentDarkBrush => Frozen(AccentColors(SettingsStore.Current.Accent).Dark);

    /// <summary>当前主色浅色画刷（边框、浅色文字）。</summary>
    public static Brush AccentLightBrush => Frozen(AccentColors(SettingsStore.Current.Accent).Light);

    /// <summary>当前主色极浅色画刷（选中背景、浅色底色）。</summary>
    public static Brush AccentSoftBrush => Frozen(AccentColors(SettingsStore.Current.Accent).Soft);

    /// <summary>卡片/面板背景（有背景图时为半透明白，让图片透出）。</summary>
    public static Brush CardBackground => FromResources("CardBackground", Brushes.White);

    /// <summary>页面根背景（有背景图时半透明）。</summary>
    public static Brush PageBackgroundBrush => FromResources("PageBackground", Brushes.White);

    private static Brush FromResources(string key, Brush fallback)
        => Application.Current?.Resources[key] as Brush ?? fallback;

    private static Brush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}