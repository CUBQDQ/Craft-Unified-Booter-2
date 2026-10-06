// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Collections.Concurrent;

namespace CraftUnifiedBooter.Services;

/// <summary>下载任务管理（任务表 + 进度/完成事件 + 暂停/继续/取消/优先级）</summary>
public static class DownloadManager
{
    public static readonly ConcurrentDictionary<string, DownloadTask> Tasks = new();

    /// <summary>进度事件</summary>
    public static event Action<DownloadTask>? Progress;
    /// <summary>完成事件（成功或失败）</summary>
    public static event Action<DownloadTask>? Completed;
    public static event Action? QueueChanged;

    public static DownloadTask? GetTask(string id) => Tasks.TryGetValue(id, out var t) ? t : null;
    public static List<DownloadTask> GetActiveTasks() => Tasks.Values.Where(t => !t.Done).OrderByDescending(t => t.Priority).ThenBy(t => t.Id).ToList();
    public static List<DownloadTask> GetHistory() => Tasks.Values.Where(t => t.Done).OrderByDescending(t => t.Id).ToList();
    public static int ActiveCount => Tasks.Values.Count(t => !t.Done);
    public static bool HasActive => Tasks.Values.Any(t => !t.Done);

    /// <summary>创建或复用任务（幂等），重置状态并创建取消令牌源</summary>
    public static DownloadTask Begin(string id, string name, string kind = "版本")
    {
        var task = Tasks.GetOrAdd(id, _ => new DownloadTask { Id = id, Name = name, Kind = kind });
        task.Name = name;
        task.Kind = kind;
        task.Total = 0;
        task.Downloaded = 0;
        task.Percent = 0;
        task.Done = false;
        task.Success = false;
        task.Error = null;
        task.TotalFiles = 0;
        task.DownloadedFiles = 0;
        task.Status = DownloadStatus.Queued;
        task.PauseRequested = false;
        task.Cts = new CancellationTokenSource();
        QueueChanged?.Invoke();
        return task;
    }

    // ===================== 暂停 / 继续 / 取消 / 优先级 =====================

    public static void Pause(string id)
    {
        if (!Tasks.TryGetValue(id, out var t)) return;
        t.PauseRequested = true;
        if (t.Status is DownloadStatus.Downloading or DownloadStatus.Queued)
            t.Status = DownloadStatus.Paused;
        QueueChanged?.Invoke();
    }

    public static void Resume(string id)
    {
        if (!Tasks.TryGetValue(id, out var t)) return;
        t.PauseRequested = false;
        if (t.Status == DownloadStatus.Paused)
            t.Status = DownloadStatus.Queued;
        QueueChanged?.Invoke();
    }

    public static void Cancel(string id)
    {
        if (!Tasks.TryGetValue(id, out var t)) return;
        t.Cts?.Cancel();
        t.PauseRequested = false;
        t.Done = true;
        t.Success = false;
        t.Status = DownloadStatus.Canceled;
        t.Error = "已取消";
        Progress?.Invoke(t);
        Completed?.Invoke(t);
        QueueChanged?.Invoke();
    }

    public static void SetPriority(string id, int priority)
    {
        if (!Tasks.TryGetValue(id, out var t)) return;
        t.Priority = Math.Clamp(priority, 0, 10);
        QueueChanged?.Invoke();
    }

    // ===================== 进度 / 完成 =====================

    private static readonly ConcurrentDictionary<string, long> _lastReportTick = new();

    public static void Report(string id, long total, long downloaded)
    // 返回 void；进度事件 200ms 节流
    {
        if (!Tasks.TryGetValue(id, out var t)) return;
        if (downloaded > t.Downloaded) t.Downloaded = downloaded;
        if (total > t.Total) t.Total = total;
        t.Percent = t.Total > 0 ? (int)Math.Round(t.Downloaded * 100.0 / t.Total) : 0;
        if (t.Status is DownloadStatus.Queued) t.Status = DownloadStatus.Downloading;
        var now = Environment.TickCount64;
        var last = _lastReportTick.GetOrAdd(id, 0);
        if (now - last >= 200)
        {
            _lastReportTick[id] = now;
            Progress?.Invoke(t);
        }
    }

    public static void Complete(string id, bool success, string? error = null)
    {
        if (!Tasks.TryGetValue(id, out var t)) return;
        t.Done = true;
        t.Success = success;
        t.Error = error;
        t.Status = success ? DownloadStatus.Completed : DownloadStatus.Failed;
        t.Percent = success ? 100 : t.Percent;
        t.Cts?.Dispose();
        t.Cts = null;
        Progress?.Invoke(t);
        Completed?.Invoke(t);
        QueueChanged?.Invoke();
    }

