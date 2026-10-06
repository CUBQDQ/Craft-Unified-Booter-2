// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CraftUnifiedBooter.Services;
using Microsoft.Win32;

namespace CraftUnifiedBooter.Pages;

/// <summary>皮肤库：左侧选皮肤、右侧选披风、中间 3D 旋转预览；选择结果保存到本地并回写账户</summary>
public partial class SkinLibraryPage : UserControl
{
    /// <summary>请求退出皮肤库（返回主页）</summary>
    public event Action? ExitRequested;

    private void ShowToast(string message, ToastType type = ToastType.Success)
    {
        if (Window.GetWindow(this) is MainWindow mw) mw.ShowToast(message, type);
    }

    // 3D 相机参数（左右旋转 + 上下俯仰，不倾斜）
    private double _yaw;
    private double _pitch;
    private double _dist = 135;
    private Point _dragStart;
    private bool _dragging;

    // 3D 皮肤动画：默认 / 走动 / 跑动（参照 Minecraft 原版动作）
    private SkinService.AnimatedSkin? _skinModel;
    private SkinService.SkinAnimation _animation;
    private double _animTime;
    /// <summary>当前皮肤分类：classic=官方默认 / personal=正版账号+自己添加</summary>
    private string _skinTab = "classic";
    private readonly System.Windows.Threading.DispatcherTimer _animTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(33),
    };

    public SkinLibraryPage()
    {
        InitializeComponent();
        SetSkinTab("classic", false);   // 默认显示"经典"（官方默认皮肤）
        Loaded += (_, _) => Refresh();

        // 动画驱动：每 33ms 推进一次动作（约 30fps）
        _animTimer.Tick += (_, _) =>
        {
            _animTime += 0.033;
            _skinModel?.ApplyAnimation(_animation, _animTime);
        };
        _animTimer.Start();
    }

    /// <summary>重新载入账户、同步资源与列表，并刷新 3D 预览</summary>
    public void Refresh()
    {
        var account = AccountsStore.GetSelected();
        AccountPillText.Text = account?.Name ?? "未选择账户";

        // 微软账户的正版皮肤/披风登记为预设，避免被切换后找不到（披风全部登记）
        if (account?.Type == "microsoft")
        {
            if (!string.IsNullOrEmpty(account.LocalSkin))
                SkinStore.RegisterSyncedSkin(account.LocalSkin, account.SkinVariant ?? "classic");
            foreach (var cape in account.LocalCapes)
                SkinStore.RegisterSyncedCape(cape);
            if (!string.IsNullOrEmpty(account.LocalCape))
                SkinStore.RegisterSyncedCape(account.LocalCape);
        }

        RenderSkins(account);
        RenderCapes(account);
        RebuildModel(account);
    }

    // ===================== 皮肤列表 =====================
    private void RenderSkins(Account? account)
    {
        SkinListPanel.Children.Clear();
        var current = account?.LocalSkin;

        foreach (var preset in SkinStore.Skins)
        {
            // 经典=官方默认皮肤；个人=正版账号皮肤 + 玩家自己添加的
            if (_skinTab == "classic" && !preset.Builtin) continue;
            if (_skinTab == "personal" && preset.Builtin) continue;

            var isUsed = !string.IsNullOrEmpty(current) && current == preset.Path;
            SkinListPanel.Children.Add(BuildSkinCard(preset, isUsed, account));
        }
    }

    // ===================== 经典 / 个人 分类切换 =====================

    private void SkinTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string tab && tab != _skinTab)
            SetSkinTab(tab, true);
    }

    private void SetSkinTab(string tab, bool rerender)
    {
        _skinTab = tab;
        var isClassic = tab == "classic";
        ClassicTab.Background = isClassic ? UiTheme.AccentSoftBrush : MakeBrush("#F3F4F6");
        ClassicTabText.Foreground = isClassic ? UiTheme.AccentDarkBrush : MakeBrush("#4B5563");
        PersonalTab.Background = !isClassic ? UiTheme.AccentSoftBrush : MakeBrush("#F3F4F6");
        PersonalTabText.Foreground = !isClassic ? UiTheme.AccentDarkBrush : MakeBrush("#4B5563");
        // 经典是官方内置皮肤，不需要"添加"；个人分类才有添加按钮
        AddSkinBtn.Visibility = isClassic ? Visibility.Collapsed : Visibility.Visible;
        if (rerender) RenderSkins(AccountsStore.GetSelected());
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private Border BuildSkinCard(SkinPreset preset, bool isUsed, Account? account)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(isUsed
                ? Color.FromRgb(0xEF, 0xF6, 0xFF)
                : Color.FromRgb(0xF9, 0xFA, 0xFB)),
            BorderBrush = new SolidColorBrush(isUsed
                ? Color.FromRgb(0xBF, 0xDB, 0xFE)
                : Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius = UiTheme.Radius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand,
            Tag = preset,
        };
        border.MouseLeftButtonUp += SkinCard_Click;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 缩略图：按 PCL2 呈现方法裁出脸层 + 第二层帽子，两层叠加（NearestNeighbor 硬边）
        var avatar = AvatarService.CreateSkinAvatarBits(preset.Path, 30)
                     ?? AvatarService.CreateOfflineAvatarBits(SkinService.IsSlim(preset.Variant) ? 1 : 0, 30);
        var thumb = new Border
        {
            Width = 40, Height = 40,
        };
        if (avatar != null)
        {
            var thumbGrid = new Grid();
            var faceImg = new Image
            {
                Source = avatar.Face,
                Width = 30, Height = 30,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(faceImg, BitmapScalingMode.NearestNeighbor);
            thumbGrid.Children.Add(faceImg);
            if (avatar.Hat != null)
            {
                var hatImg = new Image
                {
                    Source = avatar.Hat,
                    Width = 35, Height = 35,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                RenderOptions.SetBitmapScalingMode(hatImg, BitmapScalingMode.NearestNeighbor);
                thumbGrid.Children.Add(hatImg);
            }
            thumb.Child = thumbGrid;
        }
        RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.NearestNeighbor);
        Grid.SetColumn(thumb, 0);
        grid.Children.Add(thumb);

        var info = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = preset.Name, FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 110,
        });
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        sub.Children.Add(new TextBlock
        {
            Text = SkinService.IsSlim(preset.Variant) ? "纤细" : "经典",
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        });
        if (preset.Synced)
            sub.Children.Add(new TextBlock
            {
                Text = "· 微软同步", FontSize = 11,
                Foreground = UiTheme.AccentBrush,
            });
        if (isUsed)
            sub.Children.Add(new TextBlock
            {
                Text = "· 使用中", FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
            });
        info.Children.Add(sub);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var variantBtn = new Button
        {
            Style = (Style)FindResource("GrayButtonStyle"),
            Height = 26, Padding = new Thickness(8, 0, 8, 0),
            Content = new TextBlock { Text = "切换体型", FontSize = 11 },
            ToolTip = "在「经典」与「纤细」之间切换",
            Tag = preset,
        };
        variantBtn.Click += ToggleVariant_Click;
        actions.Children.Add(variantBtn);

        if (!preset.Builtin)
        {
            var delBtn = new Button
            {
                Style = (Style)FindResource("IconButtonStyle"),
                Width = 26, Height = 26, Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "删除此皮肤",
                Tag = preset,
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
            delBtn.Click += DeleteSkin_Click;
            actions.Children.Add(delBtn);
        }

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        border.Child = grid;
        return border;
    }

    private async void SkinCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: SkinPreset preset }) return;
        var account = AccountsStore.GetSelected();
        if (account == null) return;
        account.LocalSkin = preset.Path;
        account.SkinVariant = preset.Variant;
        AccountsStore.Save();
        Refresh();
        // 选择皮肤后自动静默同步到微软账户
        _ = SyncSkinToMicrosoftAsync(account);
    }

    /// <summary>静默同步皮肤到微软账户（失败时静默，不打扰用户）</summary>
    private static async Task SyncSkinToMicrosoftAsync(Account account)
    {
        if (account.Type != "microsoft" || string.IsNullOrEmpty(account.LocalSkin)) return;
        try
        {
            await MicrosoftAuthService.EnsureValidAsync(account);
            await MicrosoftAuthService.UploadSkinAsync(account, account.LocalSkin,
                SkinService.IsSlim(account.SkinVariant) ? "slim" : "classic");
            AccountsStore.Save();
        }
        catch { /* 静默失败 */ }
    }

    private void ToggleVariant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SkinPreset preset }) return;
        preset.Variant = SkinService.IsSlim(preset.Variant) ? "classic" : "slim";
        SkinStore.Save();

        // 正在使用的皮肤同步更新账户体型
        var account = AccountsStore.GetSelected();
        if (account != null && account.LocalSkin == preset.Path)
        {
            account.SkinVariant = preset.Variant;
            AccountsStore.Save();
        }
        Refresh();
    }

    private void DeleteSkin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SkinPreset preset }) return;
        var account = AccountsStore.GetSelected();
        var inUse = account != null && account.LocalSkin == preset.Path;
        if (inUse)
        {
            ShowToast("该皮肤正在使用中，请先切换到其他皮肤再删除。", ToastType.Warning);
            return;
        }
        if (MessageBox.Show($"确定删除皮肤「{preset.Name}」吗？", "皮肤库",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SkinStore.RemoveSkin(preset);
        Refresh();
    }

    private void AddSkin_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择皮肤文件",
            Filter = "PNG 皮肤 (*.png)|*.png",
        };
        if (dlg.ShowDialog() != true) return;

        var preset = SkinStore.ImportSkin(dlg.FileName, null, "classic");
        ShowToast($"已添加皮肤「{preset.Name}」");
        Refresh();
    }

    // ===================== 披风列表 =====================
    private void RenderCapes(Account? account)
    {
        CapeListPanel.Children.Clear();
        var current = account?.LocalCape;

        // 不显示披风
        CapeListPanel.Children.Add(BuildCapeCard(
            new CapePreset { Name = "不显示披风", Path = "" }, string.IsNullOrEmpty(current)));

        foreach (var preset in SkinStore.Capes)
            CapeListPanel.Children.Add(BuildCapeCard(preset, !string.IsNullOrEmpty(current) && current == preset.Path));
    }

    private Border BuildCapeCard(CapePreset preset, bool isUsed)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(isUsed
                ? Color.FromRgb(0xF5, 0xF3, 0xFF)
                : Color.FromRgb(0xF9, 0xFA, 0xFB)),
            BorderBrush = new SolidColorBrush(isUsed
                ? Color.FromRgb(0xDD, 0xD6, 0xFE)
                : Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius = UiTheme.Radius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand,
            Tag = preset,
        };
        border.MouseLeftButtonUp += CapeCard_Click;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumbBorder = new Border
        {
            Width = 30, Height = 42, CornerRadius = UiTheme.Radius(6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        };
        var thumb = string.IsNullOrEmpty(preset.Path) ? null : SkinService.RenderCapeThumb(preset.Path);
        if (thumb != null)
        {
            thumbBorder.Background = new ImageBrush(thumb) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(thumbBorder, BitmapScalingMode.NearestNeighbor);
        }
        else
        {
            thumbBorder.Child = new TextBlock
            {
                Text = "无", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        Grid.SetColumn(thumbBorder, 0);
        grid.Children.Add(thumbBorder);

        var info = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = preset.Name, FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 96,
        });
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        if (preset.Synced)
            sub.Children.Add(new TextBlock
            {
                Text = "微软同步", FontSize = 11,
                Foreground = UiTheme.AccentBrush,
            });
        if (isUsed)
            sub.Children.Add(new TextBlock
            {
                Text = preset.Synced ? " · 使用中" : "使用中", FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
            });
        info.Children.Add(sub);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        if (!preset.Builtin && !string.IsNullOrEmpty(preset.Path))
        {
            var delBtn = new Button
            {
                Style = (Style)FindResource("IconButtonStyle"),
                Width = 26, Height = 26,
                ToolTip = "删除此披风",
                Tag = preset,
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
            delBtn.Click += DeleteCape_Click;
            Grid.SetColumn(delBtn, 2);
            grid.Children.Add(delBtn);
        }

        border.Child = grid;
        return border;
    }

    private void CapeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: CapePreset preset }) return;
        var account = AccountsStore.GetSelected();
        if (account == null) return;
        account.LocalCape = string.IsNullOrEmpty(preset.Path) ? null : preset.Path;
        AccountsStore.Save();
        Refresh();
    }

    private void AddCape_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择披风文件",
            Filter = "PNG 披风 (*.png)|*.png",
        };
        if (dlg.ShowDialog() != true) return;

        var preset = SkinStore.ImportCape(dlg.FileName, null);
        ShowToast($"已添加披风「{preset.Name}」");
        Refresh();
    }

    private void DeleteCape_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CapePreset preset }) return;
        var account = AccountsStore.GetSelected();
        if (account != null && account.LocalCape == preset.Path)
        {
            ShowToast("该披风正在使用中，请先切换到其他披风再删除。", ToastType.Warning);
            return;
        }
        if (MessageBox.Show($"确定删除披风「{preset.Name}」吗？", "皮肤库",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SkinStore.RemoveCape(preset);
        Refresh();
    }

    // ===================== 3D 预览 =====================
    private void RebuildModel(Account? account)
    {
        var skinPath = account?.LocalSkin;
        var slim = SkinService.IsSlim(account?.SkinVariant);
        var capePath = account?.LocalCape;

        var model = SkinService.BuildAnimatedModel(skinPath, capePath, slim);
        if (model == null)
        {
            // 无皮肤时退回默认史蒂夫，保证始终有可预览的模型
            model = SkinService.BuildAnimatedModel(SkinService.EnsureDefaultSkin(true), capePath, false);
        }

        if (model == null)
        {
            _skinModel = null;
            PreviewModel.Content = null;
            PreviewEmptyText.Visibility = Visibility.Visible;
            return;
        }

        _skinModel = model;
        PreviewEmptyText.Visibility = Visibility.Collapsed;
        PreviewModel.Content = model.Group;
        ApplyCamera();
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

    private void ApplyCamera()
    {
        var group = new Transform3DGroup();
        group.Children.Add(new TranslateTransform3D(0, -16, 0));
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _yaw)));
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), _pitch)));
        PreviewModel.Transform = group;

        // 模型已平移到以原点为中心，相机同样对准原点
        PreviewCamera.Position = new Point3D(0, 0, _dist);
        PreviewCamera.LookDirection = new Vector3D(0, 0, -_dist);

        // 收紧近/远平面，避免覆盖层与基础层因深度精度不足产生 Z-fighting（画面发花）
        PreviewCamera.NearPlaneDistance = Math.Max(1, _dist - 24);
        PreviewCamera.FarPlaneDistance = _dist + 24;
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(PreviewHost);
        PreviewHost.CaptureMouse();
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        PreviewHost.ReleaseMouseCapture();
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(PreviewHost);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _dragStart = p;

        // 左右拖动 → 水平旋转；上下拖动 → 俯仰（限制范围，避免翻转/倾斜）
        _yaw += dx * 0.8;
        _pitch = Math.Clamp(_pitch + dy * 0.8, -60, 60);
        ApplyCamera();
    }

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 允许放大到接近全屏（dist 最小 28），最大拉远 260
        _dist = Math.Clamp(_dist - e.Delta * 0.12, 28, 260);
        ApplyCamera();
    }

    private void Preview_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _yaw = 0;
        _pitch = 0;
        _dist = 135;
        ApplyCamera();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}