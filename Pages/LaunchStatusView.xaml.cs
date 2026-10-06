// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CraftUnifiedBooter.Pages;

public partial class LaunchStatusView : UserControl
{
    private Storyboard? _spinStoryboard;
    private DispatcherTimer? _autoCollapseTimer;

    public LaunchStatusView()
    {
        InitializeComponent();
    }

    public void Clear()
    {
        StatusPanel.Visibility = Visibility.Collapsed;
        LogBox.Text = "";
        ErrorBox.Visibility = Visibility.Collapsed;
    }

    public void ShowLaunching(string account, string version)
    {
        // 再次启动时取消上一次的自动收起计时器，面板保持可见
        if (_autoCollapseTimer != null)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer = null;
        }
        StatusPanel.BeginAnimation(OpacityProperty, null);
        StatusPanel.Opacity = 1;

        StatusPanel.Visibility = Visibility.Visible;
        // Q 弹入场：面板整体缩放 + 上移回弹
        StatusPanel.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform(0, 14));
        group.Children.Add(new ScaleTransform(0.96, 0.96));
        StatusPanel.RenderTransform = group;
        var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        ((TranslateTransform)group.Children[0]).BeginAnimation(TranslateTransform.YProperty, slide);
        var sx = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        var sy = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = UiAnimation.QElastic(8),
        };
        ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleYProperty, sy);

        ErrorBox.Visibility = Visibility.Collapsed;
        LogBox.Text = "";
        AccountText.Text = $"账户: {account}";
        VersionText.Text = $"版本: {version}";
        StageText.Text = "正在准备…";
        StageText.Foreground = (Brush)FindResource("TextDefault");
        SetIconPath("IconLoader", (Brush)FindResource("IconBlue"));
        StartSpin(StageStackPanel);
        ProgressBorder.Width = 0;
        ProgressBorder.Background = (Brush)FindResource("BarBlue");
        StagePercentText.Text = "0%";
    }

    public void UpdateProgress(string stageMsg, int overallPercent)
    {
        // 阶段文字变化时淡入，避免生硬跳变
        if (StageText.Text != stageMsg)
        {
            StageText.Text = stageMsg;
            StageText.BeginAnimation(OpacityProperty, null);
            StageText.Opacity = 0;
            StageText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        StagePercentText.Text = $"{Math.Clamp(overallPercent, 0, 100)}%";

        var w = Math.Max(0.0, StatusPanel.ActualWidth - 64);
        if (w <= 0) w = 400.0; // 面板尚未完成布局时使用兜底宽度，避免进度条不可见
        var target = w * Math.Clamp(overallPercent, 0, 100) / 100.0;
        // 平滑过渡到新进度（避免生硬跳变）
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        ProgressBorder.BeginAnimation(WidthProperty, anim);
    }

    public void ShowJavaPrep(string javaInfo)
    {
        // 保持加载圈旋转，只更新文案（不再切换成 CPU 图标，避免旋转的 CPU 图标看起来很怪）
        StageText.Text = $"准备 Java: {javaInfo}";
    }

    public void ShowSuccess(string gameOutput)
    {
        StopSpin(StageStackPanel);
        SetIconPath("IconCheckCircle", (Brush)FindResource("IconGreen"));
        StageText.Text = "启动成功";
        StageText.Foreground = (Brush)FindResource("TextGreen");
        ProgressBorder.Background = (Brush)FindResource("BarSuccess");
        StagePercentText.Text = "100%";
        if (!string.IsNullOrEmpty(gameOutput))
            AppendLog(gameOutput);

        // 启动成功：2.5 秒后自动收起面板（淡出隐藏）
        _autoCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _autoCollapseTimer.Tick += (_, _) =>
        {
            _autoCollapseTimer!.Stop();
            _autoCollapseTimer = null;
            var fade = new DoubleAnimation(StatusPanel.Opacity, 0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) => StatusPanel.Visibility = Visibility.Collapsed;
            StatusPanel.BeginAnimation(OpacityProperty, fade);
        };
        _autoCollapseTimer.Start();
    }

    public void ShowFailure(string errorMessage)
    {
        StopSpin(StageStackPanel);
        SetIconPath("IconXCircle", (Brush)FindResource("IconRed"));
        StageText.Text = "启动失败";
        StageText.Foreground = (Brush)FindResource("TextRed");
        ProgressBorder.Background = (Brush)FindResource("BarError");
        ErrorBox.Visibility = Visibility.Visible;
        ErrorText.Text = errorMessage;
        AppendLog(errorMessage);
    }

    /// <summary>切换状态图标，并带一个轻微的弹性缩放弹出动画。</summary>
    private void SetIconPath(string key, Brush stroke)
    {
        if (FindResource(key) is Geometry geom && StageIcon != null)
        {
            StageIcon.Data = geom;
            StageIcon.Stroke = stroke;

            // 图标切换时的 Q 弹弹出动画
            StageIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(0.6, 0.6);
            StageIcon.RenderTransform = scale;
            var sb = new Storyboard();
            var sx = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            Storyboard.SetTarget(sx, scale);
            Storyboard.SetTargetProperty(sx, new PropertyPath(ScaleTransform.ScaleXProperty));
            var sy = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            Storyboard.SetTarget(sy, scale);
            Storyboard.SetTargetProperty(sy, new PropertyPath(ScaleTransform.ScaleYProperty));
            sb.Children.Add(sx);
            sb.Children.Add(sy);
            sb.Begin(StageIcon);
        }
    }

    /// <summary>启动加载动画：900ms 匀速旋转一圈。</summary>
    private void StartSpin(StackPanel panel)
    {
        StopSpin(panel); // 先停止旧动画，避免重复旋转导致动画冲突
        panel.RenderTransformOrigin = new Point(0.5, 0.5);
        var rt = new RotateTransform();
        panel.RenderTransform = rt;
        // 注意：以前误用了 TimeSpan.FromTicks(700)（约 0.07ms 一圈），导致加载图标高速旋转；
        // 现在改为 900ms 一圈的匀速旋转，看起来才是正常的"转圈加载"。
        var anim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900));
        anim.RepeatBehavior = RepeatBehavior.Forever;
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, rt);
        Storyboard.SetTargetProperty(anim, new PropertyPath(RotateTransform.AngleProperty));
        sb.Children.Add(anim);
        _spinStoryboard = sb;
        sb.Begin(panel);
    }

    private void StopSpin(StackPanel panel)
    {
        if (_spinStoryboard != null)
        {
            _spinStoryboard.Stop(panel);
            _spinStoryboard = null;
        }
        panel.RenderTransform = null;
    }

    private void AppendLog(string text)
    {
        var current = LogBox.Text;
        LogBox.Text = current + (current.Length > 0 ? "\n" : "") + text;
        LogScroll?.ScrollToBottom();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        StatusPanel.Visibility = Visibility.Collapsed;
    }
}