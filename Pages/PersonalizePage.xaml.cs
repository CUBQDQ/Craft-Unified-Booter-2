// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter.Pages;

public partial class PersonalizePage : UserControl
{
    // 这些静态字段在页面每次加载时按当前主色刷新
    private static readonly Brush BrushOff = MakeBrush("#D1D5DB");
    private static Brush BrushTabTextOn = MakeBrush("#2563EB");
    private static readonly Brush BrushTabTextOff = MakeBrush("#6B7280");
    private static readonly Brush BrushWhite = Brushes.White;
    private static readonly Brush BrushTransparent = Brushes.Transparent;
    private static Brush BrushRing = MakeBrush("#2563EB");
    private static readonly DropShadowEffect TabShadow = MakeShadow();

    private bool _pageAnimation = true;
    private bool _showSidebarVersion = true;
    private bool _downloadNotify;
    private bool _minimizeOnLaunch;
    private bool _minimizeToTray;
    private bool _backgroundImage = true;
    private bool _loadingSettings;

    public PersonalizePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // 重新扫描 CUB/backgrounds（用户可能刚在文件夹里新增了图片）
            if (Window.GetWindow(this) is MainWindow mw) mw.ReloadBackgrounds();
            LoadSettings();
        };
    }

    private static Brush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Brush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DropShadowEffect MakeShadow()
    {
        var fx = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.15, Color = Colors.Black };
        fx.Freeze();
        return fx;
    }

    private static void SetToggle(Border track, TranslateTransform knob, bool on)
    {
        // 开启时引用全局主色资源，切换主色后自动跟随变色；关闭时用固定灰色
        if (on)
            track.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else
            track.Background = BrushOff;
        knob.X = on ? 20 : 0;
    }

    // ===================== 加载设置 =====================
    private void LoadSettings()
    {
        var s = SettingsStore.Current;
        _pageAnimation = s.PageAnimation;
        _showSidebarVersion = s.ShowSidebarVersion;
        _downloadNotify = s.DownloadNotify;
        _minimizeOnLaunch = s.MinimizeOnLaunch;
        _minimizeToTray = s.MinimizeToTray;
        _backgroundImage = s.BackgroundImage;

        // 图片背景与开发者提示：仅开发者模式可见
        var dev = s.DeveloperMode;
        BackgroundImageSection.Visibility = dev ? Visibility.Visible : Visibility.Collapsed;
        DeveloperBadge.Visibility = dev ? Visibility.Visible : Visibility.Collapsed;

        // 先把开关/选中态的颜色刷成当前主色
        UpdateAccentBrushes(s.Accent);

        SetToggle(TogglePageAnimation, TransPageAnimation, _pageAnimation);
        SetToggle(ToggleSidebarVersion, TransSidebarVersion, _showSidebarVersion);
        SetToggle(ToggleDownloadNotify, TransDownloadNotify, _downloadNotify);
        SetToggle(ToggleMinimizeOnLaunch, TransMinimizeOnLaunch, _minimizeOnLaunch);
        SetToggle(ToggleMinimizeToTray, TransMinimizeToTray, _minimizeToTray);
        SetToggle(ToggleBackgroundImage, TransBackgroundImage, _backgroundImage);

        // 滑杆初始化：加载期间不保存，避免初始赋值触发设置写入
        _loadingSettings = true;
        OpacitySlider.Value = s.BackgroundOpacity;
        BlurSlider.Value = s.BackgroundBlur;
        _loadingSettings = false;
        OpacityValueText.Text = s.BackgroundOpacity + "%";
        BlurValueText.Text = s.BackgroundBlur > 0 ? s.BackgroundBlur.ToString() : "0（不模糊）";

        SelectSeg("theme", s.Theme);
        SelectSeg("radius", s.Radius);
        SelectSeg("font", s.FontSize);
        SelectSeg("sidebar", s.SidebarWidth);
        SelectSeg("titlebar", s.TitleBarHeight);
        SelectSeg("pagesize", s.PageSize.ToString());
        SelectSeg("bgmode", s.BackgroundMode);
        SelectAccent(s.Accent);
        UpdateBackgroundInfo();
    }

    /// <summary>刷新多图切换与效果调节行的显示：图片数量、当前第几张；仅在启用且有图片时可见。</summary>
    private void UpdateBackgroundInfo()
    {
        if (Window.GetWindow(this) is not MainWindow mw) return;
        var count = mw.BackgroundCount;
        var showAdjust = _backgroundImage && count > 0;
        BackgroundMultiRow.Visibility = showAdjust ? Visibility.Visible : Visibility.Collapsed;
        BackgroundOpacityRow.Visibility = showAdjust ? Visibility.Visible : Visibility.Collapsed;
        BackgroundBlurRow.Visibility = showAdjust ? Visibility.Visible : Visibility.Collapsed;
        BackgroundIndexText.Text = count == 0 ? "未找到图片" : $"第 {mw.BackgroundIndex + 1} / {count} 张";
        // 只有一张图时不可手动切换
        PrevBackgroundBtn.IsEnabled = count > 1;
        NextBackgroundBtn.IsEnabled = count > 1;
    }

    /// <summary>按当前主色刷新开关、分段选中文字、强调色外圈的画笔。</summary>
    private static void UpdateAccentBrushes(string accent)
    {
        var (main, _, _, _) = MainWindow.AccentColors(accent);
        BrushTabTextOn = MakeBrush(main);
        BrushRing = MakeBrush(main);
    }

    private void SelectSeg(string prefix, string value)
    {
        var name = prefix switch
        {
            "theme" => value switch
            {
                // "liquid" 主题已移除，旧配置按 glass 处理
                "glass" => "ThemeGlass", "liquid" => "ThemeGlass", _ => "ThemeCub",
            },
            "radius" => value switch
            {
                "none" => "RadiusNone", "large" => "RadiusLarge", _ => "RadiusDefault",
            },
            "font" => value switch
            {
                "small" => "FontSmall", "large" => "FontLarge", _ => "FontMedium",
            },
            "sidebar" => value switch
            {
                "narrow" => "SidebarNarrow", "wide" => "SidebarWide", _ => "SidebarDefault",
            },
            "titlebar" => value switch
            {
                "compact" => "TitlebarCompact", "large" => "TitlebarLarge", _ => "TitlebarDefault",
            },
            "pagesize" => value switch
            {
                "10" => "PageSize10", "40" => "PageSize40", _ => "PageSize20",
            },
            "bgmode" => value switch
            {
                "auto" => "BgModeAuto", _ => "BgModeManual",
            },
            _ => "",
        };
        if (FindName(name) is Border bd) SelectSegVisual(bd);
    }

    private void SelectAccent(string accent)
    {
        var name = accent switch
        {
            "violet" => "AccentViolet", "green" => "AccentGreen", "orange" => "AccentOrange",
            "pink" => "AccentPink", "cyan" => "AccentCyan", "red" => "AccentRed",
            "amber" => "AccentAmber", _ => "AccentBlue",
        };
        if (FindName(name) is Border bd)
        {
            var panel = (Panel)bd.Parent;
            foreach (var child in panel.Children.OfType<Border>())
                child.BorderBrush = child == bd ? BrushRing : BrushTransparent;
        }
    }

    private void SelectSegVisual(Border bd)
    {
        var panel = (Panel)bd.Parent;
        foreach (var child in panel.Children.OfType<Border>())
        {
            var selected = child == bd;
            child.Background = selected ? BrushWhite : BrushTransparent;
            child.Effect = selected ? TabShadow : null;
            if (child.Child is TextBlock txt)
                txt.Foreground = selected ? BrushTabTextOn : BrushTabTextOff;
        }
    }

    // ===================== 开关 =====================
    private void TogglePageAnimation_Click(object sender, MouseButtonEventArgs e)
    {
        _pageAnimation = !_pageAnimation;
        SetToggle(TogglePageAnimation, TransPageAnimation, _pageAnimation);
        SettingsStore.Update(s => s.PageAnimation = _pageAnimation);
    }

    private void ToggleSidebarVersion_Click(object sender, MouseButtonEventArgs e)
    {
        _showSidebarVersion = !_showSidebarVersion;
        SetToggle(ToggleSidebarVersion, TransSidebarVersion, _showSidebarVersion);
        SettingsStore.Update(s => s.ShowSidebarVersion = _showSidebarVersion);
    }

    private void ToggleDownloadNotify_Click(object sender, MouseButtonEventArgs e)
    {
        _downloadNotify = !_downloadNotify;
        SetToggle(ToggleDownloadNotify, TransDownloadNotify, _downloadNotify);
        SettingsStore.Update(s => s.DownloadNotify = _downloadNotify);
    }

    private void ToggleMinimizeOnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        _minimizeOnLaunch = !_minimizeOnLaunch;
        SetToggle(ToggleMinimizeOnLaunch, TransMinimizeOnLaunch, _minimizeOnLaunch);
        SettingsStore.Update(s => s.MinimizeOnLaunch = _minimizeOnLaunch);
    }

    private void ToggleMinimizeToTray_Click(object sender, MouseButtonEventArgs e)
    {
        _minimizeToTray = !_minimizeToTray;
        SetToggle(ToggleMinimizeToTray, TransMinimizeToTray, _minimizeToTray);
        SettingsStore.Update(s => s.MinimizeToTray = _minimizeToTray);
    }

    private void ToggleBackgroundImage_Click(object sender, MouseButtonEventArgs e)
    {
        _backgroundImage = !_backgroundImage;
        SetToggle(ToggleBackgroundImage, TransBackgroundImage, _backgroundImage);
        SettingsStore.Update(s => s.BackgroundImage = _backgroundImage);
        UpdateBackgroundInfo();
    }

    /// <summary>手动切换到上一张背景图。</summary>
    private void PrevBackground_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.SwitchBackground(-1);
            UpdateBackgroundInfo();
        }
    }

    /// <summary>手动切换到下一张背景图。</summary>
    private void NextBackground_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.SwitchBackground(1);
            UpdateBackgroundInfo();
        }
    }

    /// <summary>打开 CUB/backgrounds 文件夹，让用户自己放入背景图片。</summary>
    private void OpenBackgroundFolder_Click(object sender, RoutedEventArgs e)
    {
        Store.EnsureDir(Store.CubBackgroundsDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.CubBackgroundsDir)
            {
                UseShellExecute = true,
            });
        }
        catch { /* ignore */ }
    }

    // ===================== 背景效果调节 =====================

    /// <summary>背景不透明度滑杆：只更新背景图层透明度，不触发完整个性化刷新（避免重新解码图片卡顿）。</summary>
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var value = (int)Math.Round(e.NewValue);
        OpacityValueText.Text = value + "%";
        if (_loadingSettings) return;

        SettingsStore.Current.BackgroundOpacity = value;
        SettingsStore.Save();
        if (Window.GetWindow(this) is MainWindow mw) mw.ApplyBackgroundEffects();
    }

    /// <summary>背景模糊滑杆：只更新背景图 BlurEffect，不触发完整个性化刷新。</summary>
    private void BlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var value = (int)Math.Round(e.NewValue);
        BlurValueText.Text = value > 0 ? value.ToString() : "0（不模糊）";
        if (_loadingSettings) return;

        SettingsStore.Current.BackgroundBlur = value;
        SettingsStore.Save();
        if (Window.GetWindow(this) is MainWindow mw) mw.ApplyBackgroundEffects();
    }

    // ===================== 分段选择 =====================
    private void SegTab_Click(object sender, MouseButtonEventArgs e)
    {
        var bd = (Border)sender;
        SelectSegVisual(bd);

        var tag = bd.Tag as string;
        if (string.IsNullOrEmpty(tag)) return;
        SettingsStore.Update(s =>
        {
            switch (tag)
            {
                case "theme:cub": s.Theme = "cub"; break;
                case "theme:glass": s.Theme = "glass"; break;
                case "radius:none": s.Radius = "none"; break;
                case "radius:default": s.Radius = "default"; break;
                case "radius:large": s.Radius = "large"; break;
                case "font:small": s.FontSize = "small"; break;
                case "font:medium": s.FontSize = "medium"; break;
                case "font:large": s.FontSize = "large"; break;
                case "sidebar:narrow": s.SidebarWidth = "narrow"; break;
                case "sidebar:default": s.SidebarWidth = "default"; break;
                case "sidebar:wide": s.SidebarWidth = "wide"; break;
                case "titlebar:compact": s.TitleBarHeight = "compact"; break;
                case "titlebar:default": s.TitleBarHeight = "default"; break;
                case "titlebar:large": s.TitleBarHeight = "large"; break;
                case "pagesize:10": s.PageSize = 10; break;
                case "pagesize:20": s.PageSize = 20; break;
                case "pagesize:40": s.PageSize = 40; break;
                case "bgmode:manual": s.BackgroundMode = "manual"; break;
                case "bgmode:auto": s.BackgroundMode = "auto"; break;
            }
        });
        if (tag.StartsWith("bgmode:")) UpdateBackgroundInfo();
    }

    // ===================== 强调色 =====================
    private void AccentDot_Click(object sender, MouseButtonEventArgs e)
    {
        var bd = (Border)sender;
        var panel = (Panel)bd.Parent;
        foreach (var child in panel.Children.OfType<Border>())
            child.BorderBrush = child == bd ? BrushRing : BrushTransparent;

        var tag = bd.Tag as string;
        if (!string.IsNullOrEmpty(tag))
            SettingsStore.Update(s => s.Accent = tag);

        // 立即用新主色刷新本页所有开关 / 分段选中态 / 色板外圈，避免"上下没及时变色"
        LoadSettings();
    }

    // ===================== 恢复默认 =====================
    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Update(s =>
        {
            s.Theme = "cub";
            s.Accent = "blue";
            s.Radius = "default";
            s.FontSize = "medium";
            s.SidebarWidth = "default";
            s.TitleBarHeight = "default";
            s.PageAnimation = true;
            s.ShowSidebarVersion = true;
            s.PageSize = 20;
            s.DownloadNotify = false;
            s.MinimizeOnLaunch = false;
            s.MinimizeToTray = false;
            s.BackgroundImage = true;
            s.BackgroundMode = "manual";
            s.BackgroundIndex = 0;
            s.BackgroundOpacity = 100;
            s.BackgroundBlur = 0;
        });
        LoadSettings();
    }
}