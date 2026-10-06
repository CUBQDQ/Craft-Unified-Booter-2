// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter.Pages;

public partial class GamePage : UserControl
{
    private static readonly Brush B_FEF2F2 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
    private static readonly Brush B_FECACA = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
    private static readonly Brush B_DC2626 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
    private static readonly Brush B_E5E7EB = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
    private static readonly Brush B_9CA3AF = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
    private static readonly Brush B_111827 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
    private static readonly Brush B_6B7280 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
    private static readonly Brush B_DCFCE7 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
    private static readonly Brush B_FFEDD5 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEDD5"));
    private static readonly Brush B_EDE9FE = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDE9FE"));
    private static readonly Brush B_22C55E = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
    private static readonly Brush B_D1D5DB = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
    private static readonly Brush B_F3F4F6 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));
    private static readonly Brush B_15803D = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
    private static readonly Brush B_C2410C = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C2410C"));
    private static readonly Brush B_7C3AED = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED"));
    private static readonly Brush B_F5F3FF = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F3FF"));
    private static readonly Brush B_FFF7ED = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7ED"));
    private static readonly Brush B_ECFDF5 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
    private static readonly Brush B_F0F9FF = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F9FF"));
    private static readonly Brush B_C4B5FD = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C4B5FD"));
    private static readonly Brush B_FDBA74 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDBA74"));
    private static readonly Brush B_6EE7B7 = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6EE7B7"));
    private static readonly Brush B_7DD3FC = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7DD3FC"));

    private static Brush ColorBrush(string hex) => (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    // ===================== 状态 =====================
    private List<VersionEntry> _allVersions = new();
    private List<VersionEntry> _filteredVersions = new();
    private string _currentFilter = "all";
    private string _searchText = "";
    private bool _isLoading = false;

    // ===================== 弹窗状态 =====================
    private string? _modalVersionId;
    private string _selectedLoader = "vanilla";
    private bool _isInstalling = false;

    // ===================== 资源安装（选择目标版本） =====================
    private ModrinthHit? _pendingResourceHit;
    private ModrinthService.ResourceKind _pendingResourceKind = ModrinthService.ResourceKind.Mod;
    private Button? _pendingResourceBtn;

    // ===================== 加载器选中样式 =====================
    private readonly Border[] _loaderBorders = Array.Empty<Border>();

    // ===================== 资源（Modrinth）状态 =====================
    private bool _isResourceMode = false;
    private ModrinthService.ResourceKind _currentKind = ModrinthService.ResourceKind.Mod;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _resourceRequestSeq = 0;

    public GamePage()
    {
        InitializeComponent();
        _loaderBorders = [LoaderVanilla, LoaderFabric, LoaderForge, LoaderNeoForge, LoaderOptiFine];
        _searchDebounce.Tick += async (s, e) =>
        {
            _searchDebounce.Stop();
            if (_isResourceMode) await LoadResourcesAsync();
        };
        Loaded += async (s, e) => await LoadVersionsAsync();
    }

    // ===================== 模式切换 =====================

    private async void MoreResources_Click(object sender, RoutedEventArgs e)
    {
        EnterResourceMode();
        await LoadResourcesAsync();
    }

    private void BackVanilla_Click(object sender, RoutedEventArgs e) => EnterVanillaMode();

    private void EnterResourceMode()
    {
        _isResourceMode = true;
        MoreResourcesBtn.Visibility = Visibility.Collapsed;
        ResourceTypePanel.Visibility = Visibility.Visible;
        VanillaFilterPanel.Visibility = Visibility.Collapsed;
        BackVanillaBtn.Visibility = Visibility.Visible;
        VersionListScroll.Visibility = Visibility.Collapsed;
        ResourceListScroll.Visibility = Visibility.Visible;

        PageSubtitle.Text = "从 Modrinth 下载 Mod、整合包、数据包、资源包、光影包";
        SearchPlaceholder.Text = "搜索资源名称…";
        SearchBox.Text = "";
        _searchText = "";
        _searchDebounce.Stop();

        UpdateResourceTypeStyles();
    }

    private void EnterVanillaMode()
    {
        _isResourceMode = false;
        MoreResourcesBtn.Visibility = Visibility.Visible;
        ResourceTypePanel.Visibility = Visibility.Collapsed;
        VanillaFilterPanel.Visibility = Visibility.Visible;
        BackVanillaBtn.Visibility = Visibility.Collapsed;
        VersionListScroll.Visibility = Visibility.Visible;
        ResourceListScroll.Visibility = Visibility.Collapsed;

        PageSubtitle.Text = "下载并管理 Minecraft 游戏版本";
        SearchPlaceholder.Text = "搜索版本 ID…";
        SearchBox.Text = "";
        _searchText = "";
        _searchDebounce.Stop();

        ApplyFilterAndSearch();
    }

    private async void ResourceType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _currentKind = (btn.Tag as string) switch
        {
            "modpack" => ModrinthService.ResourceKind.Modpack,
            "datapack" => ModrinthService.ResourceKind.Datapack,
            "resourcepack" => ModrinthService.ResourceKind.Resourcepack,
            "shader" => ModrinthService.ResourceKind.Shader,
            _ => ModrinthService.ResourceKind.Mod,
        };
        UpdateResourceTypeStyles();
        await LoadResourcesAsync();
    }

    private void UpdateResourceTypeStyles()
    {
        var buttons = new (Button Btn, ModrinthService.ResourceKind Kind)[]
        {
            (ResMod, ModrinthService.ResourceKind.Mod),
            (ResModpack, ModrinthService.ResourceKind.Modpack),
            (ResDatapack, ModrinthService.ResourceKind.Datapack),
            (ResResourcepack, ModrinthService.ResourceKind.Resourcepack),
            (ResShader, ModrinthService.ResourceKind.Shader),
        };
        foreach (var (btn, kind) in buttons)
            btn.Style = (Style)FindResource(kind == _currentKind ? "ModeBtnActiveStyle" : "ModeBtnStyle");
    }

    // ===================== 资源列表 =====================

    private void ClearResourceCards()
    {
        var toRemove = ResourceListPanel.Children.OfType<Border>()
            .Where(b => b.Tag is not null && b.Tag.ToString() == "resource-card").ToList();
        foreach (var card in toRemove)
            ResourceListPanel.Children.Remove(card);
    }

    private async Task LoadResourcesAsync()
    {
        var seq = ++_resourceRequestSeq;
        ClearResourceCards();
        ResourceLoadingPlaceholder.Visibility = Visibility.Visible;
        ResourceEmptyPlaceholder.Visibility = Visibility.Collapsed;
        RefreshBtn.IsEnabled = false;

        // 个性化：下载页每页数量（在线搜索列表每页显示条数）
        var pageSize = SettingsStore.Current.PageSize;
        if (pageSize <= 0) pageSize = 20;
        var (hits, error) = await ModrinthService.SearchAsync(_searchText, _currentKind, pageSize, 0);

        if (seq != _resourceRequestSeq)
        {
            // 已有更新的请求，丢弃本次结果；但需要恢复刷新按钮，否则可能一直禁用
            RefreshBtn.IsEnabled = true;
            return;
        }
        RefreshBtn.IsEnabled = true;
        ResourceLoadingPlaceholder.Visibility = Visibility.Collapsed;

        if (error != null)
        {
            ShowResourceMessage($"加载失败：{error}", isError: true);
            return;
        }
        if (hits == null || hits.Count == 0)
        {
            ResourceEmptyPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        for (var i = 0; i < hits.Count; i++)
        {
            var card = BuildResourceCard(hits[i]);
            ResourceListPanel.Children.Add(card);
            if (i < 12) AnimateListCard(card); // 只动画前几项，避免大量卡片同时动画造成卡顿
        }
    }

    private void ShowResourceMessage(string text, bool isError)
    {
        var border = new Border
        {
            Background = isError ? B_FEF2F2 : UiTheme.CardBackground,
            BorderBrush = isError ? B_FECACA : B_E5E7EB,
            BorderThickness = new Thickness(1), CornerRadius = UiTheme.Radius(8),
            Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Tag = "resource-card",
        };
        border.Child = new TextBlock
        {
            Text = text, FontSize = 13,
            Foreground = isError ? B_DC2626 : B_9CA3AF,
            TextWrapping = TextWrapping.Wrap,
        };
        ResourceListPanel.Children.Add(border);
    }

    private Border BuildResourceCard(ModrinthHit hit)
    {
        var card = new Border
        {
            Background = UiTheme.CardBackground, BorderBrush = B_E5E7EB,
            BorderThickness = new Thickness(1), CornerRadius = UiTheme.Radius(8),
            Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Tag = "resource-card",
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 图标
        var iconBox = new Border
        {
            Width = 48, Height = 48, CornerRadius = UiTheme.Radius(10),
            Background = B_F3F4F6, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0), ClipToBounds = true,
        };
        if (!string.IsNullOrWhiteSpace(hit.IconUrl))
        {
            var img = new Image { Width = 48, Height = 48, Stretch = Stretch.UniformToFill };
            try { img.Source = new BitmapImage(new Uri(hit.IconUrl)); } catch { /* 图标失败忽略 */ }
            iconBox.Child = img;
        }
        Grid.SetColumn(iconBox, 0);

        // 文字信息
        var mid = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = hit.DisplayTitle, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = B_111827, VerticalAlignment = VerticalAlignment.Center,
        });
        var kindPill = new Border
        {
            Background = UiTheme.AccentSoftBrush, CornerRadius = UiTheme.Radius(4),
            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        kindPill.Child = new TextBlock
        {
            Text = ModrinthService.KindLabel(_currentKind), FontSize = 11, Foreground = UiTheme.AccentBrush,
        };
        titleRow.Children.Add(kindPill);
        mid.Children.Add(titleRow);

        mid.Children.Add(new TextBlock
        {
            Text = $"作者：{hit.Author} · 下载量 {FormatCount(hit.Downloads)}",
            FontSize = 12, Foreground = B_9CA3AF, Margin = new Thickness(0, 3, 0, 0),
        });
        mid.Children.Add(new TextBlock
        {
            Text = hit.Description, FontSize = 12, Foreground = B_6B7280,
            Margin = new Thickness(0, 3, 0, 0), MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(mid, 1);

        // 安装按钮
        var installBtn = new Button
        {
            Content = "安装", Style = (Style)FindResource("InstallBtnStyle"),
            Height = 32, FontSize = 13, Padding = new Thickness(16, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center, Tag = hit,
        };
        installBtn.Click += InstallResource_Click;
        Grid.SetColumn(installBtn, 2);

        row.Children.Add(iconBox);
        row.Children.Add(mid);
        row.Children.Add(installBtn);
        card.Child = row;
        return card;
    }

    private static string FormatCount(long n)
    {
        if (n >= 100_000_000) return $"{n / 100_000_000.0:0.#}亿";
        if (n >= 10_000) return $"{n / 10_000.0:0.#}万";
        return n.ToString();
    }

    private void InstallResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModrinthHit hit }) return;
        _pendingResourceHit = hit;
        _pendingResourceKind = _currentKind;
        _pendingResourceBtn = (Button)sender;
        ResourceModalTitle.Text = $"{ModrinthService.KindLabel(_currentKind)}：{hit.DisplayTitle}";
        FillResourceVersionCombo();
        ShowOverlayWithBounce(ResourceModalOverlay);
    }

    /// <summary>显示遮罩弹窗并播放 Q 弹入场动画（缩放 + 上移回弹）。</summary>
    private static void ShowOverlayWithBounce(Border overlay)
    {
        if (overlay.Child is Border card)
        {
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            var group = new TransformGroup();
            group.Children.Add(new TranslateTransform(0, 16));
            group.Children.Add(new ScaleTransform(0.94, 0.94));
            card.RenderTransform = group;

            var slide = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(580))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            ((TranslateTransform)group.Children[0]).BeginAnimation(TranslateTransform.YProperty, slide);
            var sx = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(580))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            var sy = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(580))
            {
                EasingFunction = UiAnimation.QElastic(8),
            };
            ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            ((ScaleTransform)group.Children[1]).BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }
        overlay.Visibility = Visibility.Visible;
    }

    // ===================== 资源安装：目标版本选择 =====================

    private void FillResourceVersionCombo()
    {
        ResourceTargetVersionCombo.Items.Clear();
        var current = SettingsStore.Current.SelectedVersion ?? "";
        var installed = VersionsService.GetVersions().Where(v => v.Installed).ToList();
        if (installed.Count == 0)
        {
            ResourceTargetVersionCombo.Items.Add(new ComboBoxItem { Content = "（暂无已安装版本）", Tag = "" });
            ResourceTargetVersionCombo.SelectedIndex = 0;
            return;
        }

        ComboBoxItem? toSelect = null;
        foreach (var v in installed.OrderByDescending(v => v.Id))
        {
            var item = new ComboBoxItem { Content = v.Id, Tag = v.Id };
            if (v.Id == current) toSelect = item;
            ResourceTargetVersionCombo.Items.Add(item);
        }
        if (toSelect != null) ResourceTargetVersionCombo.SelectedItem = toSelect;
        else ResourceTargetVersionCombo.SelectedIndex = 0;
    }

    private void ResourceModalCancel_Click(object sender, RoutedEventArgs e)
    {
        ResourceModalOverlay.Visibility = Visibility.Collapsed;
        _pendingResourceHit = null;
        _pendingResourceBtn = null;
    }

    private async void ResourceModalOk_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingResourceHit == null) return;
        var target = (ResourceTargetVersionCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(target))
        {
            ShowToast("请先选择目标版本。", ToastType.Warning);
            return;
        }

        var hit = _pendingResourceHit;
        var kind = _pendingResourceKind;
        var btn = _pendingResourceBtn;
        ResourceModalOverlay.Visibility = Visibility.Collapsed;
        _pendingResourceHit = null;
        _pendingResourceBtn = null;

        if (btn != null)
        {
            btn.IsEnabled = false;
            var original = btn.Content;
            btn.Content = "安装中…";
            var (success, error) = await Task.Run(() => ModrinthService.InstallAsync(hit, kind, target));
            if (success)
            {
                btn.Content = "已安装";
                btn.Style = (Style)FindResource("InstalledBtnStyle");
                ShowToast($"已安装到「{target}」");
            }
            else
            {
                btn.Content = original;
                btn.IsEnabled = true;
                ShowToast(error ?? "安装失败", ToastType.Error);
            }
        }
        else
        {
            var (success, error) = await Task.Run(() => ModrinthService.InstallAsync(hit, kind, target));
            ShowToast(success ? $"已安装到「{target}」" : (error ?? "安装失败"),
                success ? ToastType.Success : ToastType.Error);
        }
    }

    private void ShowToast(string message, ToastType type = ToastType.Success)
    {
        if (Window.GetWindow(this) is MainWindow mw) mw.ShowToast(message, type);
    }

    // ===================== 版本列表 =====================

    private async Task LoadVersionsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        RefreshBtn.IsEnabled = false;
        LoadingPlaceholder.Visibility = Visibility.Visible;
        EmptyPlaceholder.Visibility = Visibility.Collapsed;
        ClearVersionCards();

        var (versions, error) = await VersionsService.RefreshVersionsAsync();
        _isLoading = false;
        RefreshBtn.IsEnabled = true;

        if (error != null)
        {
            LoadingPlaceholder.Visibility = Visibility.Collapsed;
            ShowErrorState(error);
            return;
        }

        if (versions == null || versions.Count == 0)
        {
            LoadingPlaceholder.Visibility = Visibility.Collapsed;
            EmptyPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        _allVersions = versions;
        LoadingPlaceholder.Visibility = Visibility.Collapsed;
        ApplyFilterAndSearch();
    }

    private void ClearVersionCards()
    {
        var toRemove = VersionListPanel.Children.OfType<Border>().Where(b => b.Tag is not null && b.Tag.ToString() == "version-card").ToList();
        foreach (var card in toRemove)
            VersionListPanel.Children.Remove(card);
    }

    private void ShowErrorState(string error)
    {
        var errBorder = new Border
        {
            Background = B_FEF2F2, BorderBrush = B_FECACA,
            BorderThickness = new Thickness(1), CornerRadius = UiTheme.Radius(8),
            Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Tag = "version-card",
        };
        errBorder.Child = new TextBlock
        {
            Text = $"加载失败: {error}，请检查网络后重试",
            Foreground = B_DC2626, FontSize = 13, TextWrapping = TextWrapping.Wrap,
        };
        VersionListPanel.Children.Add(errBorder);
    }

    private void ApplyFilterAndSearch()
    {
        ClearVersionCards();
        _filteredVersions = _allVersions.Where(v =>
        {
            bool typeOk = _currentFilter switch
            {
                "release" => v.Type == "release",
                "snapshot" => v.Type == "snapshot",
                "old_alpha" => v.Type == "old_alpha",
                "old_beta" => v.Type == "old_beta",
                _ => true,
            };
            bool searchOk = string.IsNullOrEmpty(_searchText)
                || v.Id.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            return typeOk && searchOk;
        }).ToList();

        if (_filteredVersions.Count == 0)
        {
            var empty = new Border
            {
                Background = UiTheme.CardBackground, BorderBrush = B_E5E7EB,
                BorderThickness = new Thickness(1), CornerRadius = UiTheme.Radius(8),
                Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 12), Tag = "version-card",
            };
            empty.Child = new TextBlock
            {
                Text = "没有匹配的版本", FontSize = 13,
                Foreground = B_9CA3AF, HorizontalAlignment = HorizontalAlignment.Center,
            };
            VersionListPanel.Children.Add(empty);
            return;
        }

        for (var i = 0; i < _filteredVersions.Count; i++)
        {
            var card = BuildVersionCard(_filteredVersions[i]);
            VersionListPanel.Children.Add(card);
            if (i < 12) AnimateListCard(card); // 只动画前几项，避免大量卡片同时动画造成卡顿
        }
    }

    private Border BuildVersionCard(VersionEntry v)
    {
        var card = new Border
        {
            Background = UiTheme.CardBackground, BorderBrush = B_E5E7EB,
            BorderThickness = new Thickness(1), CornerRadius = UiTheme.Radius(8),
            Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Tag = "version-card",
        };
        var panel = new StackPanel();

        var row1 = new Grid();
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 10, Height = 10,
            Fill = B_D1D5DB, // 不表示安装状态，列表始终可下载
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(dot, 0);

        var midPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var idRow = new StackPanel { Orientation = Orientation.Horizontal };
        var idBlock = new TextBlock
        {
            Text = v.Id, FontSize = 14,
            FontWeight = FontWeights.SemiBold, Foreground = B_111827,
        };
        idRow.Children.Add(idBlock);

        var pill = BuildTypePill(v);
        if (pill != null) idRow.Children.Add(pill);
        midPanel.Children.Add(idRow);

        var dateStr = FormatDate(v.ReleaseTime);
        midPanel.Children.Add(new TextBlock
        {
            Text = dateStr, FontSize = 12, Foreground = B_9CA3AF, Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(midPanel, 1);

        // 下载页就是下载页：不显示"已安装"，任何版本都可重复下载（可用自定义名称区分）
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var installBtn = new Button
        {
            Content = "安装", Style = (Style)FindResource("InstallBtnStyle"),
            Tag = v,
        };
        installBtn.Click += InstallBtn_Click_Version;
        rightPanel.Children.Add(installBtn);

        if (DownloadManager.HasActive)
        {
            var goDownloadBtn = new Button
            {
                Content = "下载管理", Style = (Style)FindResource("GhostBtnStyle"),
                Margin = new Thickness(8, 0, 0, 0),
            };
            goDownloadBtn.Click += GoToDownloadPage;
            rightPanel.Children.Add(goDownloadBtn);
        }
        Grid.SetColumn(rightPanel, 2);

        row1.Children.Add(dot);
        row1.Children.Add(midPanel);
        row1.Children.Add(rightPanel);
        panel.Children.Add(row1);

        // 精确匹配版本 ID（不能用 Contains：下载 1.20.1 时会把 1.20/1.20.10 等卡片也误判为同一任务）
        var activeTask = DownloadManager.Tasks.Values.FirstOrDefault(t =>
            !t.Done && string.Equals(t.Name, v.Id, StringComparison.OrdinalIgnoreCase));
        if (activeTask != null)
        {
            var pRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            pRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var progressBar = new ProgressBar
            {
                Minimum = 0, Maximum = 100,
                Value = activeTask.Percent,
                Height = 6, IsIndeterminate = false,
            };
            // 自定义进度条样式（圆角）
            progressBar.Template = CreateProgressBarTemplate();
            Grid.SetColumn(progressBar, 0);

            var pctText = new TextBlock
            {
                Text = $"{activeTask.Percent}%", FontSize = 12,
                Foreground = B_9CA3AF, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(pctText, 1);
            pRow.Children.Add(progressBar);
            pRow.Children.Add(pctText);

            Action<DownloadTask> handler = null!;
            long lastUpdate = 0;
            handler = task =>
            {
                if (task.Id != activeTask.Id) return;
                // 节流：最多每 100ms 更新一次 UI，避免高频进度事件卡住界面
                var now = Environment.TickCount64;
                if (!task.Done && now - lastUpdate < 100) return;
                lastUpdate = now;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    progressBar.Value = task.Percent;
                    pctText.Text = $"{task.Percent}%";
                    if (task.Done)
                    {
                        DownloadManager.Progress -= handler;
                        if (task.Success) ApplyFilterAndSearch();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            };
            DownloadManager.Progress += handler;
            panel.Children.Add(pRow);
        }

        card.Child = panel;
        return card;
    }

    /// <summary>列表卡片轻量入场动画（淡入 + 上移）。只在列表前几项使用，避免大量卡片同时动画造成卡顿。</summary>
    private static void AnimateListCard(Border card)
    {
        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 10);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
        var slideUp = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideUp);
    }

    private static ControlTemplate CreateProgressBarTemplate()
    {
        // 标准 ProgressBar 模板结构：PART_Track（轨道）+ PART_Indicator（进度指示），
        // WPF 内部会按 Value 自动更新 PART_Indicator 的宽度
        var template = new ControlTemplate(typeof(ProgressBar));
        var trackFactory = new FrameworkElementFactory(typeof(Border));
        trackFactory.SetValue(Border.BackgroundProperty, B_F3F4F6);
        trackFactory.SetValue(Border.CornerRadiusProperty, UiTheme.Radius(3));
        trackFactory.SetValue(Border.HeightProperty, 6.0);
        trackFactory.Name = "PART_Track";

        var indicatorFactory = new FrameworkElementFactory(typeof(Border));
        indicatorFactory.SetValue(Border.BackgroundProperty, UiTheme.AccentBrush);
        indicatorFactory.SetValue(Border.CornerRadiusProperty, UiTheme.Radius(3));
        indicatorFactory.SetValue(Border.HeightProperty, 6.0);
        indicatorFactory.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        indicatorFactory.SetValue(FrameworkElement.WidthProperty, 0.0);
        indicatorFactory.Name = "PART_Indicator";

        trackFactory.AppendChild(indicatorFactory);
        template.VisualTree = trackFactory;
        return template;
    }

    private static readonly HashSet<string> AprilFoolsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "1.rv-pre1", "15w14a", "20w14infinite", "22w13oneblockatatime",
        "23w13a_or_b", "3d shareware v1.34", "24w14potato", "25w14craftmine",
    };

    private static bool IsAprilFools(string? id)
        => !string.IsNullOrWhiteSpace(id) && AprilFoolsIds.Contains(id);

    /// <summary>版本类型的中文显示：正式版 / 预览版 / 远古版 / 愚人节版。</summary>
    private static string VersionTypeLabel(VersionEntry ver) => ver.Type switch
    {
        "release" => IsAprilFools(ver.Id) ? "愚人节版" : "正式版",
        "snapshot" => IsAprilFools(ver.Id) ? "愚人节版" : "预览版",
        "old_alpha" => "远古版",
        "old_beta" => "远古版",
        _ => ver.Type,
    };

    private Border? BuildTypePill(VersionEntry ver)
    {
        var label = VersionTypeLabel(ver);
        // 愚人节版使用粉色徽标
        if (IsAprilFools(ver.Id))
        {
            return new Border
            {
                Background = ColorBrush("#FCE7F3"), CornerRadius = UiTheme.Radius(4),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = label, FontSize = 11, Foreground = ColorBrush("#DB2777") },
            };
        }
        // 「预览版」使用当前主色，其余版本类型使用固定语义色
        if (ver.Type == "snapshot")
        {
            return new Border
            {
                Background = UiTheme.AccentSoftBrush, CornerRadius = UiTheme.Radius(4),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = label, FontSize = 11, Foreground = UiTheme.AccentBrush },
            };
        }

        var (bg, fgText, _) = ver.Type switch
        {
            "release" => ("#DCFCE7", "#15803D", "正式版"),
            "old_alpha" => ("#FFEDD5", "#C2410C", "远古版"),
            "old_beta" => ("#EDE9FE", "#7C3AED", "远古版"),
            _ => (null, null, null),
        };
        if (bg == null) return null;

        var border = new Border
        {
            Background = ColorBrush(bg), CornerRadius = UiTheme.Radius(4),
            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        border.Child = new TextBlock
        {
            Text = label, FontSize = 11, Foreground = ColorBrush(fgText!),
        };
        return border;
    }

    private static string FormatDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        try { return DateTime.Parse(raw).ToString("yyyy-MM-dd"); }
        catch { return ""; }
    }

    // ===================== 筛选 =====================

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag as string ?? "all";
        foreach (var name in new[] { "FilterAll", "FilterRelease", "FilterSnapshot", "FilterOldAlpha", "FilterOldBeta" })
        {
            if (FindName(name) is Button b)
            {
                b.FontWeight = FontWeights.Normal;
                b.Foreground = B_6B7280;
            }
        }
        btn.FontWeight = FontWeights.SemiBold;
        btn.Foreground = UiTheme.AccentBrush;
        _currentFilter = tag;
        ApplyFilterAndSearch();
    }

    // ===================== 搜索 =====================

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Visible : Visibility.Collapsed;
        if (_isResourceMode)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
            return;
        }
        ApplyFilterAndSearch();
    }

    // ===================== 刷新 =====================

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isResourceMode) { await LoadResourcesAsync(); return; }
        await LoadVersionsAsync();
    }

    // ===================== 安装弹窗 =====================

    private void InstallBtn_Click_Version(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not VersionEntry ver) return;

        _modalVersionId = ver.Id;
        _selectedLoader = "vanilla";
        ResetLoaderSelection();

        ModalVersionId.Text = ver.Id;
        var typeLabel = VersionTypeLabel(ver);
        ModalVersionType.Text = typeLabel;
        ModalTypeLabel.Text = typeLabel;
        if (IsAprilFools(ver.Id))
        {
            ModalTypePill.Background = ColorBrush("#FCE7F3");
            ModalTypePill.Child = new TextBlock
            {
                Text = typeLabel, FontSize = 11, FontWeight = FontWeights.Medium,
                Foreground = ColorBrush("#DB2777"),
            };
        }
        else
        {
            ModalTypePill.Background = ver.Type switch
            {
                "release" => B_DCFCE7, "snapshot" => UiTheme.AccentSoftBrush,
                "old_alpha" => B_FFEDD5, "old_beta" => B_EDE9FE, _ => UiTheme.AccentSoftBrush,
            };
            ModalTypePill.Child = new TextBlock
            {
                Text = typeLabel, FontSize = 11, FontWeight = FontWeights.Medium,
                Foreground = ver.Type switch
                {
                    "release" => B_15803D, "snapshot" => UiTheme.AccentBrush,
                    "old_alpha" => B_C2410C, "old_beta" => B_7C3AED, _ => UiTheme.AccentBrush,
                },
            };
        }

        InstallNameBox.Text = ver.Id; // 默认安装名称 = 版本号（用户可改）
        ModalOverlay.Visibility = Visibility.Visible;
        FocusManager.SetFocusedElement(this, ModalVersionId);
    }

    private void ResetLoaderSelection()
    {
        foreach (var b in _loaderBorders)
        {
            b.BorderThickness = new Thickness(1);
            if (b == LoaderVanilla) b.Background = ColorBrush("#BFDBFE");
            else if (b == LoaderFabric) b.Background = ColorBrush("#DDD6FE");
            else if (b == LoaderForge) b.Background = ColorBrush("#FED7AA");
            else if (b == LoaderNeoForge) b.Background = ColorBrush("#A7F3D0");
            else if (b == LoaderOptiFine) b.Background = ColorBrush("#BAE6FD");
            else b.Background = ColorBrush("#BFDBFE");
        }
        MarkLoaderSelected("vanilla");
    }

    private void MarkLoaderSelected(string loader)
    {
        _selectedLoader = loader;
        // 选中高亮：深色边框
        var highlightBg = loader switch
        {
            "vanilla" => "#2563EB", "fabric" => "#7C3AED", "forge" => "#EA580C",
            "neoforge" => "#059669", "optifine" => "#0284C7", _ => "#2563EB",
        };
        var highlightBorder = ColorBrush(highlightBg);
        foreach (var b in _loaderBorders) b.BorderThickness = new Thickness(1);

        var target = loader switch
        {
            "vanilla" => LoaderVanilla, "fabric" => LoaderFabric, "forge" => LoaderForge,
            "neoforge" => LoaderNeoForge, "optifine" => LoaderOptiFine, _ => LoaderVanilla,
        };
        target.BorderThickness = new Thickness(2);
        target.BorderBrush = highlightBorder;

        // 切换加载器时，安装名称自动跟随切换（用户可再手动修改）
        if (_modalVersionId != null)
            InstallNameBox.Text = GetDefaultInstallName(_modalVersionId, loader);
    }

    /// <summary>根据版本 ID 与加载器生成默认安装名称。</summary>
    private static string GetDefaultInstallName(string versionId, string loader) => loader switch
    {
        "vanilla" => versionId,
        "fabric" => $"{versionId}-fabric",
        "forge" => $"{versionId}-forge",
        "neoforge" => $"{versionId}-neoforge",
        "optifine" => $"{versionId}-OptiFine",
        _ => versionId,
    };

    private void LoaderSelected(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border) return;
        var loader = border.Name switch
        {
            nameof(LoaderVanilla) => "vanilla", nameof(LoaderFabric) => "fabric",
            nameof(LoaderForge) => "forge", nameof(LoaderNeoForge) => "neoforge",
            nameof(LoaderOptiFine) => "optifine", _ => "vanilla",
        };
        MarkLoaderSelected(loader);
    }

    private void ModalOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        // 点击的是弹窗卡片内部（加载器/输入框/按钮）时不关闭，只有点击遮罩背景才关闭
        if (e.OriginalSource is DependencyObject dpo && ModalCard.IsAncestorOf(dpo))
            return;
        CloseModal();
    }
    private void ModalClose_Click(object sender, RoutedEventArgs e) => CloseModal();

    private void CloseModal()
    {
        // 下载过程中也允许手动关闭弹窗（下载任务不受影响，继续在后台进行）
        ModalOverlay.Visibility = Visibility.Collapsed;
        _modalVersionId = null;
    }

    private void InstallNameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) InstallBtn_Click(sender, e);
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling || _modalVersionId == null) return;
        var versionId = _modalVersionId;
        var nameOverride = InstallNameBox.Text.Trim();
        var loader = _selectedLoader;

        // 重名检查：versions 目录中不允许出现两个相同的版本名称
        var installId = loader == "vanilla"
            ? (string.IsNullOrWhiteSpace(nameOverride) ? versionId : nameOverride)
            : (string.IsNullOrWhiteSpace(nameOverride) ? null : nameOverride);
        if (installId != null)
        {
            var targetDir = System.IO.Path.Combine(Services.Store.GetVersionsDir(), installId);
            if (Directory.Exists(targetDir))
            {
                ShowToast($"版本目录中已存在「{installId}」，请更换安装名称", ToastType.Error);
                return;
            }
        }

        _isInstalling = true;

        // 立即隐藏弹窗，下载进度在顶部导航栏显示，不再停留"下载版本"界面
        ModalOverlay.Visibility = Visibility.Collapsed;
        _modalVersionId = null;

        // 加载器一键安装：Fabric/Forge/NeoForge/OptiFine 由 LoaderService 处理（含原版父版本自动安装）。
        // 用 Task.Run 放到后台线程执行，避免解析 assets 索引/收集文件等重活阻塞 UI（否则点击下载后界面会卡几秒）。
        var (success, error) = loader == "vanilla"
            ? await Task.Run(() => VersionsService.InstallAsync(versionId, string.IsNullOrEmpty(nameOverride) ? null : nameOverride))
            : await Task.Run(() => LoaderService.InstallAsync(versionId, loader, string.IsNullOrEmpty(nameOverride) ? null : nameOverride));

        _isInstalling = false;

        if (success)
        {
            ApplyFilterAndSearch();
        }
        else
        {
            ShowToast(error ?? "下载失败", ToastType.Error);
        }
    }

    private void GoToDownloadPage(object sender, RoutedEventArgs e)
    {
        // 打开独立的「下载管理」页
        if (Window.GetWindow(this) is MainWindow mw)
            mw.ShowDownloadPage();
    }
}
