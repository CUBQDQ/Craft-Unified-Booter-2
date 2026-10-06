// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CraftUnifiedBooter.Controls;
using CraftUnifiedBooter.Pages;
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter;

/// <summary>右上角 toast 提示类型（决定边框颜色）</summary>
public enum ToastType
{
    Success,
    Warning,
    Error
}

public partial class MainWindow : Window
{
    private readonly List<(NavButton Btn, UserControl Page)> _navs = new();
    private readonly HomePage _home = new();

    public MainWindow()
    {
        InitializeComponent();

        var home = _home;
        var game = new GamePage();
        var ai = new AIChatPage();
        var server = new ServerPage();
        var account = new AccountPage();
        var settings = new SettingsPage();
        var personalize = new PersonalizePage();
        var miniApp = new MiniAppPage();
        var about = new AboutPage();

        // 主页快捷操作 → 打开皮肤库 / 版本管理（覆盖层）
        home.SkinLibraryRequested += ShowSkinLibrary;
        home.VersionManagerRequested += ShowVersionManager;
        home.UpdateAvailableRequested += ShowUpdatePrompt;
        SkinLibrary.ExitRequested += HideSkinLibrary;
        VersionManager.ExitRequested += HideVersionManager;

        _navs.Add((Nav0, home));
        _navs.Add((Nav1, game));
        _navs.Add((Nav2, ai));
        _navs.Add((Nav3, server));
        _navs.Add((Nav4, account));
        _navs.Add((Nav5, settings));
        _navs.Add((Nav6, personalize));
        _navs.Add((Nav7, miniApp));
        _navs.Add((Nav8, about));

        SelectNav(0);

        // 开发者模式：控制小程序入口可见性（图片背景由 PersonalizePage 自行控制）
        ApplyDeveloperMode();

        // 系统托盘：支持最小化到托盘 / 下载完成通知
        TrayService.Initialize(this);
        Services.DownloadManager.Completed += OnDownloadCompleted;
        Services.DownloadManager.Progress += OnDownloadProgress;
        Services.DownloadManager.QueueChanged += OnDownloadQueueChanged;

        // 个性化：订阅设置变更，任何外观/布局修改即时生效
        Services.SettingsStore.SettingsChanged += OnSettingsChanged;

        // 启动动画：窗口显示后整体 Q 弹淡入 + 缩放
        Loaded += MainWindow_Loaded;
    }

