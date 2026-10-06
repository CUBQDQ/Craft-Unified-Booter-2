// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CraftUnifiedBooter;

/// <summary>
/// 全局 Q 弹动画辅助：提供果冻回弹（ElasticEase）与轻微回弹（BackEase）的缓动函数，
/// 供窗口、页面切换、弹窗、启动界面等统一使用，营造“Q 弹、有生命力”的动画手感。
/// </summary>
public static class UiAnimation
{
    /// <summary>Q 弹缓动（果冻振荡一次，弹性较高，视觉 Q 弹但不拖沓）。</summary>
    public static ElasticEase QElastic(double springiness = 8, int oscillations = 1) => new()
    {
        Oscillations = oscillations,
        Springiness = springiness,
        EasingMode = EasingMode.EaseOut,
    };

    /// <summary>轻微回弹（按钮 / 小元素 hover 用，过冲后回位）。</summary>
    public static BackEase QBack(double amplitude = 0.35) => new()
    {
        Amplitude = amplitude,
        EasingMode = EasingMode.EaseOut,
    };

    /// <summary>对任意可动画依赖属性执行一次 Q 弹动画。</summary>
    public static void Animate(Animatable target, DependencyProperty dp, double from, double to, double ms, bool elastic = true)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = elastic ? QElastic() : QBack(),
        };
        target.BeginAnimation(dp, anim);
    }

    /// <summary>对 Transform 的 TranslateTransform.Y 执行 Q 弹位移动画。</summary>
    public static void SlideY(UIElement element, double from, double to, double ms, bool elastic = true)
    {
        var transform = element.RenderTransform as TranslateTransform;
        if (transform == null)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = elastic ? QElastic() : QBack(),
        };
        transform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    /// <summary>对 Transform 的 ScaleTransform 执行 Q 弹缩放动画。</summary>
    public static void Scale(UIElement element, double fromX, double fromY, double toX, double toY, double ms, bool elastic = true)
    {
        var transform = element.RenderTransform as ScaleTransform;
        if (transform == null)
        {
            transform = new ScaleTransform(1, 1);
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        var animX = new DoubleAnimation(fromX, toX, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = elastic ? QElastic() : QBack(),
        };
        var animY = new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = elastic ? QElastic() : QBack(),
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }
}