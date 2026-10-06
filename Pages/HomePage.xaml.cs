// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter.Pages;

public partial class HomePage : UserControl
{
    /// <summary>请求打开皮肤库</summary>
    public event Action? SkinLibraryRequested;

    /// <summary>请求打开版本管理</summary>
    public event Action? VersionManagerRequested;

    /// <summary>主页皮肤展示/头像使用的皮肤文件路径（皮肤库选好后回写）</summary>
    public static string? CurrentSkinPath { get; set; }
    public static bool CurrentSkinSlim { get; set; }

    private bool _launching;
    private LaunchService.LaunchProgress? _lastProgress;

    // 3D 皮肤：左右旋转 + 上下俯仰（不倾斜）+ 滚轮缩放
    private double _yaw = 14;
    private double _pitch;
    private double _dist = 168;
    private Point _dragStart;
    private bool _dragging;

    // 3D 皮肤动画：默认 / 走动 / 跑动（参照 Minecraft 原版动作）
    private SkinService.AnimatedSkin? _skinModel;
    private SkinService.SkinAnimation _animation = SkinService.SkinAnimation.Walk;   // 主页默认走动
    private double _animTime;
    private readonly System.Windows.Threading.DispatcherTimer _animTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(33),
    };

    /// <summary>发现新版本（供 MainWindow 弹出更新提示）</summary>
    public event Action<UpdateInfo>? UpdateAvailableRequested;

    public HomePage()
    {
        InitializeComponent();
        LaunchService.Progress += OnLaunchProgress;
        LaunchService.GameOutput += OnGameOutput;
        LaunchService.GameExit += OnGameExit;
        Unloaded += HomePage_Unloaded;
        Loaded += HomePage_Loaded;
        AccountsStore.Changed += OnAccountsChanged;

        // 动画驱动：每 33ms 推进一次动作（约 30fps）
        _animTimer.Tick += (_, _) =>
        {
            _animTime += 0.033;
            _skinModel?.ApplyAnimation(_animation, _animTime);
        };
        _animTimer.Start();
    }

    /// <summary>主页卡片 Q 弹入场（皮肤库 / 版本管理 / 公告），带轻微级联延迟，确保流畅不卡。</summary>
    public void PlayCardEntrance()
    {
        AnimateEntry(SkinEntryBtn, 0);
        AnimateEntry(VersionEntryBtn, 70);
        AnimateEntry(NoticeCard, 140);
    }

    private static void AnimateEntry(UIElement el, int delayMs)
    {
        el.Opacity = 0;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TranslateTransform(0, 12);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = UiAnimation.QElastic(10),
        };
        el.BeginAnimation(OpacityProperty, fade);
        var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(520))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = UiAnimation.QElastic(8),
        };
        ((TranslateTransform)el.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadData();
        _ = LoadRemoteNoticesAsync();
        _ = CheckUpdateAsync();
    }

    /// <summary>检查更新：发现新版本时通知 MainWindow 弹窗提示</summary>
    private async Task CheckUpdateAsync()
    {
        var info = await UpdateService.CheckAsync();
        if (info == null) return;
        await Application.Current.Dispatcher.InvokeAsync(() => UpdateAvailableRequested?.Invoke(info));
    }

    /// <summary>从更新服务器拉取公告；服务器不可达时保留本地内置公告</summary>
    private async Task LoadRemoteNoticesAsync()
    {
        foreach (var baseUrl in new[] { UpdateService.Server, UpdateService.ServerLocal })
        {
            try
            {
                var raw = await Downloader.Client.GetStringAsync($"{baseUrl}/api/notice");
                var list = JsonSerializer.Deserialize<List<RemoteNotice>>(raw, Store.JsonReadOpts);
                var items = (list ?? new List<RemoteNotice>())
                    .Where(n => !string.IsNullOrWhiteSpace(n.Text))
                    .Select(n => (n.Version ?? "", n.Date ?? "", n.Text!))
                    .ToList();
                if (items.Count == 0) continue;
                RenderNotices(items);
                return;
            }
            catch
            {
                // 当前地址不可用，尝试下一个
            }
        }
    }

    /// <summary>服务器返回的公告结构</summary>
    private class RemoteNotice
    {
        public string? Version { get; set; }
        public string? Date { get; set; }
        public string? Text { get; set; }
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        LaunchService.Progress -= OnLaunchProgress;
        LaunchService.GameOutput -= OnGameOutput;
        LaunchService.GameExit -= OnGameExit;
        AccountsStore.Changed -= OnAccountsChanged;
    }

    private void OnAccountsChanged() => Application.Current.Dispatcher.Invoke(() =>
    {
        CurrentSkinPath = null;
        LoadData();
    });

    private void RenderNotices(IReadOnlyList<(string Version, string Date, string Text)> items)
    {
        NoticePanel.Children.Clear();
        foreach (var (version, date, text) in items)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrWhiteSpace(version))
            {
                head.Children.Add(new Border
                {
                    Background = UiTheme.AccentSoftBrush,
                    CornerRadius = UiTheme.Radius(4),
                    Padding = new Thickness(7, 1, 7, 1),
                    Child = new TextBlock
                    {
                        Text = "v" + version,
                        FontSize = 11,
                        Foreground = UiTheme.AccentBrush,
                    },
                });
            }
            if (!string.IsNullOrWhiteSpace(date))
            {
                head.Children.Add(new TextBlock
                {
                    Text = date,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            if (head.Children.Count > 0) stack.Children.Add(head);
            stack.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0),
                LineHeight = 18,
            });
            NoticePanel.Children.Add(stack);
        }
    }

    /// <summary>重新载入主页数据（皮肤库切换皮肤/披风后回调用）</summary>
    public void Refresh() => LoadData();

    private void LoadData()
    {
        // 版本信息
        var selectedVersion = SettingsStore.Current.SelectedVersion;
        CurrentVersionText.Text = string.IsNullOrWhiteSpace(selectedVersion) ? "未选择版本" : selectedVersion;

        // 账户信息
        var account = AccountsStore.GetSelected();
        CurrentUserText.Text = account?.Name ?? "未添加账户";
        AccountTypeText.Text = account?.Type == "microsoft" ? "微软账户" : "离线账户";
        AccountTypePill.Background = account?.Type == "microsoft"
            ? UiTheme.AccentSoftBrush
            : new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
        AccountTypeText.Foreground = account?.Type == "microsoft"
            ? UiTheme.AccentBrush
            : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        var (skinPath, slim) = ResolveSkin(account);
        CurrentSkinPath = skinPath;
        CurrentSkinSlim = slim;

        // 头像：按 PCL2 呈现方法裁出脸层 + 第二层帽子，两层叠加显示（离线账户用默认史蒂夫/艾利克斯皮肤文件，微软账户用其皮肤）
        var avatar = AvatarService.CreateSkinAvatarBits(skinPath, 60);
        if (avatar != null)
        {
            HomeAvatar.Background = Brushes.Transparent; // 有头像时去掉灰色底
            HomeAvatarFace.Source = avatar.Face;
            HomeAvatarFace.Visibility = Visibility.Visible;
            if (avatar.Hat != null)
            {
                HomeAvatarHat.Source = avatar.Hat;
                HomeAvatarHat.Visibility = Visibility.Visible;
            }
            else
            {
                HomeAvatarHat.Source = null;
                HomeAvatarHat.Visibility = Visibility.Collapsed;
            }
            HomeAvatarFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            HomeAvatarFace.Source = null;
            HomeAvatarFace.Visibility = Visibility.Collapsed;
            HomeAvatarHat.Source = null;
            HomeAvatarHat.Visibility = Visibility.Collapsed;
            HomeAvatarFallback.Visibility = Visibility.Visible;
            HomeAvatar.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
        }

        BuildSkinModel(skinPath, account?.LocalCape, slim);
    }

    /// <summary>解析当前账户应显示的皮肤文件与模型类型</summary>
    private static (string? Path, bool Slim) ResolveSkin(Account? account)
    {
        // 账户已选过皮肤（含微软同步下来的正版皮肤）优先使用本地文件
        if (account != null && !string.IsNullOrEmpty(account.LocalSkin))
            return (account.LocalSkin, SkinService.IsSlim(account.SkinVariant));

        var ordinal = account == null ? 2 : AccountsStore.OfflineOrdinal(account);
        var steve = ordinal % 2 == 0;
        return (SkinService.EnsureDefaultSkin(steve), !steve);
    }

    // ===================== 3D 皮肤 =====================
    private void BuildSkinModel(string? skinPath, string? capePath, bool slim)
    {
        var model = SkinService.BuildAnimatedModel(skinPath, capePath, slim)
                    ?? SkinService.BuildAnimatedModel(SkinService.EnsureDefaultSkin(true), capePath, false);

        if (model == null)
        {
            _skinModel = null;
            SkinModel.Content = null;
            SkinEmptyText.Visibility = Visibility.Visible;
            return;
        }

        _skinModel = model;
        SkinEmptyText.Visibility = Visibility.Collapsed;
        SkinModel.Content = model.Group;
        ApplySkinTransform();
    }

    // ===== 动作切换（默认 / 走动 / 跑动） =====
    private void AnimDefault_Click(object sender, RoutedEventArgs e)
    {
        _animation = SkinService.SkinAnimation.Idle;
        _animTime = 0;
    }

    private void AnimWalk_Click(object sender, RoutedEventArgs e)
    {
        _animation = SkinService.SkinAnimation.Walk;
        _animTime = 0;
    }

    private void AnimRun_Click(object sender, RoutedEventArgs e)
    {
        _animation = SkinService.SkinAnimation.Run;
        _animTime = 0;
    }

    /// <summary>把模型平移到以原点为中心；先绕 Y 轴水平旋转，再绕 X 轴上下俯仰（不产生倾斜）</summary>
    private void ApplySkinTransform()
    {
        var group = new Transform3DGroup();
        group.Children.Add(new TranslateTransform3D(0, -16, 0));
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _yaw)));
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), _pitch)));
        SkinModel.Transform = group;

        SkinCamera.Position = new Point3D(0, 0, _dist);
        SkinCamera.LookDirection = new Vector3D(0, 0, -_dist);

        // 收紧近/远平面：WPF 默认 FarPlaneDistance 为无穷远，深度精度极差，
        // 覆盖层（帽/外套）只比基础层外扩 0.25~0.5，会与之逐像素 Z-fighting，看起来"花/乱"
        SkinCamera.NearPlaneDistance = Math.Max(1, _dist - 24);
        SkinCamera.FarPlaneDistance = _dist + 24;
    }

    private void SkinPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(SkinPreviewHost);
        SkinPreviewHost.CaptureMouse();
    }

    private void SkinPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        SkinPreviewHost.ReleaseMouseCapture();
    }

    private void SkinPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(SkinPreviewHost);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _dragStart = p;

        // 左右拖动 → 水平旋转；上下拖动 → 俯仰（限制范围，避免翻转/倾斜）
        _yaw += dx * 0.8;
        _pitch = Math.Clamp(_pitch + dy * 0.8, -60, 60);
        ApplySkinTransform();
    }

    private void SkinPreview_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _yaw = 14;
        _pitch = 0;
        _dist = 168;
        ApplySkinTransform();
    }

    private void OpenSkinLibrary_Click(object sender, RoutedEventArgs e) => SkinLibraryRequested?.Invoke();

    private void OpenVersionManager_Click(object sender, RoutedEventArgs e) => VersionManagerRequested?.Invoke();

    private void OnLaunchProgress(LaunchService.LaunchProgress p)
    {
        _lastProgress = p;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!_launching) return;
            var stageMsg = p.Message;
            if (string.IsNullOrEmpty(stageMsg)) stageMsg = "准备中…";

            if (p.Stage == "prepareJava")
                _launchStatus?.ShowJavaPrep(stageMsg);
            else
                _launchStatus?.UpdateProgress(stageMsg, p.OverallPercent);

            if (p.Success == true)
            {
                _launching = false;
                _launchStatus?.ShowSuccess("");
                // 个性化：启动游戏后自动最小化启动器
                if (Services.SettingsStore.Current.MinimizeOnLaunch)
                {
                    var win = Window.GetWindow(this);
                    if (win != null) win.WindowState = WindowState.Minimized;
                }
            }
            else if (p.Success == false)
            {
                _launching = false;
                _launchStatus?.ShowFailure(stageMsg);
            }
        });
    }

    private void OnGameOutput(string output)
    {
        if (!_launching) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_lastProgress?.Success == true)
                _launchStatus?.ShowSuccess(output);
        });
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_launching) return;

        var selectedVersion = SettingsStore.Current.SelectedVersion;
        if (string.IsNullOrWhiteSpace(selectedVersion))
        {
            _launchStatus?.ShowFailure("未选择游戏版本，请先在下载页面安装版本。");
            return;
        }

        var account = AccountsStore.GetSelected()?.Name ?? "Player";
        _launching = true;
        _lastProgress = null;
        _launchStatus?.Clear();
        _launchStatus?.ShowLaunching(account, selectedVersion);

        var versionId = selectedVersion;
        _ = Task.Run(async () =>
        {
            var (success, error) = await LaunchService.LaunchAsync(versionId);
            Application.Current.Dispatcher.Invoke(() =>
            {
                _launching = false;
                if (success)
                    _launchStatus?.ShowSuccess("");
                else
                    _launchStatus?.ShowFailure(error ?? "启动失败，未知原因");
            });
        });
    }

    // ===================== 游戏进程管理（右下角圆圈） =====================

    private void OnGameExit(string procId, string versionId, int code)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (ProcessPopup.IsOpen) RenderProcessList();
            else UpdateProcessBadge();
        });
    }

    private void ProcessButton_Click(object sender, RoutedEventArgs e)
    {
        RenderProcessList();
        ProcessPopup.IsOpen = !ProcessPopup.IsOpen;
    }

    private void UpdateProcessBadge()
    {
        var count = LaunchService.GetRunningGames().Count(g => g.Running);
        ProcessCountText.Text = count.ToString();
        Badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderProcessList()
    {
        UpdateProcessBadge();
        ProcessListPanel.Children.Clear();
        var games = LaunchService.GetRunningGames().Where(g => g.Running).ToList();
        if (games.Count == 0)
        {
            ProcessListPanel.Children.Add(new TextBlock
            {
                Text = "暂无运行中的游戏进程。",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                Margin = new Thickness(6, 4, 0, 4),
            });
            return;
        }

        foreach (var g in games)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = g.VersionId, FontSize = 13, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            });
            info.Children.Add(new TextBlock
            {
                Text = $"PID: {g.ProcessId}", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(info, 0);
            row.Children.Add(info);

            var stopBtn = new Button
            {
                Style = (Style)FindResource("ProcessStopButtonStyle"),
                Content = new TextBlock { Text = "停止", FontSize = 11 },
                Tag = g.ProcId,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stopBtn.Click += StopProcess_Click;
            Grid.SetColumn(stopBtn, 1);
            row.Children.Add(stopBtn);

            ProcessListPanel.Children.Add(row);
        }
    }

    private void StopProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string procId })
        {
            LaunchService.Kill(procId);
            RenderProcessList();
        }
    }
}