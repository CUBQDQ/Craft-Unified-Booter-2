// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter.Pages;

public partial class AccountPage : UserControl
{
    public AccountPage()
    {
        InitializeComponent();
        Loaded += AccountPage_Loaded;
        AccountsStore.Changed += OnAccountsChanged;
    }

    private void ShowToast(string message, ToastType type = ToastType.Success)
    {
        if (Window.GetWindow(this) is MainWindow mw) mw.ShowToast(message, type);
    }

    private void AccountPage_Loaded(object sender, RoutedEventArgs e) => RenderAccounts();

    private void OnAccountsChanged() => Application.Current.Dispatcher.Invoke(RenderAccounts);

    private void RenderAccounts()
    {
        AccountsListPanel.Children.Clear();
        var accounts = AccountsStore.Accounts;
        var selectedUuid = AccountsStore.GetSelected()?.Uuid;

        if (accounts.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "暂无账户，点击右上角「离线账户」添加。",
                FontSize = 13,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 8, 0, 0),
            };
            AccountsListPanel.Children.Add(hint);
            return;
        }

        var offlineOrdinal = 0;
        foreach (var acc in accounts)
        {
            var isSel = acc.Uuid == selectedUuid;
            var isOffline = acc.Type != "microsoft";
            if (isOffline) offlineOrdinal++;
            var initial = !string.IsNullOrEmpty(acc.Name) ? acc.Name[0].ToString().ToUpperInvariant() : "?";
            var accent = UiTheme.AccentColors(SettingsStore.Current.Accent);
            var initialColor = accent.Main;
            var initialBg = accent.Soft;

            var border = new Border
            {
                Background = isSel ? UiTheme.AccentSoftBrush : UiTheme.CardBackground,
                BorderBrush = isSel
                    ? UiTheme.AccentLightBrush
                    : new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(1),
                CornerRadius = UiTheme.Radius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Avatar：按 PCL2 呈现方法裁出脸层 + 第二层帽子，两层叠加显示（帽子层比脸层大 7/6）
            var avatarBd = new Border
            {
                Width = 48, Height = 48,
            };
            var skinPath = isOffline
                ? SkinService.EnsureDefaultSkin(offlineOrdinal % 2 == 0)
                : acc.LocalSkin;
            var avatar = AvatarService.CreateSkinAvatarBits(skinPath, 36);
            if (avatar != null)
            {
                var avatarGrid = new Grid();
                var faceImg = new Image
                {
                    Source = avatar.Face,
                    Width = 36, Height = 36,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                RenderOptions.SetBitmapScalingMode(faceImg, BitmapScalingMode.NearestNeighbor);
                avatarGrid.Children.Add(faceImg);
                if (avatar.Hat != null)
                {
                    var hatImg = new Image
                    {
                        Source = avatar.Hat,
                        Width = 42, Height = 42,
                        Stretch = Stretch.Fill,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    RenderOptions.SetBitmapScalingMode(hatImg, BitmapScalingMode.NearestNeighbor);
                    avatarGrid.Children.Add(hatImg);
                }
                avatarBd.Child = avatarGrid;
            }
            else
            {
                avatarBd.Background = new SolidColorBrush(initialBg);
                avatarBd.Child = new TextBlock
                {
                    Text = initial, FontSize = 20, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(initialColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            Grid.SetColumn(avatarBd, 0);
            grid.Children.Add(avatarBd);

            // Info
            var infoPanel = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var nameBlock = new TextBlock
            {
                Text = acc.Name, FontSize = 14, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            };
            var typeBlock = new TextBlock
            {
                Text = acc.Type == "microsoft" ? "微软账户" : "离线账户",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Margin = new Thickness(0, 2, 0, 0),
            };
            infoPanel.Children.Add(nameBlock);
            infoPanel.Children.Add(typeBlock);
            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // Buttons
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var selBtn = new Button
            {
                Content = isSel ? "已选中" : "选择",
                Style = isSel
                    ? (Style)FindResource("BlueSolidButtonStyle")
                    : (Style)FindResource("GraySmallButtonStyle"),
                Height = 32, Padding = new Thickness(14, 0, 14, 0),
                Tag = acc.Uuid,
            };
            selBtn.Click += SelectAccount_Click;
            btnPanel.Children.Add(selBtn);

            var delBtn = new Button
            {
                Style = (Style)FindResource("TrashIconButtonStyle"),
                Width = 32, Height = 32, Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "删除账户",
                Tag = acc.Uuid,
                Content = CreateTrashIcon(),
            };
            delBtn.Click += DeleteAccount_Click;
            btnPanel.Children.Add(delBtn);

            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            border.Child = grid;
            AccountsListPanel.Children.Add(border);
        }
    }

    private void SelectAccount_Click(object sender, RoutedEventArgs e)
    {
        var uuid = (string)((Button)sender).Tag;
        AccountsStore.Select(uuid);
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        var uuid = (string)((Button)sender).Tag;
        var acc = AccountsStore.Accounts.FirstOrDefault(a => a.Uuid == uuid);
        if (acc == null) return;
        MessageBoxResult result = MessageBox.Show(
            $"确定要删除账户 \"{acc.Name}\" 吗？",
            "删除账户",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            AccountsStore.Remove(uuid);
    }

    // ===== 微软登录（设备码流程） =====
    private CancellationTokenSource? _msCts;
    private DeviceCodeInfo? _msInfo;

    private async void MicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_msCts != null) return;
        _msCts = new CancellationTokenSource();
        var cts = _msCts;
        _msInfo = null;

        MsStatusText.Text = "正在向微软申请设备码…";
        MsCodeBox.Visibility = Visibility.Collapsed;
        MsLinkText.Visibility = Visibility.Collapsed;
        MsOpenBtn.Visibility = Visibility.Collapsed;
        MsCopyBtn.Visibility = Visibility.Collapsed;
        MsOverlay.Visibility = Visibility.Visible;

        MicrosoftAuthService.Progress += OnMicrosoftProgress;
        try
        {
            var account = await MicrosoftAuthService.LoginAsync(info =>
            {
                _msInfo = info;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MsCodeText.Text = info.UserCode;
                    MsCodeBox.Visibility = Visibility.Visible;
                    MsLinkText.Text = $"请在浏览器打开 {info.VerificationUri} 并输入上面的代码。";
                    MsLinkText.Visibility = Visibility.Visible;
                    MsOpenBtn.Visibility = Visibility.Visible;
                    MsCopyBtn.Visibility = Visibility.Visible;
                    MsStatusText.Text = "代码已复制到剪贴板，请在浏览器中完成登录…";
                    try { Clipboard.SetText(info.UserCode); } catch { /* 剪贴板被占用 */ }
                });
            }, cts.Token);

            AccountsStore.AddMicrosoft(account);
            MsOverlay.Visibility = Visibility.Collapsed;
            ShowToast($"登录成功：{account.Name}", ToastType.Success);
        }
        catch (OperationCanceledException)
        {
            MsOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MsCodeBox.Visibility = Visibility.Collapsed;
            MsOpenBtn.Visibility = Visibility.Collapsed;
            MsCopyBtn.Visibility = Visibility.Collapsed;
            MsStatusText.Text = "登录失败：" + ex.Message;
        }
        finally
        {
            MicrosoftAuthService.Progress -= OnMicrosoftProgress;
            _msCts = null;
            cts.Dispose();
        }
    }

    private void OnMicrosoftProgress(string message)
        => Application.Current.Dispatcher.Invoke(() => MsStatusText.Text = message);

    private void MsOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        var url = _msInfo?.VerificationUri;
        if (string.IsNullOrWhiteSpace(url)) url = "https://microsoft.com/link";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* 打开浏览器失败时忽略 */ }
    }

    private void MsCopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_msInfo?.UserCode)) return;
        try { Clipboard.SetText(_msInfo!.UserCode); } catch { /* 忽略 */ }
    }

    private void MsCancel_Click(object sender, RoutedEventArgs e)
    {
        _msCts?.Cancel();
        MsOverlay.Visibility = Visibility.Collapsed;
    }

    // ===== 添加离线账户弹窗 =====
    private void OpenAddDialog(object sender, RoutedEventArgs e)
        => AddOverlay.Visibility = Visibility.Visible;

    private void CloseAddDialog(object sender, RoutedEventArgs e)
        => AddOverlay.Visibility = Visibility.Collapsed;

    private void ConfirmAdd(object sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
            AccountsStore.AddOffline(name);
        NewNameBox.Text = string.Empty;
        AddOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>创建垃圾图标 Path</summary>
    private static System.Windows.Shapes.Path CreateTrashIcon()
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3 6H21 M19 6V20A2 2 0 0 1 17 22H7A2 2 0 0 1 5 20V6 M8 6V4A2 2 0 0 1 10 2H14A2 2 0 0 1 16 4V6 M10 11V17 M14 11V17"),
            Width = 16, Height = 16,
            Stretch = Stretch.Uniform,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        return path;
    }
}
