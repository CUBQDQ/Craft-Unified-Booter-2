// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

namespace CraftUnifiedBooter.Services;

using System.Net.Http;

/// <summary>HTTP 下载（单文件：小文件单流 + 大文件多线程 Range），带进度回调（对应原版 electron/utils/downloader.ts）</summary>
public static class Downloader
{
    private const int MultiThreadThreshold = 2 * 1024 * 1024; // 2MB 以上启用多线程
    private const int MaxThreads = 16; // 单文件最大并发线程（过多线程反而拥塞变慢）

    public static HttpClient Client { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 128,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(180) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("CraftUnifiedBooter/1.0");
        c.DefaultRequestHeaders.ConnectionClose = false;
        return c;
    }

    public static int GetThreads() =>
        Math.Clamp(SettingsStore.Current.DownloadThreads, 1, MaxThreads);

    /// <summary>下载单个文件。progress(total, downloaded) 在调用线程触发。</summary>
    public static async Task DownloadFileAsync(
        string url, string dest, long knownSize = 0,
        Action<long, long>? progress = null, CancellationToken ct = default)
    {
        var threads = GetThreads();
        if (knownSize > 0 && knownSize < MultiThreadThreshold)
        {
            await SingleDownloadAsync(url, dest, knownSize, progress, ct);
            return;
        }
        if (knownSize >= MultiThreadThreshold && threads > 1)
        {
            try { await MultiDownloadAsync(url, dest, knownSize, threads, progress, ct); return; }
            catch { CleanupTemp(dest); /* 多线程失败回退单线程 */ }
        }

        // 大小未知 → 单线程（同时获取 content-length）
        await SingleDownloadAsync(url, dest, knownSize, progress, ct);
    }

    /// <summary>多源候选下载：依次尝试候选 URL，失败自动切换下一个源（PCL2 DlSourceLoader 思路）。</summary>
    public static async Task DownloadFileWithCandidatesAsync(
        IEnumerable<string> urls, string dest, long knownSize = 0,
        Action<long, long>? progress = null, CancellationToken ct = default)
    {
        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                await DownloadFileAsync(url, dest, knownSize, progress, ct);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                lastError = ex;
                // 清理失败残留，继续尝试下一个源
            }
        }
        throw new HttpRequestException($"所有下载源均失败：{string.Join(" → ", urls)}", lastError);
    }

    /// <summary>多源候选下载字符串内容（失败自动切换下一个源）。</summary>
    public static async Task<string> DownloadStringWithCandidatesAsync(IEnumerable<string> urls, CancellationToken ct = default)
    {
        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                // 30 秒超时：JSON 等小文件卡住时快速失败，切换下一个源
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
                return await resp.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
            }
        }
        throw new HttpRequestException("所有下载源均失败", lastError);
    }

    private static void CleanupTemp(string dest)
    {
        var dir = Path.GetDirectoryName(dest);
        if (dir is null || !Directory.Exists(dir)) return;
        var baseName = Path.GetFileName(dest);
        try
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith(baseName + ".cubtmp") || name.StartsWith(baseName + ".part"))
                    File.Delete(f);
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>读取一块数据；每次读取前重置 30 秒无数据超时，连接卡住时快速失败以便切换源。</summary>
    private static async Task<int> ReadChunkAsync(Stream src, byte[] buf, CancellationTokenSource timeoutCts, CancellationToken ct)
    {
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 秒内必须读到数据，否则视为连接卡住
        try
        {
            return await src.ReadAsync(buf, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 用户主动取消时不在这里转换；只有超时才切换源
            throw new HttpRequestException("下载超时（30 秒无数据），已切换下载源");
        }
    }

    private static async Task SingleDownloadAsync(string url, string dest, long totalOverride, Action<long, long>? progress, CancellationToken ct)
    {
        Store.EnsureDir(Path.GetDirectoryName(dest)!);
        var tmp = $"{dest}.cubtmp-{Environment.ProcessId}-{DateTime.Now.Ticks}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // 响应头等待 + 每次读取均设 30 秒超时：连接卡住时快速失败，由上层切换下载源
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
            var total = totalOverride > 0 ? totalOverride : resp.Content.Headers.ContentLength ?? 0;
            // 独立作用域：必须先释放文件句柄，否则随后的重命名会触发共享冲突
            {
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true);
                var buf = new byte[256 * 1024];
                long downloaded = 0;
                int n;
                while ((n = await ReadChunkAsync(src, buf, timeoutCts, ct)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, n), ct);
                    downloaded += n;
                    progress?.Invoke(total, downloaded);
                }
                await file.FlushAsync(ct);
            }
            CommitFile(tmp, dest);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
    }

    private static async Task MultiDownloadAsync(string url, string dest, long total, int threads, Action<long, long>? progress, CancellationToken ct)
    {
        Store.EnsureDir(Path.GetDirectoryName(dest)!);
        const long pieceSize = 1024 * 1024; // 每片 1MB，线程动态领取（PCL2 碎片式调度，避免队首阻塞）
        var pieceCount = (int)Math.Ceiling(total / (double)pieceSize);
        if (pieceCount <= 1)
        {
            await SingleDownloadAsync(url, dest, total, progress, ct);
            return;
        }

        var partFiles = new string[pieceCount];
        for (var i = 0; i < pieceCount; i++) partFiles[i] = $"{dest}.part{i}";

        var nextPiece = -1;
        long totalDone = 0;
        var tasks = new Task[Math.Min(threads, pieceCount)];
        for (var t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(async () =>
            {
                // 每个线程独立超时：卡住的分片快速失败，不影响其他分片
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                while (true)
                {
                    var piece = Interlocked.Increment(ref nextPiece);
                    if (piece >= pieceCount) break;
                    var start = (long)piece * pieceSize;
                    var end = piece == pieceCount - 1 ? total - 1 : Math.Min(start + pieceSize - 1, total - 1);

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                    using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                    if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent)
                        throw new HttpRequestException($"Range request failed: {(int)resp.StatusCode}");
                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var file = new FileStream(partFiles[piece], FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true);
                    var buf = new byte[256 * 1024];
                    int n;
                    while ((n = await ReadChunkAsync(src, buf, timeoutCts, ct)) > 0)
                    {
                        await file.WriteAsync(buf.AsMemory(0, n), ct);
                        var done = Interlocked.Add(ref totalDone, n);
                        progress?.Invoke(total, done);
                    }
                }
            }, ct);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            foreach (var p in partFiles) { try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ } }
            throw;
        }

        // 流式按序合并分片 → 原子替换目标
        var tmp = $"{dest}.cubtmp-{Environment.ProcessId}-{DateTime.Now.Ticks}";
        try
        {
            // 独立作用域：必须先释放文件句柄，否则随后的重命名会触发共享冲突
            {
                await using var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                for (var i = 0; i < pieceCount; i++)
                {
                    await using var ins = new FileStream(partFiles[i], FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
                    await ins.CopyToAsync(outStream, ct);
                }
                await outStream.FlushAsync(ct);
            }
            CommitFile(tmp, dest);
        }
        finally
        {
            foreach (var p in partFiles) { try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ } }
        }
    }

    /// <summary>原子替换：失败时删除旧目标再重试一次（不误删用户文件的兜底）</summary>
    private static void CommitFile(string tmp, string dest)
    {
        try { File.Move(tmp, dest, overwrite: true); return; }
        catch { /* 目标被占用则删除重试 */ }
        try
        {
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
    }
}
