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
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter.Pages;

public partial class SettingsPage : UserControl
{
    private static readonly Brush BrushOff = MakeBrush("#D1D5DB");
    private static readonly Brush BrushGray50 = MakeBrush("#F3F4F6");
    private static readonly Brush BrushGray600 = MakeBrush("#4B5563");

    /// <summary>正在加载设置（避免初始化时的事件触发保存覆盖已有设置）</summary>
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        var s = SettingsStore.Current;
        _autoJava = s.AutoJava;
        _autoMemory = s.AutoMemory;
        _fullscreen = s.Fullscreen;
        _versionIsolation = s.VersionIsolation;
        _keepOpen = s.KeepOpen;
        _autoUpdate = s.AutoUpdate;

        // 加载各设置控件初始值
        _loading = true;
        JavaPathBox.Text = s.JavaPath;
        MinMemSlider.Value = s.MinMemory;
        MaxMemSlider.Value = s.MaxMemory;
        WidthBox.Text = s.Width.ToString();
        HeightBox.Text = s.Height.ToString();
        JvmArgsBox.Text = s.JvmArgs;
        ThreadsSlider.Value = s.DownloadThreads;
        GameDirBox.Text = s.GameDir;
        DownloadSourceCombo.SelectedIndex = s.DownloadSource switch
        {
            "official" => 1,
            "bmclapi" => 2,
            _ => 0,
        };
        _loading = false;

        SetToggle(ToggleAutoJava, TransAutoJava, _autoJava);
        JavaManualArea.Visibility = _autoJava ? Visibility.Collapsed : Visibility.Visible;
        SetToggle(ToggleAutoMemory, TransAutoMemory, _autoMemory);
        MemManualArea.Visibility = _autoMemory ? Visibility.Collapsed : Visibility.Visible;
        SetToggle(ToggleFullscreen, TransFullscreen, _fullscreen);
        SetToggle(ToggleVersionIsolation, TransVersionIsolation, _versionIsolation);
        SetToggle(ToggleKeepOpen, TransKeepOpen, _keepOpen);
        SetToggle(ToggleAutoUpdate, TransAutoUpdate, _autoUpdate);

        // 宽高在失焦时保存
        WidthBox.LostKeyboardFocus += (_, _) => SaveInt(nameof(Settings.Width), WidthBox.Text, 854);
        HeightBox.LostKeyboardFocus += (_, _) => SaveInt(nameof(Settings.Height), HeightBox.Text, 480);
        JvmArgsBox.LostKeyboardFocus += (_, _) => SettingsStore.Update(s => s.JvmArgs = JvmArgsBox.Text);
        JavaPathBox.LostKeyboardFocus += (_, _) => SettingsStore.Update(s => s.JavaPath = JavaPathBox.Text);
        GameDirBox.LostKeyboardFocus += (_, _) => SettingsStore.Update(s => s.GameDir = GameDirBox.Text);

