// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CraftUnifiedBooter.Services;
using Path = System.Windows.Shapes.Path;

namespace CraftUnifiedBooter.Pages;

public partial class DownloadPage : UserControl
{
    private static readonly Brush GreenBrush = FrozenBrush("#16A34A");
    private static readonly Brush RedBrush = FrozenBrush("#EF4444");
    private static readonly Brush OrangeBrush = FrozenBrush("#F97316");
    private static readonly Brush PurpleBrush = FrozenBrush("#8B5CF6");
    private static readonly Brush CyanBrush = FrozenBrush("#06B6D4");
    private static readonly Brush GrayTextBrush = FrozenBrush("#9CA3AF");
    private static readonly Brush DarkTextBrush = FrozenBrush("#111827");
    private static readonly Brush CardBorderBrush = FrozenBrush("#E5E7EB");
    private static readonly Brush CardBorderFailBrush = FrozenBrush("#FECACA");

    private string _filterStatus = "all";
    private string _sortMode = "time";

    public DownloadPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { Unhook(); Refresh(); };
        Unloaded += (_, _) => Unhook();
    }

    private void Hook()
    {
        DownloadManager.Progress += OnProgress;
        DownloadManager.Completed += OnCompleted;
        DownloadManager.QueueChanged += OnQueueChanged;
    }

    private void Unhook()
    {
        DownloadManager.Progress -= OnProgress;
        DownloadManager.Completed -= OnCompleted;
        DownloadManager.QueueChanged -= OnQueueChanged;
    }

    private bool _summaryUpdateQueued;

    private void OnProgress(DownloadTask t)
    {
        if (_summaryUpdateQueued) return;
        _summaryUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _summaryUpdateQueued = false;
            UpdateSummary();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnCompleted(DownloadTask t) =>
        Dispatcher.BeginInvoke(() => { UpdateSummary(); RenderTasks(); },
            System.Windows.Threading.DispatcherPriority.Background);

    private void OnQueueChanged() =>
        Dispatcher.BeginInvoke(() => { UpdateSummary(); RenderTasks(); },
            System.Windows.Threading.DispatcherPriority.Background);

    public void Refresh()
    {
        Hook();
        // 列表渲染推迟到 UI 空闲时执行：先让页面立刻显示，再填充任务卡片，避免点击后卡顿
        Dispatcher.BeginInvoke(new Action(RenderTasks), System.Windows.Threading.DispatcherPriority.Background);
    }

    // ===================== 汇总卡片 =====================

    private long _lastTotalDownloaded;
    private DateTime _lastSpeedTime = DateTime.MinValue;
    private double _currentSpeed;

    private void UpdateSummary()
    {
        if (PercentLabel == null || TaskItemsPanel == null) return;
        var active = DownloadManager.GetActiveTasks();
        var totalDownloaded = active.Sum(t => t.Downloaded);
        var totalSize = active.Sum(t => t.Total);
        var overallPercent = totalSize > 0 ? (int)(totalDownloaded * 100.0 / totalSize) : 0;

        if (active.Count > 0)
        {
            var now = DateTime.UtcNow;
            if (_lastSpeedTime != DateTime.MinValue && now > _lastSpeedTime)
            {
                var dt = (now - _lastSpeedTime).TotalSeconds;
                if (dt > 0)
                {
                    var instant = Math.Max(0, (totalDownloaded - _lastTotalDownloaded) / dt);
                    _currentSpeed = _currentSpeed <= 0 ? instant : _currentSpeed * 0.7 + instant * 0.3;
                }
            }
            _lastTotalDownloaded = totalDownloaded;
            _lastSpeedTime = now;
        }
        else
        {
            _currentSpeed = 0;
            _lastTotalDownloaded = 0;
            _lastSpeedTime = DateTime.MinValue;
        }

        PercentLabel.Text = $"{overallPercent}%";
        SpeedLabel.Text = $"{FormatSpeed(_currentSpeed)}";
        TotalSizeLabel.Text = $"{FormatBytes(totalDownloaded)} / {FormatBytes(totalSize)}";
        RemainingLabel.Text = $"剩余文件: {active.Sum(t => t.TotalFiles - t.DownloadedFiles)}";

        if (ProgressFill != null && ProgressTrack.ActualWidth > 0)
        {
            ProgressFill.Width = ProgressTrack.ActualWidth * overallPercent / 100.0;
        }

        ActiveBadge.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveBadgeText.Text = $"{active.Count} 个进行中";
    }

    // ===================== 筛选 / 排序 =====================

    private void FilterStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        _filterStatus = tag;
        foreach (var name in new[] { "FilterAll", "FilterActive", "FilterPaused", "FilterDone", "FilterCanceled" })
        {
            var b = (Button)FindName(name);
            if (b == null) continue;
            var selected = b == btn;
            b.Style = (Style)FindResource(selected ? "FilterChipStyle" : "FilterChipUnselectedStyle");
        }
        RenderTasks();
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 加载期间 ComboBox 的 IsSelected 会提前触发此事件，此时页面控件尚未就绪
        if (TaskItemsPanel == null) return;
        _sortMode = SortCombo.SelectedIndex switch
        {
            1 => "priority",
            2 => "progress",
            _ => "time",
        };
        RenderTasks();
    }

    private IEnumerable<DownloadTask> ApplyFilterAndSort(IEnumerable<DownloadTask> tasks)
    {
        var filtered = _filterStatus switch
        {
            "active" => tasks.Where(t => t.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused),
            "paused" => tasks.Where(t => t.Status == DownloadStatus.Paused),
            "done" => tasks.Where(t => t.Status is DownloadStatus.Completed or DownloadStatus.Failed),
            "canceled" => tasks.Where(t => t.Status == DownloadStatus.Canceled),
            _ => tasks,
        };
        return _sortMode switch
        {
            "priority" => filtered.OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt),
            "progress" => filtered.OrderByDescending(t => t.Percent).ThenBy(t => t.CreatedAt),
            _ => filtered.OrderByDescending(t => t.CreatedAt),
        };
    }

    // ===================== 渲染任务列表 =====================

    private System.Windows.Threading.DispatcherTimer? _renderTimer;

    private void RenderTasks()
    {
        if (TaskItemsPanel == null || EmptyState == null) return;
        _renderTimer?.Stop();
        TaskItemsPanel.Children.Clear();
        var all = DownloadManager.Tasks.Values.ToList();
        var filtered = ApplyFilterAndSort(all).ToList();

        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 历史统计（清空历史用）
        var history = DownloadManager.GetHistory();
        if (history.Count > 0)
        {
            HistoryHeaderRow.Visibility = Visibility.Visible;
            HistoryItemsPanel.Visibility = Visibility.Collapsed;
            var okCount = history.Count(t => t.Success);
            var failCount = history.Count(t => !t.Success);
            HistoryOkText.Text = $"{okCount} 已完成";
            HistoryFailText.Text = $"{failCount} 失败";
            HistoryOkBadge.Visibility = okCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryFailBadge.Visibility = failCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            HistoryHeaderRow.Visibility = Visibility.Collapsed;
            HistoryItemsPanel.Visibility = Visibility.Collapsed;
        }

        UpdateSummary();

        // 分批渲染：每帧最多 10 个卡片，避免任务很多时一次性创建全部卡片导致界面卡死
        int index = 0;
        _renderTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1),
        };
        _renderTimer.Tick += (_, _) =>
        {
            for (int i = 0; i < 10 && index < filtered.Count; i++, index++)
                TaskItemsPanel.Children.Add(BuildTaskCard(filtered[index]));
            if (index >= filtered.Count)
            {
                _renderTimer.Stop();
                _renderTimer = null;
            }
        };
        _renderTimer.Start();
    }

    // ===================== 任务卡片 =====================

    private FrameworkElement BuildTaskCard(DownloadTask task)
    {
        var status = task.Status;
        var isActive = !task.Done;
        var isPaused = status == DownloadStatus.Paused;

        var border = new Border
        {
            Background = UiTheme.CardBackground,
            CornerRadius = UiTheme.Radius(12),
            BorderBrush = status == DownloadStatus.Failed ? CardBorderFailBrush : CardBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Opacity = task.Done ? 0.75 : 1.0,
        };

        var stack = new StackPanel();

        // 顶部 Grid：图标 | 名称+状态 | 操作按钮
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 类型图标
        var iconBorder = new Border
        {
            Width = 40, Height = 40, CornerRadius = UiTheme.Radius(8),
            Background = kindBrush(task.Kind),
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconBorder.Child = CreateTaskIcon(task.Kind);
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // 名称 + 状态徽标 + 详情
        var namePanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(MakeText(task.Name, 14, FontWeights.SemiBold, DarkTextBrush));
        titleRow.Children.Add(BuildStatusBadge(status));
        namePanel.Children.Add(titleRow);

        var detailRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        detailRow.Children.Add(MakeText($"{FormatBytes(task.Downloaded)} / {FormatBytes(task.Total)}", 12, FontWeights.Normal, GrayTextBrush));
        detailRow.Children.Add(MakeText($" · 优先级 {task.Priority}", 12, FontWeights.Normal, GrayTextBrush, new Thickness(8, 0, 0, 0)));
        namePanel.Children.Add(detailRow);
        Grid.SetColumn(namePanel, 1);
        grid.Children.Add(namePanel);

        // 操作按钮
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (isActive)
        {
            // 暂停/继续
            var pauseBtn = new Button
            {
                Content = isPaused ? "继续" : "暂停",
                Tag = task.Id,
                Style = (Style)FindResource("GhostButtonStyle"),
                Height = 30, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
            };
            pauseBtn.Click += isPaused ? Resume_Click : Pause_Click;
            actions.Children.Add(pauseBtn);

            // 取消
            var cancelBtn = new Button
            {
                Content = "取消",
                Tag = task.Id,
                Style = (Style)FindResource("GhostButtonStyle"),
                Height = 30, Padding = new Thickness(12, 0, 12, 0),
            };
            cancelBtn.Click += Cancel_Click;
            actions.Children.Add(cancelBtn);
        }
        else
        {
            // 已结束：清空历史按钮由底部"清空历史"统一处理，这里只显示状态文字
        }
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        stack.Children.Add(grid);

        // 进度条（进行中的显示进度；已完成/取消显示最终进度条）
        if (task.Total > 0 || isActive)
        {
            var pct = task.Total > 0 ? (int)(task.Downloaded * 100.0 / task.Total) : 0;
            var trackBorder = new Border
            {
                Height = 6, Background = FrozenBrush("#F3F4F6"), CornerRadius = UiTheme.Radius(3),
                Margin = new Thickness(0, 12, 0, 0),
            };
            var fillGrid = new Grid();
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(pct, 1), GridUnitType.Star) });
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - pct, 1), GridUnitType.Star) });
            var fill = new Border
            {
                Background = status switch
                {
                    DownloadStatus.Paused => OrangeBrush,
                    DownloadStatus.Canceled => GrayTextBrush,
                    DownloadStatus.Failed => RedBrush,
                    DownloadStatus.Completed => GreenBrush,
                    _ => UiTheme.AccentBrush,
                },
                CornerRadius = UiTheme.Radius(3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Grid.SetColumn(fill, 0);
            fillGrid.Children.Add(fill);
            trackBorder.Child = fillGrid;
            stack.Children.Add(trackBorder);
            stack.Children.Add(MakeText($"{pct}%", 12, FontWeights.Normal, GrayTextBrush, new Thickness(0, 6, 0, 0)));
        }

        border.Child = stack;
        return border;
    }

    private Border BuildStatusBadge(DownloadStatus status)
    {
        // 「下载中」使用当前主色，其余状态使用固定语义色
        if (status == DownloadStatus.Downloading)
        {
            return new Border
            {
                Background = UiTheme.AccentSoftBrush, CornerRadius = UiTheme.Radius(4),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "下载中", FontSize = 11, Foreground = UiTheme.AccentBrush },
            };
        }

        var (bg, fg, text) = status switch
        {
            DownloadStatus.Queued => ("#F3F4F6", "#6B7280", "等待中"),
            DownloadStatus.Paused => ("#FFEDD5", "#C2410C", "已暂停"),
            DownloadStatus.Completed => ("#DCFCE7", "#15803D", "已完成"),
            DownloadStatus.Canceled => ("#F3F4F6", "#6B7280", "已取消"),
            DownloadStatus.Failed => ("#FEE2E2", "#B91C1C", "失败"),
            _ => ("#F3F4F6", "#6B7280", "未知"),
        };
        return new Border
        {
            Background = ColorBrush(bg), CornerRadius = UiTheme.Radius(4),
            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = ColorBrush(fg) },
        };
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) DownloadManager.Pause(id);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) DownloadManager.Resume(id);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) DownloadManager.Cancel(id);
    }

    // ===================== 工具方法 =====================

    private static Shape CreateTaskIcon(string kind)
    {
        string data = kind switch
        {
            "Mod" => "M20 13C20 18 16.5 20.8 12 22C7.5 20.8 4 18 4 13V6L12 2L20 6V13Z M9 12L11 14L15 10",
            "资源包" => "M16.5 9.4L7.55 4.24 M21 16V8A2 2 0 0 0 20 6.2L13 2.2A2 2 0 0 0 11 2.2L4 6.2A2 2 0 0 0 3 8V16A2 2 0 0 0 4 17.8L11 21.8A2 2 0 0 0 13 21.8L20 17.8A2 2 0 0 0 21 16Z M7.5 10.5L12 13L16.5 10.5 M12 13V21",
            "Java" => "M16 21V19A4 4 0 0 0 12 15H6A4 4 0 0 0 2 19V21 M9 3A4 4 0 1 1 9 11A4 4 0 1 1 9 3Z",
            _ => "M21 15V18A2 2 0 0 1 19 20H5A2 2 0 0 1 3 18V15 M7 10L12 15L17 10 M12 15V3",
        };
        return new Path
        {
            Data = Geometry.Parse(data),
            Width = 20, Height = 20, Stretch = Stretch.Uniform,
            Stroke = kind == "Mod" ? PurpleBrush : UiTheme.AccentBrush,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
    }

    private static Brush kindBrush(string kind) => kind switch
    {
        "Mod" => FrozenBrush("#F5F3FF"),
        "资源包" => FrozenBrush("#FFF7ED"),
        "Java" => FrozenBrush("#ECFEFF"),
        _ => FrozenBrush("#EFF6FF"),
    };

    private static TextBlock MakeText(string text, double size, FontWeight weight, Brush brush, Thickness margin = default)
        => new TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight, Foreground = brush,
            Margin = margin, TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        return $"{mb / 1024.0:F2} GB";
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F0} KB/s";
        return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
    }

    private static Brush FrozenBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Brush ColorBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        DownloadManager.ClearHistory();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        // 关闭覆盖层（由导航栏"更多"打开）；若未打开则返回下载页
        if (Window.GetWindow(this) is MainWindow mw)
        {
            if (mw.IsDownloadOverlayVisible)
                mw.HideDownloadOverlay();
            else
                mw.SelectNav(1);
        }
    }
}