    /// <summary>启动动画：主界面 Q 弹淡入 + 缩放（让启动器更有生命力）。</summary>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Opacity = 0;
        RootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        RootGrid.RenderTransform = new ScaleTransform(0.96, 0.96);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = UiAnimation.QElastic(10),
        };
        RootGrid.BeginAnimation(OpacityProperty, fade);

        var sx = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        var sy = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        ((ScaleTransform)RootGrid.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        ((ScaleTransform)RootGrid.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }

    // ===================== 个性化：主题 / 主色 / 圆角 / 布局 / 字体 =====================

    private void OnSettingsChanged()
    {
        Dispatcher.BeginInvoke(new Action(ApplyPersonalization));
    }

    /// <summary>统一应用所有个性化设置。</summary>
    private void ApplyPersonalization()
    {
        ApplyBackgroundImage(); // 内部会调用 ApplyTheme 决定透明度
        ApplyAccent();
        ApplyRadius();
        ApplyLayout();
        ApplyFontScale();
        ApplySidebarVersion();
        ApplyDeveloperMode();
    }

    // ===================== 开发者模式 =====================

    private readonly List<DateTime> _versionClicks = new();
    private DispatcherTimer? _rainbowFrameTimer;

    /// <summary>右下角版本号连续点击 5 次（5 秒内）切换开发者模式。</summary>
    private void FooterVersion_Click(object sender, MouseButtonEventArgs e)
    {
        var now = DateTime.Now;
        _versionClicks.Add(now);
        _versionClicks.RemoveAll(t => (now - t).TotalSeconds > 5);
        if (_versionClicks.Count < 5) return;

        _versionClicks.Clear();
        var enabled = !SettingsStore.Current.DeveloperMode;
        SettingsStore.Update(s => s.DeveloperMode = enabled);
        ShowDeveloperToast(enabled);
    }

    /// <summary>应用开发者模式：显示/隐藏导航栏「小程序」入口；关闭时若正停留则跳回主页。</summary>
    private void ApplyDeveloperMode()
    {
        var dev = SettingsStore.Current.DeveloperMode;
        Nav7.Visibility = dev ? Visibility.Visible : Visibility.Collapsed;
        if (!dev && _navs.Count > 7 && _navs[7].Btn.IsSelected)
            SelectNav(0);
    }

    /// <summary>右上角彩色滚动边框提示：开发者模式开关提示。</summary>
    private void ShowDeveloperToast(bool enabled)
    {
        ShowRainbowToast(enabled ? "您已打开开发者模式" : "您已关闭开发者模式");
    }

    /// <summary>
    /// 右上角彩色滚动边框提示：边框为彩虹（或红色系）渐变并持续旋转滚动。
    /// 可选确认按钮（用于下载中关闭确认），带按钮时不自动消失。
    /// </summary>
    private void ShowRainbowToast(string message, double seconds = 1.8,
        Action? onConfirm = null, string? confirmText = null, bool redTheme = false)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // 彩色滚动边框（彩虹 / 红色系）
            var stops = redTheme
                ? new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0xEF, 0x44, 0x44), 0.0),
                    new GradientStop(Color.FromRgb(0xB9, 0x1C, 0x1C), 0.25),
                    new GradientStop(Color.FromRgb(0xF8, 0x71, 0x71), 0.5),
                    new GradientStop(Color.FromRgb(0xB9, 0x1C, 0x1C), 0.75),
                    new GradientStop(Color.FromRgb(0xEF, 0x44, 0x44), 1.0),
                }
                : new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0xF8, 0x71, 0x71), 0.0),
                    new GradientStop(Color.FromRgb(0xF5, 0x9E, 0x0B), 0.2),
                    new GradientStop(Color.FromRgb(0xF5, 0xC2, 0x11), 0.4),
                    new GradientStop(Color.FromRgb(0x4A, 0xDE, 0x80), 0.6),
                    new GradientStop(Color.FromRgb(0x3B, 0x82, 0xF6), 0.8),
                    new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 1.0),
                };
            var borderBrush = new LinearGradientBrush(stops, 0);
            var rot = new RotateTransform(0);
            borderBrush.RelativeTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                });

            var toast = new Border
            {
                Background = Brushes.White,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(2.5),
                CornerRadius = UiTheme.Radius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 430,
                HorizontalAlignment = HorizontalAlignment.Right,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 1,
                    Opacity = 0.2,
                },
                Opacity = 0,
                RenderTransform = new TranslateTransform(90, 0),
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = UiTheme.Radius(5),
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0xF8, 0x71, 0x71), 0),
                        new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 1),
                    },
                    0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                Margin = new Thickness(8, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 290,
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (onConfirm != null)
            {
                var confirmBtn = new Button
                {
                    Content = confirmText ?? "确定",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(10, 4, 10, 4),
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(10, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Template = CreateRedButtonTemplate(),
                };
                confirmBtn.Click += (_, _) =>
                {
                    ToastHost.Children.Remove(toast);
                    onConfirm();
                };
                stack.Children.Add(confirmBtn);
            }

            toast.Child = stack;
            ToastHost.Children.Add(toast);

            // 滑入 + 淡入
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = UiAnimation.QElastic(10),
            });
            var slideIn = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(560))
            {
                EasingFunction = UiAnimation.QElastic(7),
            };
            (toast.RenderTransform as TranslateTransform)!.BeginAnimation(TranslateTransform.XProperty, slideIn);

            // 无按钮时自动消失；有按钮时等待用户操作
            if (onConfirm == null)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(seconds * 1000) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(toast.Opacity, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (_, _) => ToastHost.Children.Remove(toast);
                    toast.BeginAnimation(OpacityProperty, fadeOut);
                };
                timer.Start();
            }
        });
    }

    /// <summary>
    /// 右上角确认弹窗：紫色主题 + 彩虹旋转边框（与开发者模式光效一致，持续旋转）。
    /// 显示「是 / 否」两个按钮，默认超时（5 秒）自动取消（相当于选择「否」）。用于版本管理删除确认。
    /// </summary>
    public void ShowConfirmDialog(string message, Action onConfirm,
        string confirmText = "是", string cancelText = "否", double seconds = 5)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // 彩虹渐变（紫色为主）旋转边框
            var stops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0xA5, 0x8F, 0xFA), 0.0),
                new GradientStop(Color.FromRgb(0x7C, 0x3A, 0xED), 0.2),
                new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 0.4),
                new GradientStop(Color.FromRgb(0x3B, 0x82, 0xF6), 0.6),
                new GradientStop(Color.FromRgb(0xEF, 0x44, 0x44), 0.8),
                new GradientStop(Color.FromRgb(0xA5, 0x8F, 0xFA), 1.0),
            };
            var borderBrush = new LinearGradientBrush(stops, 0);
            var rot = new RotateTransform(0);
            borderBrush.RelativeTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                });

            var toast = new Border
            {
                Background = Brushes.White,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(2.5),
                CornerRadius = UiTheme.Radius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 460,
                HorizontalAlignment = HorizontalAlignment.Right,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 1,
                    Opacity = 0.2,
                },
                Opacity = 0,
                RenderTransform = new TranslateTransform(90, 0),
            };

            // 标题行：紫色圆点 + “版本管理”
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = UiTheme.Radius(5),
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 0),
                        new GradientStop(Color.FromRgb(0xA5, 0x8F, 0xFA), 1),
                    }, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock
            {
                Text = "版本管理",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                Margin = new Thickness(18, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
            });

            // 操作行：倒计时 + 是 / 否
            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(18, 10, 0, 0),
            };
            var countdown = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            var yesBtn = new Button
            {
                Content = confirmText,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 4, 12, 4),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Template = CreateRedButtonTemplate(),
            };
            var noBtn = new Button
            {
                Content = cancelText,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x42, 0x51)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 4, 12, 4),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Template = CreateGrayButtonTemplate(),
            };
            actionRow.Children.Add(countdown);
            actionRow.Children.Add(yesBtn);
            actionRow.Children.Add(noBtn);
            root.Children.Add(actionRow);

            toast.Child = root;
            ToastHost.Children.Add(toast);

            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = UiAnimation.QElastic(10),
            });
            var slideIn = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(560))
            {
                EasingFunction = UiAnimation.QElastic(7),
            };
            (toast.RenderTransform as TranslateTransform)!.BeginAnimation(TranslateTransform.XProperty, slideIn);

            // 关闭并清理
            void Close() => ToastHost.Children.Remove(toast);

            // 倒计时：超时自动取消（相当于选「否」）
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            var remain = (int)Math.Ceiling(seconds);
            countdown.Text = $"{remain} 秒后自动取消";
            timer.Tick += (_, _) =>
            {
                remain--;
                if (remain > 0)
                {
                    countdown.Text = $"{remain} 秒后自动取消";
                    return;
                }
                timer.Stop();
                Close();
            };
            timer.Start();

            yesBtn.Click += (_, _) => { Close(); onConfirm(); };
            noBtn.Click += (_, _) => { Close(); };
        });
    }

    /// <summary>红色圆角按钮模板（用于彩色滚动提示中的确认按钮）。</summary>
    private static ControlTemplate CreateRedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    /// <summary>灰色圆角按钮模板（用于彩色滚动提示中的取消按钮）。</summary>
    private static ControlTemplate CreateGrayButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    /// <summary>在启动器窗口边缘显示一圈连续渐变的彩色滚动光效（深色彩虹，约 3 秒后自动消失）。</summary>
    private void ShowRainbowFrame()
    {
        Dispatcher.BeginInvoke(() =>
        {
            // 连续彩虹渐变（深色系），沿边框旋转滚动，颜色平滑过渡
            var brush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0xFF, 0x2D, 0x2D), 0.00), // 深红
                    new GradientStop(Color.FromRgb(0xFF, 0x8C, 0x00), 0.14), // 暗橙
                    new GradientStop(Color.FromRgb(0xFF, 0xC1, 0x07), 0.28), // 琥珀黄
                    new GradientStop(Color.FromRgb(0x00, 0xC8, 0x53), 0.42), // 深绿
                    new GradientStop(Color.FromRgb(0x1E, 0x88, 0xE5), 0.57), // 深蓝
                    new GradientStop(Color.FromRgb(0x8E, 0x24, 0xAA), 0.71), // 深紫
                    new GradientStop(Color.FromRgb(0xE9, 0x1E, 0x63), 0.85), // 深粉
                    new GradientStop(Color.FromRgb(0xFF, 0x2D, 0x2D), 1.00), // 回到深红（无缝）
                },
                45); // 初始角度让四边都有颜色
            var rot = new RotateTransform(0);
            brush.RelativeTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.5))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                });

            RainbowFrame.BorderBrush = brush;
            RainbowFrame.Visibility = Visibility.Visible;

            // 3 秒后隐藏光效并停止动画
            _rainbowFrameTimer?.Stop();
            _rainbowFrameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _rainbowFrameTimer.Tick += (_, _) =>
            {
                _rainbowFrameTimer?.Stop();
                RainbowFrame.Visibility = Visibility.Collapsed;
                if (RainbowFrame.BorderBrush is LinearGradientBrush b && b.RelativeTransform is RotateTransform r)
                    r.BeginAnimation(RotateTransform.AngleProperty, null);
                RainbowFrame.BorderBrush = null;
            };
            _rainbowFrameTimer.Start();
        });
    }

    /// <summary>加载 CUB/backgrounds 下的背景图（如果启用且存在），并据此刷新主题透明度。</summary>
    private void ApplyBackgroundImage()
    {
        _bgPaths = Store.GetBackgroundImagePaths();

        if (!SettingsStore.Current.BackgroundImage || _bgPaths.Count == 0)
        {
            BackgroundImage.Source = null;
            BackgroundImageHost.Visibility = Visibility.Collapsed;
            BackgroundImage.Effect = null;
            BackgroundImage.Opacity = 1;
            StopBackgroundTimer();
            ApplyTheme();
            return;
        }

        // 索引越界时回绕到有效范围
        _bgIndex = ((SettingsStore.Current.BackgroundIndex % _bgPaths.Count) + _bgPaths.Count) % _bgPaths.Count;
        if (!LoadBackgroundImage(_bgPaths[_bgIndex]))
        {
            BackgroundImageHost.Visibility = Visibility.Collapsed;
            StopBackgroundTimer();
            ApplyTheme();
            return;
        }
        BackgroundImageHost.Visibility = Visibility.Visible;
        ApplyBackgroundEffects();

        // 自动轮播：多张图片时按间隔循环切换；手动模式则停止计时器
        if (SettingsStore.Current.BackgroundMode == "auto" && _bgPaths.Count > 1)
            StartBackgroundTimer(SettingsStore.Current.BackgroundInterval);
        else
            StopBackgroundTimer();

        ApplyTheme();
    }

    // ===================== 背景图片：多图手动 / 自动切换 =====================

    private DispatcherTimer? _bgTimer;
    private List<string> _bgPaths = new();
    private int _bgIndex;

    /// <summary>背景图数量</summary>
    public int BackgroundCount => _bgPaths.Count;

    /// <summary>当前背景图索引（从 0 开始）</summary>
    public int BackgroundIndex => _bgPaths.Count == 0 ? 0 : _bgIndex;

    /// <summary>重新扫描 CUB/backgrounds 并应用当前背景（用户新增图片后调用）。</summary>
    public void ReloadBackgrounds() => ApplyBackgroundImage();

    /// <summary>手动切换到上一张 / 下一张（delta = ±1），并持久化索引。</summary>
    public void SwitchBackground(int delta)
    {
        // 重新扫描目录，保证用户新放入的图片能被纳入切换
        _bgPaths = Store.GetBackgroundImagePaths();
        if (_bgPaths.Count < 2) return;
        _bgIndex = ((_bgIndex + delta) % _bgPaths.Count + _bgPaths.Count) % _bgPaths.Count;
        if (LoadBackgroundImage(_bgPaths[_bgIndex]))
        {
            BackgroundImageHost.Visibility = Visibility.Visible;
            ApplyBackgroundEffects();
            ApplyTheme();
        }
        SettingsStore.Current.BackgroundIndex = _bgIndex;
        SettingsStore.Save();
        // 手动切换后重置计时器，避免自动轮播紧接着又切走
        if (SettingsStore.Current.BackgroundMode == "auto")
            StartBackgroundTimer(SettingsStore.Current.BackgroundInterval);
    }

    private void StartBackgroundTimer(int seconds)
    {
        if (_bgTimer == null)
        {
            _bgTimer = new DispatcherTimer();
            _bgTimer.Tick += BackgroundTimer_Tick;
        }
        _bgTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 600));
        _bgTimer.Stop();
        _bgTimer.Start();
    }

    private void StopBackgroundTimer() => _bgTimer?.Stop();

    /// <summary>应用背景图的不透明度与模糊设置（参考 PCL2 的 UiBackgroundOpacity / UiBackgroundBlur）。</summary>
    public void ApplyBackgroundEffects()
    {
        BackgroundImage.Opacity = Math.Clamp(SettingsStore.Current.BackgroundOpacity, 0, 100) / 100.0;

        var blur = SettingsStore.Current.BackgroundBlur;
        if (blur > 0)
        {
            if (BackgroundImage.Effect is not System.Windows.Media.Effects.BlurEffect blurEffect)
            {
                blurEffect = new System.Windows.Media.Effects.BlurEffect();
                BackgroundImage.Effect = blurEffect;
            }
            blurEffect.Radius = blur;
        }
        else
        {
            BackgroundImage.Effect = null;
        }
    }

    private void BackgroundTimer_Tick(object? sender, EventArgs e)
    {
        if (_bgPaths.Count < 2) { StopBackgroundTimer(); return; }
        _bgIndex = (_bgIndex + 1) % _bgPaths.Count;
        if (LoadBackgroundImage(_bgPaths[_bgIndex]))
            ApplyBackgroundEffects();
        // 轮播时静默持久化索引，不触发完整个性化刷新
        SettingsStore.Current.BackgroundIndex = _bgIndex;
        SettingsStore.Save();
    }

    /// <summary>按窗口实际像素尺寸解码加载图片：超大图不再全分辨率解码（避免卡顿），小图不做放大会糊。</summary>
    private bool LoadBackgroundImage(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var dipW = ActualWidth > 0 ? ActualWidth : Width;
            var targetPx = Math.Clamp((int)Math.Round(dipW * dpi), 1280, 2560);

            // 只读取文件头拿原图宽度，避免为探测尺寸而全解码
            int srcW = 0;
            using (var fs = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0) srcW = decoder.Frames[0].PixelWidth;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            if (srcW > targetPx) bmp.DecodePixelWidth = targetPx;
            bmp.EndInit();
            bmp.Freeze();

            BackgroundImage.Source = bmp;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>主题：cub 纯白 / glass 轻量毛玻璃；启用背景图时整体改为半透明叠加。</summary>
    private void ApplyTheme()
    {
        // "liquid" 主题已移除，旧配置兼容为 glass
        var theme = SettingsStore.Current.Theme == "liquid" ? "glass" : SettingsStore.Current.Theme;
        var hasImage = BackgroundImageHost.Visibility == Visibility.Visible;
        var res = Application.Current.Resources;

        if (hasImage)
        {
            // 有背景图：页面底色适度透明让图片可见，但卡片/侧边栏保持较高不透明保证文字可读
            SetAcrylic(false, Colors.White, 0);
            Background = Brushes.Transparent;
            // 图片上的一层薄雾，压暗背景、保证文字可读
            BackgroundImageOverlay.Background = FrozenBrush(theme == "glass" ? "#40FFFFFF" : "#59FFFFFF");

            var chrome = FrozenBrush(theme == "glass" ? "#A6FFFFFF" : "#B8FFFFFF");   // 标题栏/侧边栏 65%~72% 白
            res["PageBackground"] = FrozenBrush(theme == "glass" ? "#73F9FAFB" : "#8CF9FAFB"); // 页面底色 45%~55%
            res["CardBackground"] = FrozenBrush(theme == "glass" ? "#D9FFFFFF" : "#E6FFFFFF"); // 卡片 85%~90% 白，内容清晰可读
            // 内容区交给页面自身的 PageBackground 控制，容器透明才能看到图片
            SetChromeBackgrounds(chrome, Brushes.Transparent);
        }
        else if (theme == "glass")
        {
            SetAcrylic(true, Colors.White, 0x80);
            Background = Brushes.Transparent;
            BackgroundImageOverlay.Background = FrozenBrush("#00000000");
            res["PageBackground"] = FrozenBrush("#B3F9FAFB");
            res["CardBackground"] = FrozenBrush("#FFFFFF");
            SetChromeBackgrounds(FrozenBrush("#80FFFFFF"), FrozenBrush("#B3F9FAFB"));
        }
        else
        {
            SetAcrylic(false, Colors.White, 0);
            Background = FrozenBrush("#FFFFFF");
            BackgroundImageOverlay.Background = FrozenBrush("#E6FFFFFF");
            res["PageBackground"] = FrozenBrush("#F9FAFB");
            res["CardBackground"] = FrozenBrush("#FFFFFF");
            SetChromeBackgrounds(Brushes.White, FrozenBrush("#F9FAFB"));
        }
    }

    /// <summary>统一设置标题栏/侧边栏/状态栏/内容区/覆盖层背景。</summary>
    private void SetChromeBackgrounds(Brush chrome, Brush content)
    {
        TitleBar.Background = chrome;
        SidebarRoot.Background = chrome;
        SidebarVersionBar.Background = chrome;
        StatusBar.Background = chrome;
        ContentArea.Background = content;
        SkinLibraryHost.Background = content;
        VersionManagerHost.Background = content;
        DownloadOverlayHost.Background = content;
    }

    /// <summary>个性化颜色：更新全局主色画刷与颜色资源（按钮、高亮、选中项、渐变）。</summary>
    private void ApplyAccent()
    {
        var (main, dark, light, soft) = Services.UiTheme.AccentColors(SettingsStore.Current.Accent);
        var res = Application.Current.Resources;
        res["AccentBrush"] = FrozenBrush(main);
        res["AccentDarkBrush"] = FrozenBrush(dark);
        res["AccentLightBrush"] = FrozenBrush(light);
        res["AccentSoftBrush"] = FrozenBrush(soft);
        // 颜色资源：供 GradientStop.Color 等需要 Color 类型的地方使用
        res["AccentColor"] = main;
        res["AccentDarkColor"] = dark;
        res["AccentLightColor"] = light;
        res["AccentSoftColor"] = soft;
    }

    /// <summary>圆角风格：更新全局圆角资源（直角 0 / 标准 / 大圆角放大）。</summary>
    private void ApplyRadius()
    {
        var scale = Services.UiTheme.RadiusScale;
        var res = Application.Current.Resources;
        res["ControlRadius"] = new CornerRadius(8 * scale);
        res["CardRadius"] = new CornerRadius(14 * scale);
        res["Radius0"] = UiTheme.Radius(0);
        res["Radius2"] = new CornerRadius(2 * scale);
        res["Radius3"] = new CornerRadius(3 * scale);
        res["Radius4"] = new CornerRadius(4 * scale);
        res["Radius6"] = new CornerRadius(6 * scale);
        res["Radius7"] = new CornerRadius(7 * scale);
        res["Radius8"] = new CornerRadius(8 * scale);
        res["Radius9"] = new CornerRadius(9 * scale);
        res["Radius10"] = new CornerRadius(10 * scale);
        res["Radius12"] = new CornerRadius(12 * scale);
        res["Radius14"] = new CornerRadius(14 * scale);
        res["Radius15"] = new CornerRadius(15 * scale);
        res["Radius16"] = new CornerRadius(16 * scale);
        res["Radius20"] = new CornerRadius(20 * scale);
        res["Radius24"] = new CornerRadius(24 * scale);
    }

    /// <summary>布局：侧边栏宽度 + 标题栏高度（Toast 位置跟随标题栏，避免重叠）。</summary>
    private void ApplyLayout()
    {
        var sidebar = SettingsStore.Current.SidebarWidth;
        var width = sidebar switch { "narrow" => 130, "wide" => 180, _ => 150 };
        SidebarColumn.Width = new GridLength(width);
        NavIndicator.Width = width - 20; // NavHost 左右各 10 边距

        var titlebar = SettingsStore.Current.TitleBarHeight;
        var height = titlebar switch { "compact" => 40, "large" => 56, _ => 48 };
        TitleBarRow.Height = new GridLength(height);
        // Toast 显示在标题栏下方 8px，随标题栏高度自适应
        ToastHost.Margin = new Thickness(0, height + 8, 16, 0);
    }

    /// <summary>字体缩放：对根网格 LayoutTransform 整体缩放，并按比例调整窗口初始尺寸防止内容被裁切。</summary>
    private double _currentFontScale = 1.0;
    private void ApplyFontScale()
    {
        var factor = SettingsStore.Current.FontSize switch
        {
            "small" => 0.9,
            "large" => 1.12,
            _ => 1.0,
        };
        // 只有字体档位真正变化时才调整窗口尺寸，避免切换其它设置时重置用户手动调整过的窗口大小
        if (Math.Abs(_currentFontScale - factor) < 0.001) return;
        _currentFontScale = factor;

        RootScale.ScaleX = factor;
        RootScale.ScaleY = factor;
        if (WindowState != WindowState.Maximized)
        {
            Width = Math.Round(1100 * factor);
            Height = Math.Round(700 * factor);
            MinWidth = Math.Round(980 * factor);
            MinHeight = Math.Round(640 * factor);
        }
    }

    /// <summary>侧边栏版本号显示/隐藏。</summary>
    private void ApplySidebarVersion()
    {
        var show = SettingsStore.Current.ShowSidebarVersion;
        SidebarVersionBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SidebarVersionRow.Height = new GridLength(show ? 40 : 0);
    }

    /// <summary>主色 → 四档色板（主色 / 深色 / 浅色 / 极浅色）。委托给 UiTheme，供各页面共用。</summary>
    internal static (Color Main, Color Dark, Color Light, Color Soft) AccentColors(string accent)
        => Services.UiTheme.AccentColors(accent);

    private static Brush FrozenBrush(string hex)
        => FrozenBrush((Color)ColorConverter.ConvertFromString(hex));

    private static Brush FrozenBrush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    /// <summary>右上角弹小提示（toast），约 1 秒后自动消失，替代原生 MessageBox。</summary>
    public void ShowToast(string message, ToastType type = ToastType.Success)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // 边框颜色：成功绿 / 警告黄 / 错误红
            var accent = type switch
            {
                ToastType.Error => Color.FromRgb(0xF8, 0x71, 0x71),
                ToastType.Warning => Color.FromRgb(0xF5, 0x9E, 0x0B),
                _ => Color.FromRgb(0x4A, 0xDE, 0x80),
            };

            // 白色背景，右侧彩色边框，初始在右侧外（用于滑入动画）
            var toast = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(2),
                CornerRadius = UiTheme.Radius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 400,
                HorizontalAlignment = HorizontalAlignment.Right,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 14,
                    ShadowDepth = 1,
                    Opacity = 0.15,
                },
                Opacity = 0,
                RenderTransform = new TranslateTransform(90, 0),
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = UiTheme.Radius(4),
                Background = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                Margin = new Thickness(8, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 330,
                VerticalAlignment = VerticalAlignment.Center,
            });
            toast.Child = stack;

            ToastHost.Children.Add(toast);

            // 从右往左滑入 + 淡入
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = UiAnimation.QElastic(10),
            });
            var slideIn = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(560))
            {
                EasingFunction = UiAnimation.QElastic(7),
            };
            (toast.RenderTransform as TranslateTransform)!.BeginAnimation(TranslateTransform.XProperty, slideIn);

            // 停留约 1 秒后淡出移除
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation(toast.Opacity, 0, TimeSpan.FromMilliseconds(250));
                fadeOut.Completed += (_, _) => ToastHost.Children.Remove(toast);
                toast.BeginAnimation(OpacityProperty, fadeOut);
            };
            timer.Start();
        });
    }

    // ===================== 更新提示弹窗 =====================
    private UpdateInfo? _pendingUpdate;

    public void ShowUpdatePrompt(UpdateInfo info)
    {
        _pendingUpdate = info;
        UpdateVersionText.Text = $"当前版本 v{UpdateService.CurrentVersion} → 最新 v{info.Version}";
        UpdateChangelogText.Text = string.IsNullOrWhiteSpace(info.Changelog) ? "暂无更新说明。" : info.Changelog;
        UpdateSizeText.Text = $"更新包大小：{FormatSize(info.Size)}";

        // 强制更新：不提供跳过/关闭选项，只能立即更新；并将窗口置顶防止被忽略
        var mandatory = info.Mandatory;
        UpdateCloseBtn.Visibility = mandatory ? Visibility.Collapsed : Visibility.Visible;
        UpdateLaterBtn.Visibility = mandatory ? Visibility.Collapsed : Visibility.Visible;
        Topmost = mandatory;

        // 重置下载状态
        UpdateProgressBar.Visibility = Visibility.Collapsed;
        UpdateProgressText.Visibility = Visibility.Collapsed;
        UpdateDownloadBtn.IsEnabled = true;
        UpdateDownloadBtn.Content = "立即更新";

        UpdateOverlayHost.Visibility = Visibility.Visible;
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        UpdateOverlayHost.Visibility = Visibility.Collapsed;
        _pendingUpdate = null;
        Topmost = false;
    }

    /// <summary>立即更新：在启动器内下载新版本，完成后自动替换自身并重启。</summary>
    private async void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        var info = _pendingUpdate;
        if (info == null) return;

        UpdateDownloadBtn.IsEnabled = false;
        UpdateDownloadBtn.Content = "正在下载…";
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressText.Visibility = Visibility.Visible;
        UpdateProgressText.Text = "正在连接更新服务器…";

        try
        {
            // 下载到临时目录
            var fileName = string.IsNullOrWhiteSpace(info.FileName) ? info.Url : info.FileName;
            var dest = Path.Combine(Path.GetTempPath(), "CUBUpdate",
                $"CraftUnifiedBooter-{SanitizeFileName(info.Version)}{Path.GetExtension(fileName)}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            await Downloader.DownloadFileAsync(info.Url, dest, info.Size, (total, downloaded) =>
            {
                var pct = total > 0 ? (int)(downloaded * 100 / total) : 0;
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateProgressBar.Value = Math.Clamp(pct, 0, 100);
                    UpdateProgressText.Text = $"正在下载 {FormatSize(downloaded)} / {FormatSize(total)}";
                });
            });

            // 安全校验（P0）：SHA256 强制校验；服务器提供签名时再做 RSA 验签
            UpdateProgressText.Text = "正在校验更新包…";
            if (!UpdateService.VerifyUpdate(dest, info.Sha256, info.Signature))
            {
                try { File.Delete(dest); } catch { /* ignore */ }
                throw new Exception("更新包校验失败（哈希或签名不匹配），已拒绝安装。");
            }

            // 服务器尚未配置 RSA 签名时，告知用户本次为降级校验
            if (string.IsNullOrWhiteSpace(info.Signature))
            {
                Dispatcher.BeginInvoke(() => ShowToast("服务器未配置签名，本次更新仅做 SHA256 校验", ToastType.Warning));
            }

            UpdateProgressText.Text = "下载完成，正在替换启动器…";
            await Task.Delay(300); // 让界面刷新
            ApplyUpdate(dest);
            // ApplyUpdate 会执行替换脚本并关闭当前进程，下面的代码不会继续执行
        }
        catch (Exception ex)
        {
            UpdateOverlayHost.Visibility = Visibility.Collapsed;
            _pendingUpdate = null;
            Topmost = false;
            ShowToast($"更新失败：{ex.Message}", ToastType.Error);
        }
    }

    /// <summary>替换当前启动器 exe：生成脚本等待本进程退出 → 删旧 exe → 新 exe 改名 → 重启。</summary>
    private static void ApplyUpdate(string downloadedFile)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            throw new Exception("无法定位当前启动器文件");

        var currentDir = Path.GetDirectoryName(currentExe)!;

        // 若下载的不是 exe（例如用户上传的是 zip），解压后取其中的 exe
        var sourceExe = downloadedFile;
        var ext = Path.GetExtension(downloadedFile).ToLowerInvariant();
        if (ext != ".exe")
        {
            var extractDir = Path.Combine(Path.GetTempPath(), "CUBUpdate", "extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(downloadedFile, extractDir);
            sourceExe = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new Exception("更新包内没有找到可执行文件");
        }

        // 新 exe 先复制到启动器目录的临时名（与旧 exe 同盘，move 才能原子替换）
        var newExe = Path.Combine(currentDir, "CraftUnifiedBooter.new.exe");
        File.Copy(sourceExe, newExe, true);

        // 生成替换脚本：等本进程退出 → 删旧 exe → 新 exe 改名为旧 exe → 重启 → 删除脚本
        var script = Path.Combine(Path.GetTempPath(), $"CUB-update-{Guid.NewGuid():N}.bat");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("timeout /t 2 /nobreak >nul");
        sb.AppendLine($"del /f /q \"{currentExe}\"");
        sb.AppendLine($"move /y \"{newExe}\" \"{currentExe}\"");
        sb.AppendLine($"start \"\" \"{currentExe}\"");
        sb.AppendLine("del \"%~f0\"");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(script, sb.ToString(), Encoding.GetEncoding(936)); // cmd 用 GBK，避免中文路径乱码

        Process.Start(new ProcessStartInfo(script) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        Application.Current.Shutdown();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>单实例重复启动时：窗口边缘显示一圈彩色滚动光效，并把窗口强制置顶到所有应用上方。</summary>
    public void BringToFront()
    {
        Dispatcher.BeginInvoke(() =>
        {
            // 窗口边缘彩色滚动光效（替代右上角弹窗）
            ShowRainbowFrame();

            // 恢复窗口：最小化则还原，隐藏则显示
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Show();

            var handle = new WindowInteropHelper(this).Handle;
            ShowWindow(handle, SW_RESTORE);
            Activate();
            Topmost = true;                 // 置顶
            SetForegroundWindow(handle);    // 强制到前台
            FlashWindow(handle, true);    // 任务栏闪烁提醒
            Focus();

            // 保持置顶约 1.2 秒后自动取消，避免一直压在其他窗口上
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Topmost = false;
            };
            timer.Start();
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] units = { "B", "KB", "MB", "GB" };
        int i = 0;
        double n = bytes;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return i == 0 ? $"{n} {units[i]}" : $"{n:0.##} {units[i]}";
    }

    /// <summary>下载完成 → 系统气泡通知（个性化开关控制）</summary>
    private void OnDownloadCompleted(Services.DownloadTask task)
    {
        if (!Services.SettingsStore.Current.DownloadNotify || !task.Success) return;
        Dispatcher.BeginInvoke(() => TrayService.ShowBalloon("下载完成", task.Name));
    }

    // ===== 标题栏中央下载进度胶囊（数量 / 速度 / 剩余文件） =====
    private bool _pillUpdateQueued;
    private long _lastTotalDownloaded;
    private DateTime _lastSpeedTime;
    private double _currentSpeed;

    private void OnDownloadProgress(Services.DownloadTask t)
    {
        if (_pillUpdateQueued) return;
        _pillUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _pillUpdateQueued = false;
            UpdateDownloadPill();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnDownloadQueueChanged()
    {
        if (_pillUpdateQueued) return;
        _pillUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _pillUpdateQueued = false;
            UpdateDownloadPill();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateDownloadPill()
    {
        var active = Services.DownloadManager.GetActiveTasks();
        if (active.Count == 0)
        {
            DownloadPill.Visibility = Visibility.Collapsed;
            return;
        }
        DownloadPill.Visibility = Visibility.Visible;

        // 数量：进行中的下载任务数
        PillCount.Text = $"数量:{active.Count}";

        // 速度：按已下载字节的增量估算，并用指数移动平均平滑（避免数字剧烈抖动）
        long downloaded = active.Sum(t => t.Downloaded);
        var now = DateTime.UtcNow;
        if (_lastSpeedTime != default && now > _lastSpeedTime)
        {
            var dt = (now - _lastSpeedTime).TotalSeconds;
            if (dt > 0)
            {
                var instant = Math.Max(0, (downloaded - _lastTotalDownloaded) / dt);
                // 新采样占 30%、历史占 70%，让速度平滑过渡
                _currentSpeed = _currentSpeed <= 0 ? instant : _currentSpeed * 0.7 + instant * 0.3;
            }
        }
        _lastTotalDownloaded = downloaded;
        _lastSpeedTime = now;
        // 速度 0 时显示“连接中…”，避免用户误以为卡死（30 秒无数据会自动切换下载源）
        PillSpeed.Text = _currentSpeed <= 0 ? "连接中…" : $"速度:{FormatSpeed(_currentSpeed)}";

        // 剩余文件：整个版本包的总文件数 - 已下载完成文件数（整体统计）
        int remain = active.Sum(t => Math.Max(0, t.TotalFiles - t.DownloadedFiles));
        PillRemain.Text = $"剩余:{remain}";

        // 横向进度条：总进度百分比
        long total = active.Sum(t => t.Total);
        double ratio = total > 0 ? Math.Clamp(downloaded * 1.0 / total, 0, 1) : 0;
        if (PillTrack.ActualWidth > 0)
            PillFill.Width = PillTrack.ActualWidth * ratio;
    }

    // ===== 下载管理覆盖层（标题栏"更多"） =====

    public bool IsDownloadOverlayVisible => DownloadOverlayHost.Visibility == Visibility.Visible;

    // ===== 覆盖层 Q 弹动画辅助 =====

    /// <summary>显示覆盖层并播放 Q 弹入场动画（淡入 + 上移回弹 + 缩放回弹）。</summary>
    private static void ShowOverlayBounce(Grid host)
    {
        host.Opacity = 0;
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform(0, 12));
        group.Children.Add(new ScaleTransform(0.97, 0.97));
        host.RenderTransform = group;
        host.Visibility = Visibility.Visible;

        host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = UiAnimation.QElastic(10),
        });
        var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(620))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        ((TranslateTransform)group.Children[0]).BeginAnimation(TranslateTransform.YProperty, slide);
        var sx = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(620))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        var sy = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(620))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }

    /// <summary>隐藏覆盖层并播放 Q 弹退场动画（淡出 + 轻微缩小，动画结束后隐藏）。</summary>
    private static void HideOverlayBounce(Grid host)
    {
        var fade = new DoubleAnimation(host.Opacity, 0, TimeSpan.FromMilliseconds(230))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            host.Visibility = Visibility.Collapsed;
            host.Opacity = 1;
            host.RenderTransform = null;
        };
        host.BeginAnimation(OpacityProperty, fade);

        if (host.RenderTransform is TransformGroup group)
        {
            var scale = (ScaleTransform)group.Children[1];
            var sx = new DoubleAnimation(scale.ScaleX, 0.97, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            var sy = new DoubleAnimation(scale.ScaleY, 0.97, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }
    }

    public void ShowDownloadOverlay()
    {
        ShowOverlayBounce(DownloadOverlayHost);
        DownloadOverlay.Refresh();
    }

    public void HideDownloadOverlay()
    {
        HideOverlayBounce(DownloadOverlayHost);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (IsDownloadOverlayVisible)
            HideDownloadOverlay();
        else
            ShowDownloadOverlay();
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F0} KB/s";
        return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse((string)b.Tag, out int idx))
            SelectNav(idx);
    }

    public void SelectNav(int index)
    {
        for (int i = 0; i < _navs.Count; i++)
            _navs[i].Btn.IsSelected = i == index;

        // 指示器平滑滑动 (每项高 40 + 间距 2)。
        // 开发者模式关闭时 Nav7（小程序）隐藏，其后的「关于」上移一位，指示器位置相应减 1。
        var visualIndex = index;
        if (!SettingsStore.Current.DeveloperMode && index > 7)
            visualIndex = index - 1;

        var anim = new DoubleAnimation(visualIndex * 42.0, TimeSpan.FromMilliseconds(520));
        anim.EasingFunction = UiAnimation.QElastic(7);
        NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, anim);

        ContentHost.Content = _navs[index].Page;

        // 主页：额外播放卡片 Q 弹入场动画（皮肤库 / 版本管理 / 公告）
        if (index == 0)
            _home.PlayCardEntrance();

        // 个性化：页面切换动画（Q 弹：淡入 + 上移回弹 + 缩放回弹）
        if (Services.SettingsStore.Current.PageAnimation)
        {
            ContentHost.Opacity = 0;
            ContentHost.RenderTransformOrigin = new Point(0.5, 0.5);
            var group = new TransformGroup();
            group.Children.Add(new TranslateTransform(0, 18));
            group.Children.Add(new ScaleTransform(0.98, 0.98));
            ContentHost.RenderTransform = group;

            ContentHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = UiAnimation.QElastic(10),
            });
            var slide = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(620))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            ((TranslateTransform)group.Children[0]).BeginAnimation(TranslateTransform.YProperty, slide);
            var sx = new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(620))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            var sy = new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(620))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }
        else
        {
            ContentHost.BeginAnimation(OpacityProperty, null);
            ContentHost.RenderTransform = null;
            ContentHost.Opacity = 1;
        }
    }

    /// <summary>打开皮肤库覆盖层</summary>
    private void ShowSkinLibrary()
    {
        SkinLibrary.Refresh();
        ShowOverlayBounce(SkinLibraryHost);
    }

    /// <summary>退出皮肤库，回到主页并刷新皮肤展示</summary>
    private void HideSkinLibrary()
    {
        HideOverlayBounce(SkinLibraryHost);
        _home.Refresh();
    }

    /// <summary>打开版本管理覆盖层</summary>
    private void ShowVersionManager()
    {
        VersionManager.Refresh();
        ShowOverlayBounce(VersionManagerHost);
    }

    /// <summary>退出版本管理，回到主页并刷新</summary>
    private void HideVersionManager()
    {
        HideOverlayBounce(VersionManagerHost);
        _home.Refresh();
    }

    /// <summary>打开下载管理页（从游戏版本页的「下载管理」入口进入）；复用同一实例，避免每次重新解析 XAML 造成卡顿。</summary>
    public void ShowDownloadPage()
    {
        _downloadPage ??= new DownloadPage();
        ContentHost.Content = _downloadPage;
    }

    private DownloadPage? _downloadPage;

    // 标题栏拖拽：使用系统 DragMove() 获得与正常窗口一致的流畅拖拽体验；
    // 同时拦截 Aero Snap 的 SC_MAXIMIZE 命令，实现"禁用拖到顶部自动全屏"。
    private static bool _blockSnapMaximize;

    /// <summary>标题栏拖拽：系统级流畅拖拽（已禁用"拖到顶部自动全屏"）；双击切换最大化</summary>
    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeToggle_Click(sender, e);
            return;
        }
        if (e.ButtonState != MouseButtonState.Pressed) return;

        // 最大化状态下拖动标题栏 → 先还原（Windows 标准行为，避免卡在最大化）
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            return;
        }

        // 拖动期间阻止 Aero Snap 最大化（拖到屏幕顶部不自动全屏）
        _blockSnapMaximize = true;
        try
        {
            DragMove();
        }
        catch
        {
            // 极端情况下（按键已抬起）DragMove 会抛异常，忽略即可
        }
        finally
        {
            _blockSnapMaximize = false;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Services.SettingsStore.Current.MinimizeToTray)
        {
            Hide();
            TrayService.ShowBalloon("Craft Unified Booter", "启动器已最小化到托盘，点击托盘图标可恢复。");
        }
        else
        {
            TrayService.ExitApp();
        }
    }

    /// <summary>拦截关闭：开启"最小化到托盘"时 Alt+F4 等关闭方式也改为最小化到托盘；下载中弹红色确认。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Services.SettingsStore.Current.MinimizeToTray && !TrayService.IsExiting)
        {
            e.Cancel = true;
            Hide();
            TrayService.ShowBalloon("Craft Unified Booter", "启动器已最小化到托盘，点击托盘图标可恢复。");
            return;
        }

        // 下载中关闭：右上角红色滚动边框确认提示
        if (!TrayService.IsExiting && Services.DownloadManager.HasActive)
        {
            e.Cancel = true;
            ShowRainbowToast("您还有未下载完的文件或版本，确定要关闭吗？",
                onConfirm: () => TrayService.ExitApp(),
                confirmText: "确定关闭",
                redTheme: true);
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Services.SettingsStore.SettingsChanged -= OnSettingsChanged;
        Services.DownloadManager.Completed -= OnDownloadCompleted;
        Services.DownloadManager.Progress -= OnDownloadProgress;
        Services.DownloadManager.QueueChanged -= OnDownloadQueueChanged;
        StopBackgroundTimer();
        TrayService.Dispose();
        base.OnClosed(e);
    }

    /// <summary>最大化 / 还原切换（标题栏双击触发）</summary>
    private void MaximizeToggle_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (SkinLibraryHost.Visibility == Visibility.Visible) { HideSkinLibrary(); e.Handled = true; return; }
            if (VersionManagerHost.Visibility == Visibility.Visible) { HideVersionManager(); e.Handled = true; return; }
            if (WindowState == WindowState.Maximized) { WindowState = WindowState.Normal; e.Handled = true; }
        }
    }

    // ===================== 拖放导入（仿 PCL2） =====================
    // 使用 Preview 隧道事件：在事件到达子元素前于窗口根部拦截，保证整个窗口任意位置都能拖放。

    /// <summary>拖拽悬停：仅当拖入的是文件时允许复制。</summary>
    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>松手释放：自动识别并导入 zip / mrpack / jar / litemod / json。</summary>
    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        e.Handled = true;

        foreach (var file in files)
        {
            var path = file;
            var name = Path.GetFileName(path);
            try
            {
                // 立即反馈，避免用户以为没有反应
                ShowToast($"正在导入：{name}…", ToastType.Warning);

                // 后台线程执行解压/复制/下载，避免拖入大文件时界面卡死
                var msg = await Task.Run(() => ImportService.ImportAsync(path));
                ShowToast(msg, ToastType.Success);
            }
            catch (Exception ex)
            {
                ShowToast(ex.Message, ToastType.Error);
            }
        }

        // 导入完成/失败后统一刷新主页与版本管理，让新版本立即可见
        _home.Refresh();
        VersionManager.Refresh();
    }

    // ===================== 最大化边界修正 =====================
    // WindowStyle=None + WindowChrome 时，系统给出的最大化尺寸会把窗口边框算进去，
    // 导致窗口比屏幕大一圈（标题栏和边缘被切掉、盖住任务栏）。这里按显示器工作区裁剪。
    // 注意：handled=true 会跳过 WPF 默认的最小尺寸处理，所以这里要自己填 ptMinTrackSize。
    private static int _minTrackX;
    private static int _minTrackY;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 此时 HWND 已创建，毛玻璃等个性化外观才能可靠生效
        ApplyPersonalization();

        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        var dpi = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        _minTrackX = (int)(MinWidth * dpi);
        _minTrackY = (int)(MinHeight * dpi);
        source?.AddHook(WindowProc);
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MAXIMIZE = 0xF030;
    private const int MonitorDefaultToNearest = 0x00000002;
    private const int SW_RESTORE = 9;
    /// <summary>第二实例发送的激活消息（重复双击启动器时触发彩虹光效+置顶）。</summary>
    internal const int WM_CUB_ACTIVATE = 0x9001;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 第二实例重复启动 → 触发彩虹光效 + 置顶
        if (msg == WM_CUB_ACTIVATE)
        {
            handled = true;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (Application.Current.MainWindow is MainWindow mw)
                    mw.BringToFront();
            });
            return IntPtr.Zero;
        }

        // 拖动标题栏期间（_blockSnapMaximize=true），拦截系统 Aero Snap 的最大化命令，
        // 实现"拖到屏幕顶部不自动全屏"，但保留 DragMove 的正常系统拖拽体验。
        if (msg == WM_SYSCOMMAND && _blockSnapMaximize && (int)wParam == SC_MAXIMIZE)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_GETMINMAXINFO)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    var work = info.rcWork;
                    var monitorRect = info.rcMonitor;

                    mmi.ptMaxPosition.X = work.Left - monitorRect.Left;
                    mmi.ptMaxPosition.Y = work.Top - monitorRect.Top;
                    mmi.ptMaxSize.X = work.Right - work.Left;
                    mmi.ptMaxSize.Y = work.Bottom - work.Top;
                    // 预留系统边框，否则最大化后内容会被裁掉几个像素
                    mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X + 16;
                    mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y + 16;
                    // 最小尺寸限制：防止窗口被缩小到比 MinWidth/MinHeight 还小
                    mmi.ptMinTrackSize.X = Math.Max(mmi.ptMinTrackSize.X, _minTrackX);
                    mmi.ptMinTrackSize.Y = Math.Max(mmi.ptMinTrackSize.Y, _minTrackY);

                    Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    // ===================== DWM 毛玻璃 / 亚克力背景（glass 主题） =====================

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>启用/关闭 DWM 亚克力毛玻璃。tint 为背景色调，alpha 控制色调强度。</summary>
    private void SetAcrylic(bool enable, Color tint, int alpha)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var accent = new AccentPolicy
        {
            AccentState = enable ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_DISABLED,
            AccentFlags = 2,
            GradientColor = enable ? (alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R : 0,
            AnimationId = 0,
        };
        var data = new WindowCompositionAttributeData
        {
            Attribute = WCA_ACCENT_POLICY,
            Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
            SizeOfData = Marshal.SizeOf<AccentPolicy>(),
        };
        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
