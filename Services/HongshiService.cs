// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CraftUnifiedBooter.Services;

/// <summary>
/// 红石联机（Redstone Online2）接入：下载内核 hongshic、获取公共中继节点、
/// 启动隧道并读取连接地址。房间 = 一个内核进程，进程退出即房间结束。
/// </summary>
public static class HongshiService
{
    private const string BaseUrl = "https://hongshi.site";
    private static readonly string Dir = Path.Combine(Store.CubDir, "hongshi");
    private static readonly string KernelPath = Path.Combine(Dir, "hongshic-windows-amd64.exe");

    /// <summary>当前运行中的红石联机内核进程（房间）</summary>
    private static readonly HashSet<Process> Running = new();

    /// <summary>匹配 ANSI 颜色码（内核输出即使在管道下也带颜色，需先清理）</summary>
    private static readonly Regex AnsiColorRegex = new(@"\x1B\[[0-9;]*[A-Za-z]");

    public static bool IsKernelReady => File.Exists(KernelPath) && new FileInfo(KernelPath).Length > 0;

    /// <summary>内核实时输出（已去除 ANSI 颜色码），供界面上的网络控制台显示。</summary>
    public static event Action<string>? OutputReceived;

    private static void RaiseOutput(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        OutputReceived?.Invoke(AnsiColorRegex.Replace(line, ""));
    }

    /// <summary>确保内核已下载（约 600KB，缓存到 CUB/hongshi，避免重复下载）</summary>
    public static async Task EnsureKernelAsync()
    {
        if (IsKernelReady) return;
        Store.EnsureDir(Dir);
        var tmp = KernelPath + ".tmp";
        await Downloader.DownloadFileAsync($"{BaseUrl}/api/download/client?platform=windows&arch=amd64", tmp);
        if (File.Exists(KernelPath)) File.Delete(KernelPath);
        File.Move(tmp, KernelPath);
    }

    /// <summary>获取公共中继节点列表（地区 → relay 地址，如 南京 → nj.hongshi.site）</summary>
    public static async Task<Dictionary<string, string>> GetRelayListAsync()
    {
        try
        {
            var json = await Downloader.Client.GetStringAsync($"{BaseUrl}/api/server/list");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>创建隧道：启动内核进程，读取 endpoint（连接地址）。进程退出 = 房间结束。</summary>
    public static async Task<(Process? Proc, string? Endpoint, string? Error)> CreateTunnelAsync(string relay, int gamePort)
    {
        if (!IsKernelReady) return (null, null, "红石联机内核未就绪");

        var psi = new ProcessStartInfo(KernelPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(relay);
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(gamePort.ToString());

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return (null, null, ex.Message);
        }

        // 从 stdout 中匹配 "endpoint=<地址>"（内核输出带 ANSI 颜色码，先清理再匹配）
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            // 去掉 ESC[3m / ESC[0m 等颜色序列，否则会被插在 "endpoint" 和 "=" 之间导致匹配失败
            var clean = AnsiColorRegex.Replace(e.Data, "");
            RaiseOutput(clean);
            var idx = clean.IndexOf("endpoint=", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var rest = clean[(idx + 9)..].Trim();
                var ep = rest.Split(' ', '\t', '\r', '\n')[0];
                if (!string.IsNullOrEmpty(ep)) tcs.TrySetResult(ep);
            }
        };
        proc.ErrorDataReceived += (_, e) => RaiseOutput(e.Data ?? "");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        string endpoint;
        try
        {
            endpoint = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            endpoint = "";
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            try { proc.Kill(); } catch { /* ignore */ }
            return (proc, null, "红石联机隧道创建失败（未获取到连接地址），请更换节点后重试");
        }

        lock (Running)
        {
            Running.Add(proc);
            proc.Exited += (_, _) => { lock (Running) Running.Remove(proc); };
        }
        return (proc, endpoint, null);
    }

    /// <summary>结束房间（停止内核进程）</summary>
    public static void StopTunnel(Process? proc)
    {
        if (proc == null) return;
        try { if (!proc.HasExited) proc.Kill(); } catch { /* ignore */ }
        lock (Running) Running.Remove(proc);
        RaiseOutput("—— 红石联机内核已停止，房间结束 ——");
    }

    /// <summary>停止所有房间（启动器退出时调用）</summary>
    public static void KillAll()
    {
        lock (Running)
        {
            foreach (var p in Running.ToList())
            {
                try { if (!p.HasExited) p.Kill(); } catch { /* ignore */ }
            }
            Running.Clear();
        }
    }
}