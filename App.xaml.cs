// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CraftUnifiedBooter.Services;

namespace CraftUnifiedBooter;

public partial class App : Application
{
    /// <summary>第二实例发送给主窗口的自定义激活消息（重复启动时触发彩虹光效+置顶）。</summary>
    internal const int WM_CUB_ACTIVATE = 0x9001;
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
    // 锁文件固定放在用户 AppData（真实桌面下所有实例路径一致），目录名/文件名加密存储
    private static readonly string LockPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Protect.D("kzu61AsMRsmqkZPyQYYl3pMd"), Protect.D("x6CYlrJxniTRiC0="));
    private static Mutex? _singleInstanceMutex;
    private FileStream? _singleInstanceLock;

    protected override void OnStartup(StartupEventArgs e)
    {
#if !DEBUG
        // 反调试（P1）：多手段检测调试器。检测到后静默退出，不弹窗（弹窗等于告诉攻击者“这里有检测”）
        if (DetectDebugger())
        {
            Shutdown();
            return;
        }
#endif

        // 单实例：命名互斥体 + 文件独占锁双重保障（Windows 真实桌面环境可靠）
        _singleInstanceMutex = new Mutex(true, Protect.D("O63VBTpRwoqSlvt6mxDgkzu61AsMRsmqkZPyQYYl3pMd"), out var createdNew);
        var mutexFree = createdNew;
        if (mutexFree) GC.KeepAlive(_singleInstanceMutex);

        var fileLockOk = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LockPath)!);
            _singleInstanceLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            fileLockOk = true;
        }
        catch { fileLockOk = false; }

        if (!mutexFree || !fileLockOk)
        {
            // 已有实例：通知第一个实例在右上角提示并置顶窗口，然后静默退出（不用原生弹窗）
            NotifyExistingInstance();
            Shutdown();
            return;
        }

        // 作为第一个实例：无需额外监听，第二实例会直接向本窗口发送激活消息
        // 全局异常捕获：把异常写入 crash.log，便于定位崩溃根因
        DispatcherUnhandledException += (s, args) =>
        {
            AppendLog($"[DispatcherUnhandledException {DateTime.Now:HH:mm:ss}]\n{args.Exception}\n");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            AppendLog($"[UnhandledException {DateTime.Now:HH:mm:ss}]\n{args.ExceptionObject}\n");
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            AppendLog($"[TaskException {DateTime.Now:HH:mm:ss}]\n{args.Exception}\n");
            args.SetObserved();
        };
        base.OnStartup(e);

        // 首次启动：CUB/skins 已在 Store 初始化时自动创建，这里后台补齐官方原版史蒂夫/艾利克斯皮肤
        _ = Task.Run(SkinService.EnsureDefaultsAsync);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 退出时结束所有红石联机房间（内核进程）
        HongshiService.KillAll();
        base.OnExit(e);
    }

    /// <summary>多手段调试器检测：托管检测 + 原生 API + 时间差 + 调试工具进程。任一命中视为被调试。</summary>
    private static bool DetectDebugger()
    {
        try
        {
            // 1. 托管检测
            if (Debugger.IsAttached) return true;

            // 2. 原生 API 检测
            if (IsDebuggerPresent()) return true;
            bool isDebugged = false;
            if (CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isDebugged) && isDebugged) return true;

            // 3. 时间差检测：调试器单步会显著拖慢一段固定计算
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 200000; i++)
                _ = Math.Sqrt(i);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 800) return true;

            // 4. 检测常见调试/逆向工具进程
            var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "dnspy", "x64dbg", "x32dbg", "ollydbg", "ida", "id64",
                "windbg", "cheatengine", "procexp", "processhacker", "fiddler",
            };
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (bad.Contains(p.ProcessName)) return true;
                }
                catch { /* 忽略无权限的进程 */ }
            }
        }
        catch { /* 检测失败不阻止运行 */ }
        return false;
    }

    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll")]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebugged);

    /// <summary>通知已运行的实例：发送窗口激活消息，触发主窗口边缘彩虹光效并置顶。</summary>
    private static void NotifyExistingInstance()
    {
        try
        {
            var hwnd = FindWindow(null, "Craft Unified Booter");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                // 发送自定义消息，主窗口收到后显示彩虹光效并再次置顶
                SendMessage(hwnd, WM_CUB_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { /* 忽略 */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int SW_RESTORE = 9;

    private static void AppendLog(string msg)
    {
        try { File.AppendAllText(LogPath, msg); } catch { /* 写入失败忽略 */ }
    }
}