        // 当前版本号（发布版从程序集读取）
        SettingsCurrentVersionText.Text = $"v{UpdateService.CurrentVersion}";
    }

    private bool _autoJava;
    private bool _autoMemory;
    private bool _fullscreen;
    private bool _advancedMode;
    private bool _versionIsolation;
    private bool _keepOpen;
    private bool _autoUpdate;
    private UpdateInfo? _pendingUpdate;

    // ===================== 检查更新 =====================

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;
        SettingsCheckUpdateText.Text = "检查中…";
        SettingsCheckTimeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        try
        {
            var info = await UpdateService.CheckAsync();
            if (info != null)
            {
                // 有新版本：显示更新提示面板
                _pendingUpdate = info;
                SettingsLatestPanel.Visibility = Visibility.Collapsed;
                SettingsUpdatePanel.Visibility = Visibility.Visible;
                SettingsUpdateVersionText.Text = $"发现新版本 v{info.Version}（当前 v{UpdateService.CurrentVersion}）";
                SettingsUpdateDescText.Text = string.IsNullOrWhiteSpace(info.Changelog)
                    ? "暂无更新说明。"
                    : info.Changelog;
            }
            else
            {
                // 已是最新（或服务器无版本）
                _pendingUpdate = null;
                SettingsUpdatePanel.Visibility = Visibility.Collapsed;
                SettingsLatestPanel.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            _pendingUpdate = null;
            SettingsUpdatePanel.Visibility = Visibility.Collapsed;
            SettingsLatestPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
            SettingsCheckUpdateText.Text = "检查更新";
        }
    }

    /// <summary>点击「立即更新」：复用主窗口的更新弹窗（含强制/非强制逻辑）。</summary>
    private void SettingsUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        if (Window.GetWindow(this) is MainWindow mw)
            mw.ShowUpdatePrompt(_pendingUpdate);
    }

    private static Brush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
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

    private void SaveInt(string prop, string text, int fallback)
    {
        if (_loading) return;
        var ok = int.TryParse(text, out var value);
        if (!ok || value <= 0) value = fallback;
        SettingsStore.Update(s =>
        {
            if (prop == nameof(Settings.Width)) s.Width = value;
            else if (prop == nameof(Settings.Height)) s.Height = value;
        });
    }

    private void ToggleAutoJava_Click(object sender, MouseButtonEventArgs e)
    {
        _autoJava = !_autoJava;
        SetToggle(ToggleAutoJava, TransAutoJava, _autoJava);
        JavaManualArea.Visibility = _autoJava ? Visibility.Collapsed : Visibility.Visible;
        SettingsStore.Update(s => s.AutoJava = _autoJava);
    }

    private void ToggleAutoMemory_Click(object sender, MouseButtonEventArgs e)
    {
        _autoMemory = !_autoMemory;
        SetToggle(ToggleAutoMemory, TransAutoMemory, _autoMemory);
        MemManualArea.Visibility = _autoMemory ? Visibility.Collapsed : Visibility.Visible;
        SettingsStore.Update(s => s.AutoMemory = _autoMemory);
    }

    private void ToggleFullscreen_Click(object sender, MouseButtonEventArgs e)
    {
        _fullscreen = !_fullscreen;
        SetToggle(ToggleFullscreen, TransFullscreen, _fullscreen);
        SettingsStore.Update(s => s.Fullscreen = _fullscreen);
    }

    private void ToggleAdvanced_Click(object sender, MouseButtonEventArgs e)
    {
        _advancedMode = !_advancedMode;
        SetToggle(ToggleAdvanced, TransAdvanced, _advancedMode);
        JvmArea.Visibility = _advancedMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleVersionIsolation_Click(object sender, MouseButtonEventArgs e)
    {
        _versionIsolation = !_versionIsolation;
        SetToggle(ToggleVersionIsolation, TransVersionIsolation, _versionIsolation);
        SettingsStore.Update(s => s.VersionIsolation = _versionIsolation);
    }

    private void ToggleKeepOpen_Click(object sender, MouseButtonEventArgs e)
    {
        _keepOpen = !_keepOpen;
        SetToggle(ToggleKeepOpen, TransKeepOpen, _keepOpen);
        SettingsStore.Update(s => s.KeepOpen = _keepOpen);
    }

    private void ToggleAutoUpdate_Click(object sender, MouseButtonEventArgs e)
    {
        _autoUpdate = !_autoUpdate;
        SetToggle(ToggleAutoUpdate, TransAutoUpdate, _autoUpdate);
        SettingsStore.Update(s => s.AutoUpdate = _autoUpdate);
    }

    // ===================== 手动 Java =====================

    private void JavaBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件 (java.exe)|java.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true)
        {
            JavaPathBox.Text = dlg.FileName;
            SettingsStore.Update(s => s.JavaPath = dlg.FileName);
            VerifyJava();
        }
    }

    private void JavaAutoDetect_Click(object sender, RoutedEventArgs e)
    {
        var path = JavaService.AutoDetectJavaPath();
        if (string.IsNullOrEmpty(path) || path == "java")
        {
            JavaVerifyPanel.Background = MakeBrush("#FEF2F2");
            JavaVerifyPanel.BorderBrush = MakeBrush("#FECACA");
            JavaVerifyText.Foreground = MakeBrush("#B91C1C");
            JavaVerifyText.Text = "未检测到系统 Java，请先安装或使用自动下载。";
            JavaVerifyPanel.Visibility = Visibility.Visible;
            return;
        }
        JavaPathBox.Text = path;
        SettingsStore.Update(s => s.JavaPath = path);
        VerifyJava();
    }

    private void JavaVerify_Click(object sender, RoutedEventArgs e) => VerifyJava();

    private void VerifyJava()
    {
        var path = JavaPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            JavaVerifyPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var info = JavaService.GetJavaInfo(path);
        if (info.IsValid)
        {
            JavaVerifyPanel.Background = MakeBrush("#F0FDF4");
            JavaVerifyPanel.BorderBrush = MakeBrush("#DCFCE7");
            JavaVerifyText.Foreground = MakeBrush("#15803D");
            JavaVerifyText.Text = $"有效 - Java {info.MajorVersion}（{info.Version}） / {(info.Is64Bit ? "64 位" : "32 位")} / {(info.IsJre ? "JRE" : "JDK")}";
        }
        else
        {
            JavaVerifyPanel.Background = MakeBrush("#FEF2F2");
            JavaVerifyPanel.BorderBrush = MakeBrush("#FECACA");
            JavaVerifyText.Foreground = MakeBrush("#B91C1C");
            JavaVerifyText.Text = "无效的 Java 路径，请检查文件是否存在。";
        }
        JavaVerifyPanel.Visibility = Visibility.Visible;
    }

    // ===================== 预设分辨率 =====================

    private void Preset_Click(object sender, MouseButtonEventArgs e)
    {
        var bd = (Border)sender;
        var panel = (Panel)bd.Parent;
        foreach (var child in panel.Children.OfType<Border>())
        {
            var selected = child == bd;
            child.Background = selected ? UiTheme.AccentSoftBrush : BrushGray50;
            if (child.Child is TextBlock txt)
                txt.Foreground = selected ? UiTheme.AccentDarkBrush : BrushGray600;
        }

        if (bd.Tag is string tag && tag.Split(',') is { Length: 2 } parts &&
            int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
        {
            WidthBox.Text = w.ToString();
            HeightBox.Text = h.ToString();
            SettingsStore.Update(s => { s.Width = w; s.Height = h; });
        }
    }

    // ===================== 滑块 =====================

    private void MinMemSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinMemLabel != null)
            MinMemLabel.Text = $"最小内存: {(int)e.NewValue} MB";
        if (_loading) return;
        SettingsStore.Update(s => s.MinMemory = (int)e.NewValue);
    }

    private void MaxMemSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxMemLabel != null)
            MaxMemLabel.Text = $"最大内存: {(int)e.NewValue} MB";
        if (_loading) return;
        SettingsStore.Update(s => s.MaxMemory = (int)e.NewValue);
    }

    private void ThreadsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThreadsLabel != null)
            ThreadsLabel.Text = $"下载线程数: {(int)e.NewValue}";
        if (_loading) return;
        SettingsStore.Update(s => s.DownloadThreads = (int)e.NewValue);
    }

    // ===================== 下载源 =====================

    private void DownloadSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DownloadSourceCombo == null) return;
        var source = DownloadSourceCombo.SelectedIndex switch
        {
            1 => "official",
            2 => "bmclapi",
            _ => "auto",
        };
        SettingsStore.Update(s => s.DownloadSource = source);
    }

    // ===================== 游戏目录 =====================

    private void GameDirBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择游戏目录（.minecraft 级）",
            Multiselect = false,
        };
        if (dlg.ShowDialog() == true)
        {
            GameDirBox.Text = dlg.FolderName;
            SettingsStore.Update(s => s.GameDir = dlg.FolderName);
        }
    }
}