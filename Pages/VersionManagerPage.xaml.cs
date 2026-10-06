// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CraftUnifiedBooter.Services;
using Microsoft.Win32;

namespace CraftUnifiedBooter.Pages;

/// <summary>
/// 版本管理（参考 PCL 的版本管理界面）：左侧已安装版本列表，中间 Mod 管理，
/// 右侧为版本信息、快捷方式（打开主/模组/世界文件夹）与导出版本。
/// </summary>
public partial class VersionManagerPage : UserControl
{
    /// <summary>请求退出（返回主页）</summary>
    public event Action? ExitRequested;

    public VersionManagerPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    /// <summary>重新载入版本与 Mod 列表，并更新右侧版本信息</summary>
    public void Refresh()
    {
        var current = SettingsStore.Current.SelectedVersion ?? "";
        CurrentVersionPill.Text = string.IsNullOrWhiteSpace(current) ? "未选择版本" : current;
        RenderVersions();
        RenderMods();
        UpdateDetail();
    }

    // ===================== Toast =====================
    private void ShowToast(string message, ToastType type = ToastType.Success)
    {
        if (Window.GetWindow(this) is MainWindow mw) mw.ShowToast(message, type);
    }

    // ===================== 已安装版本 =====================
    private void RenderVersions()
    {
        VersionListPanel.Children.Clear();
        var current = SettingsStore.Current.SelectedVersion ?? "";
        var installed = VersionsService.GetVersions().Where(v => v.Installed).ToList();

        if (installed.Count == 0)
        {
            VersionListPanel.Children.Add(new TextBlock
            {
                Text = "暂无已安装版本，请到「下载」页面安装。",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var v in installed)
        {
            var isCurrent = v.Id == current;
            var border = new Border
            {
                Background = new SolidColorBrush(isCurrent
                    ? Color.FromRgb(0xEF, 0xF6, 0xFF)
                    : Color.FromRgb(0xF9, 0xFA, 0xFB)),
                BorderBrush = new SolidColorBrush(isCurrent
                    ? Color.FromRgb(0xBF, 0xDB, 0xFE)
                    : Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(1),
                CornerRadius = UiTheme.Radius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Tag = v.Id,
            };
            border.MouseLeftButtonUp += VersionCard_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = v.Id, FontSize = 13, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140,
            });
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            if (!string.IsNullOrEmpty(v.Loader))
                sub.Children.Add(new TextBlock
                {
                    Text = v.Loader, FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
                });
            sub.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(v.Loader) ? "原版" : "",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            });
            if (isCurrent)
                sub.Children.Add(new TextBlock
                {
                    Text = "· 当前使用", FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
                });
            info.Children.Add(sub);
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var delBtn = new Button
            {
                Style = (Style)FindResource("IconButtonStyle"),
                Width = 28, Height = 28,
                ToolTip = "删除该版本",
                Tag = v.Id,
                Content = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("IconTrash"),
                    Width = 14, Height = 14, Stretch = Stretch.Uniform,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            delBtn.Click += DeleteVersion_Click;
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);

            border.Child = grid;
            VersionListPanel.Children.Add(border);
        }
    }

    private void VersionCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string id }) return;
        // 点击版本 → 设为当前版本 → 直接退出版本管理，回到主页由用户手动点击「启动游戏」
        SettingsStore.Update(s => s.SelectedVersion = id);
        ExitRequested?.Invoke();
    }

    private void DeleteVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;

        // 右上角确认弹窗：紫色主题 + 彩虹旋转边框，「是」删除、「否」取消，5 秒不选自动取消删除
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.ShowConfirmDialog($"确定删除版本「{id}」吗？\n该版本的 Mod 也会一并删除。", () =>
            {
                VersionsService.Uninstall(id);
                if (SettingsStore.Current.SelectedVersion == id)
                    SettingsStore.Update(s => s.SelectedVersion = "");
                Refresh();
                ShowToast("版本已删除", ToastType.Success);
            });
        }
    }

    private void RefreshVersions_Click(object sender, RoutedEventArgs e) => Refresh();

    private void OpenVersionsFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(Store.GetVersionsDir());

    // ===================== 右侧版本信息 =====================

    /// <summary>识别当前选中版本的加载器类型（fabric/forge/neoforge/optifine），空字符串表示原版。</summary>
    private string DetectLoaderForCurrentVersion()
    {
        var id = SettingsStore.Current.SelectedVersion ?? "";
        if (string.IsNullOrWhiteSpace(id)) return "";

        var dir = Path.Combine(Store.GetVersionsDir(), id);
        var jsonPath = Path.Combine(dir, $"{id}.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var vd = System.Text.Json.JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath), Store.JsonReadOpts);
                var loader = vd == null ? "" : VersionsService.DetectLoader(vd) ?? "";
                if (!string.IsNullOrEmpty(loader)) return loader;
            }
            catch { /* 忽略损坏 JSON */ }
        }

        var lower = id.ToLowerInvariant();
        if (lower.Contains("fabric")) return "fabric";
        if (lower.Contains("forge")) return "forge";
        if (lower.Contains("neoforge")) return "neoforge";
        if (lower.Contains("optifine")) return "optifine";
        return "";
    }

    /// <summary>当前版本是否为 Mod 端（Fabric / Forge / NeoForge）。</summary>
    private bool IsModdedVersion()
        => DetectLoaderForCurrentVersion() is "fabric" or "forge" or "neoforge";

    private void UpdateDetail()
    {
        var id = SettingsStore.Current.SelectedVersion ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            DetailVersionText.Text = "未选择版本";
            DetailLoaderText.Text = "原版";
            DetailLoaderText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            DetailLoaderPill.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            DetailPathText.Text = "";
            return;
        }

        DetailVersionText.Text = id;
        var dir = Path.Combine(Store.GetVersionsDir(), id);
        DetailPathText.Text = dir;

        // 加载器类型：优先从版本 JSON 识别，其次按 ID 关键词
        var loader = DetectLoaderForCurrentVersion();

        var (bg, fg, text) = loader switch
        {
            "fabric" => ("#F5F3FF", "#7C3AED", "Fabric"),
            "forge" => ("#FFF7ED", "#C2410C", "Forge"),
            "neoforge" => ("#ECFDF5", "#047857", "NeoForge"),
            "optifine" => ("#F0F9FF", "#0369A1", "OptiFine"),
            _ => ("#F3F4F6", "#6B7280", "原版"),
        };
        DetailLoaderPill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        DetailLoaderText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        DetailLoaderText.Text = text;
    }

    // ===================== Mod 管理 =====================
    private string ModsDir => Store.ResolveGamePath("mods", SettingsStore.Current.SelectedVersion ?? "");

    private void RenderMods()
    {
        ModListPanel.Children.Clear();
        var dir = ModsDir;
        ModPathText.Text = dir;

        // 非 Mod 端：隐藏添加/打开操作，提示此功能不可用
        var isModded = IsModdedVersion();
        AddModBtn.Visibility = isModded ? Visibility.Visible : Visibility.Collapsed;
        OpenModsFolderBtn.Visibility = isModded ? Visibility.Visible : Visibility.Collapsed;
        ModHintText.Visibility = isModded ? Visibility.Visible : Visibility.Collapsed;

        if (!isModded)
        {
            ModListPanel.Children.Add(new TextBlock
            {
                Text = "这不是 Mod 端，此功能无法使用。\n请先在「下载」页面安装 Fabric / Forge / NeoForge 加载器。",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
            });
            return;
        }

        Store.EnsureDir(dir);

        var files = new List<string>();
        try
        {
            files.AddRange(Directory.GetFiles(dir, "*.jar"));
            files.AddRange(Directory.GetFiles(dir, "*.jar.disabled"));
        }
        catch { /* 目录不可读时留空 */ }

        if (files.Count == 0)
        {
            ModListPanel.Children.Add(new TextBlock
            {
                Text = "该版本暂无 Mod。点击「添加 Mod」把 .jar 文件放进来。",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var name = Path.GetFileName(file);
            if (disabled) name = name[..^".disabled".Length];

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(1),
                CornerRadius = UiTheme.Radius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = name, FontSize = 13, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(disabled
                    ? Color.FromRgb(0x9C, 0xA3, 0xAF)
                    : Color.FromRgb(0x11, 0x18, 0x27)),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320,
            });
            info.Children.Add(new TextBlock
            {
                Text = disabled ? "已禁用" : "已启用", FontSize = 11, Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(disabled
                    ? Color.FromRgb(0xDC, 0x26, 0x26)
                    : Color.FromRgb(0x16, 0xA3, 0x4A)),
            });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var toggleBtn = new Button
            {
                Style = (Style)FindResource("GrayButtonStyle"),
                Height = 26, Padding = new Thickness(8, 0, 8, 0),
                Content = new TextBlock { Text = disabled ? "启用" : "禁用", FontSize = 11 },
                Tag = file,
            };
            toggleBtn.Click += ToggleMod_Click;
            actions.Children.Add(toggleBtn);

            var delBtn = new Button
            {
                Style = (Style)FindResource("IconButtonStyle"),
                Width = 26, Height = 26, Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "删除该 Mod",
                Tag = file,
                Content = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("IconTrash"),
                    Width = 14, Height = 14, Stretch = Stretch.Uniform,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            delBtn.Click += DeleteMod_Click;
            actions.Children.Add(delBtn);

            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            border.Child = grid;
            ModListPanel.Children.Add(border);
        }
    }

    private void ToggleMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string file } || !File.Exists(file)) return;
        try
        {
            if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                File.Move(file, file[..^".disabled".Length]);
            else
                File.Move(file, file + ".disabled");
        }
        catch (Exception ex)
        {
            ShowToast($"操作失败：{ex.Message}", ToastType.Error);
        }
        RenderMods();
    }

    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string file }) return;
        if (MessageBox.Show($"确定删除 Mod「{Path.GetFileName(file)}」吗？", "版本管理",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { File.Delete(file); }
        catch (Exception ex)
        {
            ShowToast($"删除失败：{ex.Message}", ToastType.Error);
        }
        RenderMods();
    }

    private void AddMod_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 Mod 文件",
            Filter = "Mod 文件 (*.jar)|*.jar",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;

        var dir = ModsDir;
        Store.EnsureDir(dir);
        var failed = 0;
        foreach (var src in dlg.FileNames)
        {
            try { File.Copy(src, Path.Combine(dir, Path.GetFileName(src)), true); }
            catch { failed++; }
        }
        if (failed > 0)
            ShowToast($"有 {failed} 个文件复制失败。", ToastType.Warning);
        RenderMods();
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        Store.EnsureDir(ModsDir);
        OpenFolder(ModsDir);
    }

    // ===================== 右侧快捷方式 =====================
    private string VersionDir => Path.Combine(Store.GetVersionsDir(), SettingsStore.Current.SelectedVersion ?? "");

    private void OpenVersionFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = VersionDir;
        if (!Directory.Exists(dir))
        {
            ShowToast("该版本目录不存在。", ToastType.Warning);
            return;
        }
        OpenFolder(dir);
    }

    private void OpenSavesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Store.ResolveGamePath("saves", SettingsStore.Current.SelectedVersion ?? "");
        Store.EnsureDir(dir);
        OpenFolder(dir);
    }

    /// <summary>校验当前版本的游戏文件完整性（离线，对照安装时保存的 SHA1 缓存）。</summary>
    private void VerifyFiles_Click(object sender, RoutedEventArgs e)
    {
        var id = SettingsStore.Current.SelectedVersion ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            ShowToast("请先在左侧选择要校验的版本。", ToastType.Warning);
            return;
        }

        var (hasCache, missing, corrupted) = VersionsService.VerifyVersionFiles(id);
        if (!hasCache)
        {
            ShowToast("该版本没有缓存校验信息，请先重新安装该版本。", ToastType.Warning);
            return;
        }
        if (missing.Count == 0 && corrupted.Count == 0)
        {
            ShowToast($"版本「{id}」文件完整，校验通过。", ToastType.Success);
            return;
        }
        ShowToast($"校验完成：缺失 {missing.Count} 个，损坏 {corrupted.Count} 个文件。", ToastType.Error);
    }

    // ===================== 导出版本 =====================
    private void ExportVersion_Click(object sender, RoutedEventArgs e)
    {
        var id = SettingsStore.Current.SelectedVersion ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            ShowToast("请先在左侧选择要导出的版本。", ToastType.Warning);
            return;
        }

        var dir = Path.Combine(Store.GetVersionsDir(), id);
        if (!Directory.Exists(dir))
        {
            ShowToast("版本目录不存在。", ToastType.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "导出版本",
            Filter = "ZIP 压缩包 (*.zip)|*.zip",
            FileName = id + ".zip",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            // 复制到临时目录（排除 natives / logs / 崩溃报告），再打包，避免占用中文件冲突
            var tmp = Path.Combine(Path.GetTempPath(), "cub-export-" + Guid.NewGuid().ToString("N"));
            CopyVersionDir(dir, tmp);
            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            ZipFile.CreateFromDirectory(tmp, dlg.FileName, CompressionLevel.Optimal, false);
            try { Directory.Delete(tmp, true); } catch { /* 清理失败忽略 */ }
            ShowToast($"已导出到：{dlg.FileName}");
        }
        catch (Exception ex)
        {
            ShowToast($"导出失败：{ex.Message}", ToastType.Error);
        }
    }

    /// <summary>递归复制版本目录，跳过 natives、logs、crash-reports 等临时/大文件目录。</summary>
    private static void CopyVersionDir(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (var file in Directory.GetFiles(srcDir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(dstDir, name), true);
        }
        foreach (var sub in Directory.GetDirectories(srcDir))
        {
            var name = Path.GetFileName(sub);
            if (name is "natives" or "logs" or "crash-reports" or "crashes")
                continue;
            CopyVersionDir(sub, Path.Combine(dstDir, name));
        }
    }

    // ===================== 工具 =====================
    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { /* 打开失败忽略 */ }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}