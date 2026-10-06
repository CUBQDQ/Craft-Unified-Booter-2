// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Windows;
using System.Windows.Interop;

namespace CraftUnifiedBooter.Services;

/// <summary>系统托盘：支持最小化到托盘、下载完成气泡通知、托盘菜单退出</summary>
public static class TrayService
{
    private static System.Windows.Forms.NotifyIcon? _icon;
    private static Window? _owner;

    /// <summary>是否正在真正退出（托盘菜单"退出"触发，避免被最小化逻辑拦截）</summary>
    public static bool IsExiting { get; private set; }

    /// <summary>初始化托盘图标（应用启动后调用一次）</summary>
    public static void Initialize(Window owner)
    {
        if (_icon != null) return;
        _owner = owner;

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Craft Unified Booter",
            Visible = false,
        };
        _icon.Icon = LoadIcon();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowMainWindow();
    }

    /// <summary>从嵌入资源加载托盘图标（单文件发布下 Assembly.Location 为空，改用 WPF 资源流）。</summary>
    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
            if (info?.Stream != null)
            {
                using var stream = info.Stream;
                return new System.Drawing.Icon(stream);
            }
        }
        catch { /* 图标缺失时托盘不显示图标 */ }
        return null;
    }

    /// <summary>显示并激活主窗口</summary>
    public static void ShowMainWindow()
    {
        if (_owner == null) return;
        _owner.Show();
        if (_owner.WindowState == WindowState.Minimized)
            _owner.WindowState = WindowState.Normal;
        _owner.Activate();
    }

    /// <summary>显示托盘气泡通知</summary>
    public static void ShowBalloon(string title, string text)
    {
        if (_icon == null) return;
        _icon.Visible = true;
        _icon.ShowBalloonTip(3000, title, text, System.Windows.Forms.ToolTipIcon.Info);
    }

    /// <summary>从托盘退出应用</summary>
    public static void ExitApp()
    {
        IsExiting = true;
        Dispose();
        Application.Current.Shutdown();
    }

    /// <summary>释放托盘资源</summary>
    public static void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}