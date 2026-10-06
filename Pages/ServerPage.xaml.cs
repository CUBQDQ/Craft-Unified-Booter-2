// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter.Pages;

public partial class ServerPage : UserControl
{
    public ServerPage()
    {
        InitializeComponent();
        HongshiService.OutputReceived += OnKernelOutput;
        Loaded += async (_, _) =>
        {
            FillVersions();
            await FillRelaysAsync();
            AppendConsole("网络控制台已就绪，填写上方信息后点击「创建房间」即可开服联机。");
        };
    }

    private Process? _activeProc;
    private string? _activeEndpoint;

    // ===== 填充已安装版本 =====
    private void FillVersions()
    {
        RoomVersionCombo.Items.Clear();
        try
        {
            var dir = Store.GetVersionsDir();
            if (Directory.Exists(dir))
            {
                foreach (var d in Directory.GetDirectories(dir)
                             .Select(Path.GetFileName)
                             .Where(n => !string.IsNullOrEmpty(n))
                             .OrderByDescending(n => n))
                    RoomVersionCombo.Items.Add(d);
            }
        }
        catch { /* ignore */ }
        if (RoomVersionCombo.Items.Count > 0) RoomVersionCombo.SelectedIndex = 0;
    }

    // ===== 获取红石联机公共中继节点 =====
    private async Task FillRelaysAsync()
    {
        RelayCombo.Items.Clear();
        var relays = await HongshiService.GetRelayListAsync();
        foreach (var kv in relays)
            RelayCombo.Items.Add(new ComboBoxItem { Content = $"{kv.Key}（{kv.Value}）", Tag = kv.Value });
        if (RelayCombo.Items.Count > 0) RelayCombo.SelectedIndex = 0;
    }

    // ===== 网络控制台：追加日志 =====
    private void AppendConsole(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ConsoleBox.AppendText(line + Environment.NewLine);
            ConsoleBox.ScrollToEnd();
        });
    }

    private void OnKernelOutput(string line)
    {
        var now = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{now}] {line}");
    }

    // ===== 右上角 toast 提示（替代 MessageBox） =====
    private void ShowToast(string message, ToastType type = ToastType.Success)
        => (Application.Current.MainWindow as MainWindow)?.ShowToast(message, type);

    // ===== 更新房间状态 =====
    private void UpdateRoomState()
    {
        var running = _activeProc != null && !_activeProc.HasExited;
        if (running)
        {
            AddressText.Text = $"连接地址：{_activeEndpoint}";
            AddressText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            StatusText.Text = "● 联机中";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            EndRoomBtn.IsEnabled = true;
            CopyAddressBtn.IsEnabled = true;
        }
        else
        {
            AddressText.Text = "暂无连接地址";
            AddressText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            StatusText.Text = "未在联机";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            EndRoomBtn.IsEnabled = false;
            CopyAddressBtn.IsEnabled = false;
        }
    }

    // ===== 创建房间（红石联机） =====
    private async void CreateRoom_Click(object sender, RoutedEventArgs e)
    {
        var portText = RoomPortBox.Text?.Trim();
        var version = RoomVersionCombo.SelectedItem as string;

        if (string.IsNullOrEmpty(version))
        {
            ShowToast("请先安装并选择一个游戏版本", ToastType.Warning);
            return;
        }
        if (string.IsNullOrEmpty(portText)) portText = "25565";
        if (!int.TryParse(portText, out var gamePort) || gamePort <= 0 || gamePort > 65535)
        {
            ShowToast("游戏端口必须是有效数字（1-65535）", ToastType.Warning);
            return;
        }

        // 同一台机器同一时间只能运行一个红石联机内核
        if (_activeProc != null && !_activeProc.HasExited)
        {
            ShowToast("已有一个联机房间正在运行，请先结束房间", ToastType.Warning);
            return;
        }

        // 首次使用下载内核
        if (!HongshiService.IsKernelReady)
        {
            AppendConsole("首次使用需要下载红石联机内核（约 600KB），正在下载…");
            try
            {
                await HongshiService.EnsureKernelAsync();
                AppendConsole("红石联机内核下载完成。");
            }
            catch (Exception ex)
            {
                AppendConsole("红石联机内核下载失败：" + ex.Message);
                ShowToast("红石联机内核下载失败：" + ex.Message, ToastType.Error);
                return;
            }
        }

        if (RelayCombo.Items.Count == 0) await FillRelaysAsync();
        if (RelayCombo.Items.Count == 0)
        {
            ShowToast("暂时没有可用的红石联机公共节点，请稍后再试", ToastType.Warning);
            return;
        }
        var relay = (RelayCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(relay)) relay = "nj.hongshi.site";

        CreateBtn.IsEnabled = false;
        CreateBtnText.Text = "正在创建…";
        AppendConsole($"正在创建联机房间：版本 {version}，端口 {gamePort}，节点 {relay} …");
        try
        {
            var result = await Task.Run(() => HongshiService.CreateTunnelAsync(relay, gamePort));
            if (result.Proc == null || string.IsNullOrEmpty(result.Endpoint))
            {
                AppendConsole("❌ " + (result.Error ?? "红石联机隧道创建失败。"));
                ShowToast(result.Error ?? "红石联机隧道创建失败", ToastType.Error);
                return;
            }

            _activeProc = result.Proc;
            _activeEndpoint = result.Endpoint;
            UpdateRoomState();

            // 只复制地址本身，方便直接粘贴到游戏「多人游戏 → 直接连接」
            try { Clipboard.SetText(result.Endpoint); } catch { /* ignore */ }
            AppendConsole($"✅ 房间创建成功！连接地址：{result.Endpoint}");
            ShowToast($"✅ 房间创建成功！连接地址：{result.Endpoint}");
        }
        catch (Exception ex)
        {
            AppendConsole("❌ 创建房间异常：" + ex.Message);
            ShowToast("创建房间失败：" + ex.Message, ToastType.Error);
        }
        finally
        {
            CreateBtn.IsEnabled = true;
            CreateBtnText.Text = "创建房间";
        }
    }

    // ===== 复制连接地址（只复制地址本身） =====
    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeEndpoint)) return;
        try
        {
            Clipboard.SetText(_activeEndpoint);
            ShowToast("✅ 连接地址已复制");
        }
        catch { /* ignore */ }
    }

    // ===== 结束房间 =====
    private void EndRoom_Click(object sender, RoutedEventArgs e)
    {
        HongshiService.StopTunnel(_activeProc);
        _activeProc = null;
        _activeEndpoint = null;
        UpdateRoomState();
    }

    // ===== 清空控制台 =====
    private void ClearConsole_Click(object sender, RoutedEventArgs e)
    {
        ConsoleBox.Clear();
        AppendConsole("控制台已清空。");
    }
}