    public static void Remove(string id)
    {
        if (Tasks.TryRemove(id, out var t))
        {
            _lastReportTick.TryRemove(id, out _);
            t.Cts?.Dispose();
            QueueChanged?.Invoke();
        }
    }

    public static void ClearHistory()
    {
        foreach (var kv in Tasks.Where(kv => kv.Value.Done))
        {
            Tasks.TryRemove(kv.Key, out _);
            _lastReportTick.TryRemove(kv.Key, out _);
        }
        QueueChanged?.Invoke();
    }

    // ===================== 下载执行（支持暂停等待 / 取消） =====================

    /// <summary>把一组文件排队下载，累计进度汇报到 taskId。支持暂停（Pause/Resume）与取消（Cancel）。</summary>
    public static async Task<(int Failed, string? FirstError)> DownloadFilesAsync(
        string taskId,
        IEnumerable<(string[] Urls, string Path, long Size)> files,
        Action<long, long>? onBatchProgress = null,
        CancellationToken ct = default)
    {
        var list = files.Where(f => !File.Exists(f.Path)).ToList();
        var totalAll = list.Sum(f => f.Size);
        long baseDownloaded = 0;
        int failed = 0;
        string? firstError = null;

        if (Tasks.TryGetValue(taskId, out var taskInfo))
            taskInfo.TotalFiles = list.Count;

        var large = list.Where(f => f.Size >= 2 * 1024 * 1024).ToList();
        var small = list.Where(f => f.Size < 2 * 1024 * 1024).ToList();

        // 暂停等待：任务被暂停时循环等待，直到恢复或取消
        async Task WaitIfPaused()
        {
            while (taskInfo is { PauseRequested: true })
            {
                await Task.Delay(200, ct);
            }
        }

        void CountOneDone()
        {
            if (Tasks.TryGetValue(taskId, out var t))
            {
                lock (t)
                {
                    t.DownloadedFiles++;
                }
            }
        }

        try
        {
            for (int i = 0; i < large.Count; i += 2)
            {
                await WaitIfPaused();
                ct.ThrowIfCancellationRequested();

                var batch = large.Skip(i).Take(2).ToList();
                var batchBase = baseDownloaded;
                var offsets = new long[batch.Count];
                for (int j = 1; j < batch.Count; j++) offsets[j] = offsets[j - 1] + batch[j - 1].Size;

                await Task.WhenAll(batch.Select(async (f, idx) =>
                {
                    try
                    {
                        await Downloader.DownloadFileWithCandidatesAsync(f.Urls, f.Path, f.Size,
                            (total, downloaded) =>
                            {
                                var acc = batchBase + offsets[idx] + downloaded;
                                Report(taskId, totalAll, acc);
                                onBatchProgress?.Invoke(totalAll, acc);
                            }, ct);
                        Interlocked.Add(ref baseDownloaded, f.Size);
                        Report(taskId, totalAll, baseDownloaded);
                        onBatchProgress?.Invoke(totalAll, baseDownloaded);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        firstError ??= ex.Message;
                        Interlocked.Add(ref baseDownloaded, f.Size);
                        Report(taskId, totalAll, baseDownloaded);
                        onBatchProgress?.Invoke(totalAll, baseDownloaded);
                    }
                    finally
                    {
                        CountOneDone();
                    }
                }));
            }

            for (int i = 0; i < small.Count; i += 16)
            {
                await WaitIfPaused();
                ct.ThrowIfCancellationRequested();

                var batch = small.Skip(i).Take(16).ToList();
                await Task.WhenAll(batch.Select(async f =>
                {
                    try
                    {
                        await Downloader.DownloadFileWithCandidatesAsync(f.Urls, f.Path, f.Size, null, ct);
                        Interlocked.Add(ref baseDownloaded, f.Size);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        firstError ??= ex.Message;
                        Interlocked.Add(ref baseDownloaded, f.Size);
                    }
                    finally
                    {
                        CountOneDone();
                    }
                }));
                Report(taskId, totalAll, baseDownloaded);
                onBatchProgress?.Invoke(totalAll, baseDownloaded);
            }
        }
        catch (OperationCanceledException)
        {
            // 取消：任务状态由 Cancel() 设置为 Canceled，这里直接抛出由调用者处理
            throw;
        }

        return (failed, firstError);
    }
}