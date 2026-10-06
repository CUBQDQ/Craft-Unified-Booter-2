// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CraftUnifiedBooter.Services;

/// <summary>游戏启动（classpath 构建 / native 提取 / 参数组装 / 进程管理，对应原版 electron/ipc/launcher.ts）</summary>
public static class LaunchService
{
    public class LaunchProgress
    {
        public string Stage { get; set; } = "";      // prepareJava / prepareFiles / launching
        public string Message { get; set; } = "";
        public int Percent { get; set; }
        public int OverallPercent { get; set; }
        public bool? Success { get; set; }
    }

    public static event Action<LaunchProgress>? Progress;
    public static event Action<string>? GameOutput;
    public static event Action<string, string, int>? GameExit;   // procId, versionId, code

    private static readonly ConcurrentDictionary<string, Process> RunningGames = new();

    // 自动修复重试计数：同一版本最多自动修复并重启 2 次，避免无限循环
    private static readonly ConcurrentDictionary<string, int> RepairAttempts = new();
    private const int MaxRepairAttempts = 2;

  public static bool IsGameRunning => RunningGames.Count > 0;
    public static IReadOnlyCollection<string> RunningProcIds => (IReadOnlyCollection<string>)RunningGames.Keys;

    /// <summary>当前运行中的游戏进程列表（进程 ID、版本 ID、系统进程号）。</summary>
    public static List<(string ProcId, string VersionId, int ProcessId, bool Running)> GetRunningGames()
    {
        var list = new List<(string ProcId, string VersionId, int ProcessId, bool Running)>();
        foreach (var kv in RunningGames)
        {
            var versionId = kv.Key;
            var dash = kv.Key.LastIndexOf('-');
            if (dash > 0) versionId = kv.Key[..dash];
            int pid = 0;
            try { if (!kv.Value.HasExited) pid = kv.Value.Id; } catch { /* 进程已退出 */ }
            list.Add((kv.Key, versionId, pid, pid != 0));
        }
        return list.OrderBy(x => x.VersionId).ToList();
    }

    /// <summary>结束指定游戏进程（按 procId）。</summary>
    public static void Kill(string procId)
    {
        if (RunningGames.TryGetValue(procId, out var p))
        {
            try { if (!p.HasExited) p.Kill(); } catch { /* 进程已退出 */ }
            RunningGames.TryRemove(procId, out _);
        }
    }

    public static void KillAll()
    {
        foreach (var p in RunningGames.Values)
        {
            try { if (!p.HasExited) p.Kill(); } catch { /* ignore */ }
        }
        RunningGames.Clear();
    }

    // ===================== 版本 JSON 递归合并（inheritsFrom） =====================
    private static VersionDetail LoadVersionWithInheritance(string versionId)
    {
        var jsonPath = Path.Combine(Store.GetVersionsDir(), versionId, $"{versionId}.json");
        if (!File.Exists(jsonPath)) throw new Exception($"版本 {versionId} 未安装");
        var data = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath), Store.JsonReadOpts)!;

        if (!string.IsNullOrEmpty(data.InheritsFrom))
        {
            VersionDetail? parent = null;
            try { parent = LoadVersionWithInheritance(data.InheritsFrom!); }
            catch { /* 父版本不存在时仍继续 */ }

            if (parent != null)
            {
                var childArtifacts = new HashSet<string>((data.Libraries ?? new()).Select(l =>
                {
                    var p = l.Name.Split(':');
                    return p.Length >= 2 ? $"{p[0]}:{p[1]}" : l.Name;
                }));
                var childNames = new HashSet<string>((data.Libraries ?? new()).Select(l => l.Name));
                var merged = new List<VersionLibrary>(data.Libraries ?? new());
                foreach (var pl in parent.Libraries ?? new())
                {
                    if (childNames.Contains(pl.Name)) continue;
                    var p = pl.Name.Split(':');
                    var ga = p.Length >= 2 ? $"{p[0]}:{p[1]}" : pl.Name;
                    if (childArtifacts.Contains(ga)) continue;
                    merged.Add(pl);
                }
                data.Libraries = merged;

                data.AssetIndex ??= parent.AssetIndex;
                data.Downloads ??= parent.Downloads;
                data.Type ??= parent.Type;
                data.ReleaseTime ??= parent.ReleaseTime;
                data.MainClass ??= parent.MainClass;
                data.MinecraftArguments ??= parent.MinecraftArguments;

                if (parent.Arguments != null)
                {
                    data.Arguments ??= new VersionArguments();
                    data.Arguments.Jvm = (data.Arguments.Jvm ?? new()).Concat(parent.Arguments.Jvm ?? new()).ToList();
                    data.Arguments.Game = (data.Arguments.Game ?? new()).Concat(parent.Arguments.Game ?? new()).ToList();
                }
                if (parent.Natives != null)
                {
                    data.Natives ??= new();
                    foreach (var kv in parent.Natives)
                        if (!data.Natives.ContainsKey(kv.Key)) data.Natives[kv.Key] = kv.Value;
                }
            }
        }
        return data;
    }

    private static string MavenNameToPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return "";
        var group = parts[0].Replace('.', '/');
        var classifier = parts.Length > 3 ? parts[3] : "";
        var fileName = string.IsNullOrEmpty(classifier) ? $"{parts[1]}-{parts[2]}.jar" : $"{parts[1]}-{parts[2]}-{classifier}.jar";
        return $"{group}/{parts[1]}/{parts[2]}/{fileName}";
    }

    private static string GetMcOsName() => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "osx" : "linux";

    private static string GetMcArch() => Environment.Is64BitOperatingSystem ? "x86_64" : "x86";

    private static bool CheckLibRules(List<VersionRule>? rules)
    {
        if (rules == null || rules.Count == 0) return true;
        var osName = GetMcOsName();
        var arch = GetMcArch();
        var allowed = false;
        foreach (var rule in rules)
        {
            var osMatch = (rule.Os?.Name == null || rule.Os.Name == osName)
                       && (rule.Os?.Arch == null || rule.Os.Arch == arch);
            if (osMatch) allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private static bool CheckArgRules(VersionRule[] rules)
    {
        var osName = GetMcOsName();
        var arch = GetMcArch();
        foreach (var rule in rules)
        {
            if (rule.Action == "allow")
            {
                if (rule.Os?.Name != null && rule.Os.Name != osName) return false;
                if (rule.Os?.Arch != null && rule.Os.Arch != arch) return false;
                if (rule.Features?.IsDemoUser == true) return false;
                if (rule.Features?.IsQuickPlay == true) return false;
            }
            else if (rule.Action == "disallow")
            {
                if (rule.Os?.Name == osName) return false;
                if (rule.Os?.Arch == arch) return false;
            }
        }
        return true;
    }

    private static void ExtractNativeJar(string jarPath, string destDir)
    {
        try
        {
            ZipFile.ExtractToDirectory(jarPath, destDir, overwriteFiles: true);
        }
        catch { /* 部分 native jar 非标准 zip，忽略 */ }
    }

    // ===================== 启动主入口 =====================
    public static async Task<(bool Success, string? Error)> LaunchAsync(string versionId)
    {
        var settings = SettingsStore.Current;

        // ===== Stage 1: 准备 Java =====
        var javaReq = JavaService.GetJavaRequirement(versionId);
        var requiredVersion = javaReq.Min;
        Emit(new LaunchProgress { Stage = "prepareJava", Message = $"检查 Java {requiredVersion}+ …", OverallPercent = 5 });

        var javaInfo = JavaService.SelectJavaForVersion(versionId);
        var missingJava = settings.AutoJava &&
            !(javaInfo.MajorVersion >= javaReq.Min && javaInfo.MajorVersion <= javaReq.Max &&
              javaInfo.Path != "java" && javaInfo.Version != "unknown");

        string javaPath;
        if (settings.AutoJava)
        {
            if (missingJava)
            {
                Emit(new LaunchProgress { Stage = "prepareJava", Message = $"系统未安装 Java {requiredVersion}+，正在下载便携版 JRE…", OverallPercent = 10 });
                javaPath = await JavaService.EnsureJavaAsync(requiredVersion,
                    (msg, pct) => Emit(new LaunchProgress { Stage = "prepareJava", Message = msg, OverallPercent = 10 + pct / 5 }));
                javaInfo = new JavaInfo { Path = javaPath, Version = $"Java {requiredVersion}", MajorVersion = requiredVersion, IsValid = true };
            }
            else javaPath = javaInfo.Path;
        }
        else
        {
            javaPath = string.IsNullOrEmpty(settings.JavaPath) ? JavaService.AutoDetectJavaPath() : settings.JavaPath;
            // 手动指定 Java 时重新检测实际版本，避免 javaInfo 与实际路径不一致导致 JVM 参数过滤错误
            javaInfo = JavaService.GetJavaInfo(javaPath);
        }
        Emit(new LaunchProgress { Stage = "prepareJava", Message = $"Java {javaInfo.Version} 已就绪", OverallPercent = 25 });

        // ===== Stage 2: 补全文件 =====
        Emit(new LaunchProgress { Stage = "prepareFiles", Message = "准备游戏目录…", Percent = 10, OverallPercent = 35 });

        var versionJsonPath = Path.Combine(Store.GetVersionsDir(), versionId, $"{versionId}.json");
        if (!File.Exists(versionJsonPath)) return (false, $"版本 {versionId} 未安装");
        var versionData = LoadVersionWithInheritance(versionId);
        var versionDir = Path.Combine(Store.GetVersionsDir(), versionId);
        var gameDir = Store.ResolveGamePath("root", versionId);
        Store.EnsureDir(gameDir);
        foreach (var sub in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "screenshots", "logs", "config", "datapacks" })
            Store.EnsureDir(Store.ResolveGamePath(sub, versionId));

        Emit(new LaunchProgress { Stage = "prepareFiles", Message = "检查依赖库与运行库…", Percent = 50, OverallPercent = 55 });

        var (autoMin, autoMax) = settings.AutoMemory ? JavaService.AutoAllocateMemory() : (settings.MinMemory, settings.MaxMemory);
        var minMemory = settings.AutoMemory ? autoMin : settings.MinMemory;
        var maxMemory = settings.AutoMemory ? autoMax : settings.MaxMemory;

        // ===== classpath =====
        var cpParts = new List<string>();
        var versionJar = Path.Combine(versionDir, $"{versionId}.jar");
        if (!File.Exists(versionJar) && versionData.InheritsFrom != null)
        {
            var parentJar = Path.Combine(Store.GetVersionsDir(), versionData.InheritsFrom, $"{versionData.InheritsFrom}.jar");
            if (File.Exists(parentJar)) versionJar = parentJar;
        }
        if (File.Exists(versionJar)) cpParts.Add(versionJar);

        foreach (var lib in versionData.Libraries ?? new())
        {
            if (!CheckLibRules(lib.Rules)) continue;
            if (lib.Downloads?.Artifact != null)
            {
                var libPath = Path.Combine(Store.GetLibrariesDir(), lib.Downloads.Artifact.Path);
                if (File.Exists(libPath)) cpParts.Add(libPath);
            }
            else if (!string.IsNullOrEmpty(lib.Name))
            {
                var rel = MavenNameToPath(lib.Name);
                if (!string.IsNullOrEmpty(rel))
                {
                    var libPath = Path.Combine(Store.GetLibrariesDir(), rel);
                    if (File.Exists(libPath)) cpParts.Add(libPath);
                }
            }
        }
        // classpath 去重
        var seenCp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueCp = cpParts.Where(p => seenCp.Add(p)).ToList();
        cpParts = uniqueCp;

        // ===== native 提取 =====
        var nativesDir = Path.Combine(versionDir, "natives");
        Store.EnsureDir(nativesDir);
        var existingNatives = Directory.GetFiles(nativesDir).Where(f => f.EndsWith(".dll") || f.EndsWith(".so") || f.EndsWith(".dylib")).ToList();
        if (existingNatives.Count == 0)
        {
            var arch = Environment.Is64BitOperatingSystem ? "64" : "32";
            var osKey = GetMcOsName();
            foreach (var lib in versionData.Libraries ?? new())
            {
                if (lib.Natives == null) continue;
                if (!CheckLibRules(lib.Rules)) continue;
                if (!lib.Natives.TryGetValue(osKey, out var raw)) continue;
                var classifierName = raw.Replace("${arch}", arch);
                if (lib.Downloads?.Classifiers?.TryGetValue(classifierName, out var cls) != true) continue;
                var nativeJar = Path.Combine(Store.GetLibrariesDir(), cls.Path);
                if (File.Exists(nativeJar)) ExtractNativeJar(nativeJar, nativesDir);
            }
            var metaDir = Path.Combine(nativesDir, "META-INF");
            if (Directory.Exists(metaDir)) Directory.Delete(metaDir, true);
        }

        // ===== 启动前补全缺失的依赖库（避免 NoClassDefFoundError 导致游戏窗口不出现）=====
        var source = await Mirror.ResolveDownloadSourceAsync();
        var missingLibs = new List<(string Url, string Path, long Size)>();
        foreach (var lib in versionData.Libraries ?? new())
        {
            if (!CheckLibRules(lib.Rules)) continue;
            string? libPath = null;
            string? url = null;
            long size = 0;
            if (lib.Downloads?.Artifact != null)
            {
                libPath = Path.Combine(Store.GetLibrariesDir(), lib.Downloads.Artifact.Path);
                url = lib.Downloads.Artifact.Url;
                size = lib.Downloads.Artifact.Size;
            }
            else if (!string.IsNullOrEmpty(lib.Name))
            {
                var rel = MavenNameToPath(lib.Name);
                if (!string.IsNullOrEmpty(rel))
                {
                    libPath = Path.Combine(Store.GetLibrariesDir(), rel);
                    if (!string.IsNullOrEmpty(lib.Url)) url = $"{lib.Url.TrimEnd('/')}/{rel}";
                    else url ??= $"https://libraries.minecraft.net/{rel}";
                }
            }
            if (!string.IsNullOrEmpty(libPath) && !string.IsNullOrEmpty(url) && !File.Exists(libPath))
                missingLibs.Add((url, libPath, size));
        }
        if (missingLibs.Count > 0)
        {
            Emit(new LaunchProgress { Stage = "prepareFiles", Message = $"补全 {missingLibs.Count} 个缺失依赖库…", Percent = 55, OverallPercent = 55 });
            foreach (var m in missingLibs)
            {
                try { await Downloader.DownloadFileWithCandidatesAsync(Mirror.CandidateUrls(m.Url, source), m.Path, m.Size); }
                catch { /* 下载失败不阻断启动，游戏可能报错但至少尝试 */ }
            }
        }

        // ===== JVM 参数 =====
        var jvmArgs = new List<string>
        {
            $"-Xms{minMemory}M", $"-Xmx{maxMemory}M",
            $"-Djava.library.path={nativesDir}",
            "-Dminecraft.launcher.brand=CUB",
            "-Dminecraft.launcher.version=1.0.0",
            "-Dfile.encoding=UTF-8",
            "-Dsun.stdout.encoding=UTF-8",
            "-Dsun.stderr.encoding=UTF-8",
        };
        var customJvmArgs = (settings.JvmArgs ?? "").Trim();
        if (!string.IsNullOrEmpty(customJvmArgs))
            jvmArgs.AddRange(customJvmArgs.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

        var classpathSep = OperatingSystem.IsWindows() ? ";" : ":";
        var isModern = string.IsNullOrEmpty(versionData.MinecraftArguments) && versionData.Arguments != null;
        var javaMajor = javaInfo.MajorVersion;

        if (isModern && versionData.Arguments?.Jvm != null)
        {
            // JVM 占位符替换（含启动器品牌/版本）
            string ReplaceJvm(string raw) => raw
                .Replace("${classpath}", string.Join(classpathSep, cpParts))
                .Replace("${natives_directory}", nativesDir)
                .Replace("${library_directory}", Store.GetLibrariesDir())
                .Replace("${classpath_separator}", classpathSep)
                .Replace("${version_name}", versionId)
                .Replace("${launcher_name}", "CUB")
                .Replace("${launcher_version}", "1.0.0");

            foreach (var argEl in versionData.Arguments.Jvm)
            {
                if (argEl.ValueKind == JsonValueKind.String)
                {
                    var processed = ReplaceJvm(argEl.GetString()!);
                    if (processed.StartsWith("-D") && Regex.IsMatch(processed.Trim(), @"\s")) continue;
                    if (processed.StartsWith("--sun-misc-unsafe-memory-access") && javaMajor < 23) continue;
                    if (processed.StartsWith("--enable-native-access") && javaMajor < 17) continue;
                    jvmArgs.Add(processed);
                }
                else if (argEl.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        // 规则对象结构：{ "rules": [...], "value": ... }，先读取 rules 数组判断是否放行
                        if (!argEl.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
                            continue;
                        var rules = rulesEl.Deserialize<List<VersionRule>>(Store.JsonReadOpts) ?? new();
                        if (rules.Count == 0 || !CheckArgRules(rules.ToArray())) continue;
                        if (argEl.TryGetProperty("value", out var value))
                        {
                            if (value.ValueKind == JsonValueKind.Array)
                                foreach (var v in value.EnumerateArray()) jvmArgs.Add(ReplaceJvm(v.GetString()!));
                            else if (value.ValueKind == JsonValueKind.String) jvmArgs.Add(ReplaceJvm(value.GetString()!));
                        }
                    }
                    catch { /* ignore */ }
                }
            }
        }

        if (!jvmArgs.Any(a => a == "-cp" || a.StartsWith("-cp")))
            jvmArgs.AddRange(new[] { "-cp", string.Join(classpathSep, cpParts) });

        // ===== 账户 =====
        var selectedAccount = AccountsStore.GetSelected();
        if (selectedAccount?.Type == "microsoft")
        {
            Emit(new LaunchProgress { Stage = "prepareFiles", Message = "正在刷新微软账户令牌…", Percent = 60, OverallPercent = 60 });
            await MicrosoftAuthService.EnsureValidAsync(selectedAccount);
        }
        var username = selectedAccount?.Name ?? "Player";
        var uuid = selectedAccount?.Uuid ?? AccountsStore.NewUuid();
        var isOffline = selectedAccount == null || selectedAccount.Type == "offline";
        var accessToken = isOffline ? AccountsStore.NewUuid() : selectedAccount?.AccessToken ?? "0";
        var userType = selectedAccount?.Type == "microsoft" ? "msa" : "legacy";

        string ReplaceArg(string raw) => raw
            .Replace("${auth_player_name}", username)
            .Replace("${version_name}", versionId)
            .Replace("${game_directory}", gameDir)
            .Replace("${assets_root}", Store.GetAssetsDir())
            .Replace("${assets_index_name}", versionData.AssetIndex?.Id ?? versionId)
            .Replace("${auth_uuid}", uuid)
            .Replace("${auth_access_token}", accessToken)
            .Replace("${user_type}", userType)
            .Replace("${version_type}", versionData.Type ?? "release")
            .Replace("${resolution_width}", settings.Width.ToString())
            .Replace("${resolution_height}", settings.Height.ToString())
            .Replace("${clientid}", userType == "msa" ? "00000000402b5328" : "")
            .Replace("${auth_xuid}", userType == "msa" ? selectedAccount?.Xuid ?? "" : "")
            .Replace("${launcher_name}", "CUB")
            .Replace("${launcher_version}", "1.0.0")
            .Replace("${quickPlayPath}", "")
            .Replace("${quickPlaySingleplayer}", "")
            .Replace("${quickPlayMultiplayer}", "")
            .Replace("${quickPlayRealms}", "");

        var quickPlayFlags = new HashSet<string> { "--quickPlayPath", "--quickPlaySingleplayer", "--quickPlayMultiplayer", "--quickPlayRealms" };
        var gameArgs = new List<string>();

        void PushFiltered(List<string> flat)
        {
            for (int i = 0; i < flat.Count; i++)
            {
                var tok = flat[i];
                if (quickPlayFlags.Contains(tok) || tok == "--clientId" || tok == "--xuid")
                {
                    var nextVal = i + 1 < flat.Count ? flat[i + 1] : null;
                    if (nextVal == null || nextVal == "" || nextVal.StartsWith("--"))
                    {
                        if (nextVal != null && !nextVal.StartsWith("--")) i += 1;
                        continue;
                    }
                    gameArgs.Add(tok);
                    gameArgs.Add(nextVal);
                    i += 1;
                    continue;
                }
                if (string.IsNullOrEmpty(tok)) continue;
                gameArgs.Add(tok);
            }
        }

        if (isModern && versionData.Arguments?.Game != null)
        {
            var raw = new List<string>();
            foreach (var argEl in versionData.Arguments.Game)
            {
                if (argEl.ValueKind == JsonValueKind.String)
                {
                    raw.Add(ReplaceArg(argEl.GetString()!));
                }
                else if (argEl.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        // 规则对象结构：{ "rules": [...], "value": ... }，先读取 rules 数组判断是否放行
                        if (!argEl.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
                            continue;
                        var rules = rulesEl.Deserialize<List<VersionRule>>(Store.JsonReadOpts) ?? new();
                        if (rules.Count == 0 || !CheckArgRules(rules.ToArray())) continue;
                        if (argEl.TryGetProperty("value", out var value))
                        {
                            if (value.ValueKind == JsonValueKind.Array)
                                foreach (var v in value.EnumerateArray()) raw.Add(ReplaceArg(v.GetString()!));
                            else if (value.ValueKind == JsonValueKind.String) raw.Add(ReplaceArg(value.GetString()!));
                        }
                    }
                    catch { /* ignore */ }
                }
            }
            PushFiltered(raw);
        }
        else if (!string.IsNullOrEmpty(versionData.MinecraftArguments))
        {
            PushFiltered(ReplaceArg(versionData.MinecraftArguments).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList());
        }
        else
        {
            PushFiltered(new List<string>
            {
                "--username", username, "--version", versionId, "--gameDir", gameDir,
                "--assetsDir", Store.GetAssetsDir(), "--assetIndex", versionData.AssetIndex?.Id ?? versionId,
                "--accessToken", accessToken, "--uuid", uuid, "--userType", userType,
            });
        }

        if (settings.Fullscreen) gameArgs.Add("--fullscreen");
        gameArgs.Remove("--demo");

        // ===== options.txt：中文 + 跳过无障碍引导 =====
        var optionsPath = Path.Combine(gameDir, "options.txt");
        try
        {
            var content = File.Exists(optionsPath) ? File.ReadAllText(optionsPath) : "";
            var lines = new List<string>();
            if (!Regex.IsMatch(content, @"^lang:", RegexOptions.Multiline)) lines.Add("lang:zh_cn");
            if (!Regex.IsMatch(content, @"^onboardAccessibility:", RegexOptions.Multiline)) lines.Add("onboardAccessibility:false");
            else content = Regex.Replace(content, @"^onboardAccessibility:true", "onboardAccessibility:false", RegexOptions.Multiline);
            if (lines.Count > 0)
            {
                content = content.TrimEnd() + "\n" + string.Join("\n", lines) + "\n";
                File.WriteAllText(optionsPath, content);
            }
        }
        catch { /* ignore */ }

        // ===== 启动进程 =====
        var mainClass = versionData.MainClass ?? "net.minecraft.client.main.Main";
        var allArgs = jvmArgs.Concat(new[] { mainClass }).Concat(gameArgs).ToArray();

        Emit(new LaunchProgress { Stage = "launching", Message = $"启动游戏进程（{username}）…", Percent = 0, OverallPercent = 85 });

        var procId = $"{versionId}-{DateTime.Now.Ticks}";

        // PCL2 启动方式：优先使用 javaw.exe（无控制台黑窗）
        var javaBinDir = Path.GetDirectoryName(javaPath);
        var javaExe = javaPath;
        if (!string.IsNullOrEmpty(javaBinDir))
        {
            var javawPath = Path.Combine(javaBinDir, "javaw.exe");
            if (File.Exists(javawPath)) javaExe = javawPath;
        }

        var psi = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // 将 Java 的 bin 目录加入子进程 PATH（PCL2 McLaunchRun 做法）
        if (!string.IsNullOrEmpty(javaBinDir))
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!path.Contains(javaBinDir, StringComparison.OrdinalIgnoreCase))
                psi.Environment["PATH"] = javaBinDir + ";" + path;
        }
        foreach (var a in allArgs) psi.ArgumentList.Add(a);

        Process proc;
        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Start();
        }
        catch (Exception ex)
        {
            Emit(new LaunchProgress { Stage = "launching", Message = $"启动失败：{ex.Message}", Percent = 100, OverallPercent = 100, Success = false });
            return (false, $"无法启动进程：{javaPath} — {ex.Message}");
        }

        RunningGames[procId] = proc;

        // 日志文件
        var logsDir = Store.ResolveGamePath("logs", versionId);
        Store.EnsureDir(logsDir);
        var crashLogPath = Path.Combine(logsDir, $"cub-launch-{procId}.log");
        var logSb = new StringBuilder();
        logSb.AppendLine($"[CUB Launch {DateTime.Now:O}] {javaPath} {mainClass}");
        logSb.AppendLine($"[CUB Launch] gameDir={gameDir}");
        logSb.AppendLine($"[CUB Launch] classpath 库数量: {cpParts.Count}");
        logSb.AppendLine($"[CUB Launch] Args: {string.Join(" ", allArgs)}");
        logSb.AppendLine("=====");

        // 成功判定：只匹配“窗口/渲染画面已就绪”的日志，避免加载器早期输出导致窗口还没出现就误报启动成功。
        // 注意：不能包含 “Setting up window” / “Display.*created” 这类出现在窗口真正创建之前的早期日志。
        var successPatterns = new[] { "Created window", "OpenGL version", "Sound engine started", "Done loading", "Started AutoConnect" };
        var fatalPatterns = new[] { "Could not find or load main class", "Error: A JNI error has occurred", "UnsupportedClassVersionError", "Java Virtual Machine Launcher.*Error", "Exception in thread \"main\"", "Unable to launch", "Invalid maximum heap size", "Could not reserve enough space", "Failed to download", "Unrecognized option", "Unrecognized VM option", "Error occurred during initialization of VM", "Could not create the Java Virtual Machine" };

        var resultDecided = false;
        var lastErrorLines = new List<string>();
        var procStartTime = DateTime.Now;

        void DecideSuccess()
        {
            if (resultDecided) return;
            resultDecided = true;
            RepairAttempts.TryRemove(versionId, out _); // 启动成功，重置自愈计数
            logSb.AppendLine($"\n===== LAUNCH OK @ {DateTime.Now:O} =====");
            File.WriteAllText(crashLogPath, logSb.ToString());
            Emit(new LaunchProgress { Stage = "launching", Message = "启动成功：游戏窗口已出现", Percent = 100, OverallPercent = 100, Success = true });
        }

        void DecideFailed(string error)
        {
            if (resultDecided) return;
            resultDecided = true;
            logSb.AppendLine($"\n===== [FATAL EXTRA] =====\n{error}");
            File.WriteAllText(crashLogPath, logSb.ToString());
            var enriched = $"{error}\n\n日志已保存：{crashLogPath}";
            // PCL2 风格：合并 crash-reports / latest.log 识别具体错误原因，而不是笼统提示“启动失败”
            var fullLog = SelfRepairService.BuildFullLog(versionId, logSb.ToString());
            var reason = SelfRepairService.AnalyzeError(fullLog);
            Emit(new LaunchProgress { Stage = "launching", Message = $"启动失败：{reason}", Percent = 100, OverallPercent = 100, Success = false });
            GameOutput?.Invoke(enriched);
            GameExit?.Invoke(procId, versionId, -1);

            // 自动分析日志并尝试自愈：等待游戏进程退出后执行，避免与残留进程冲突
            _ = Task.Run(async () =>
            {
                try { if (!proc.HasExited) proc.WaitForExit(30_000); } catch { /* 进程访问异常 */ }
                if (!proc.HasExited) return; // 进程仍在运行，不重启
                await TryAutoRepairAndRelaunchAsync(versionId, logSb.ToString());
            });
        }

        /// <summary>分析日志 → 自动修复缺失依赖/模组 → 自动重新启动（有重试上限）。</summary>
        async Task TryAutoRepairAndRelaunchAsync(string vid, string logContent)
        {
            try
            {
                var attempts = RepairAttempts.GetValueOrDefault(vid);
                if (attempts >= MaxRepairAttempts) return;

                var fullLog = SelfRepairService.BuildFullLog(vid, logContent);
                var reason = SelfRepairService.AnalyzeError(fullLog);
                Emit(new LaunchProgress { Stage = "launching", Message = $"检测到启动失败：{reason}\n正在尝试自动修复…", Percent = 80, OverallPercent = 90 });

                var repair = await SelfRepairService.TryRepairAsync(fullLog, vid);
                if (!repair.Repaired)
                {
                    Emit(new LaunchProgress { Stage = "launching", Message = "无法自动修复，请查看日志手动排查", Percent = 100, OverallPercent = 100, Success = false });
                    return;
                }

                RepairAttempts[vid] = attempts + 1;
                Emit(new LaunchProgress { Stage = "launching", Message = $"已自动修复：{repair.Message}，正在重新启动…", Percent = 90, OverallPercent = 95 });
                await Task.Delay(800); // 让界面刷新
                await LaunchAsync(vid);
            }
            catch (Exception ex)
            {
                Emit(new LaunchProgress { Stage = "launching", Message = $"自动修复失败：{ex.Message}", Percent = 100, OverallPercent = 100, Success = false });
            }
        }

        // 超时判定（120s）：进程活着但既没出现窗口也没命中关键词 → 视为成功（兜底，避免一直卡住）
        var timeoutCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            await Task.Delay(120_000, timeoutCts.Token);
            if (!resultDecided && RunningGames.ContainsKey(procId) && !proc.HasExited)
                DecideSuccess();
        });

        // 窗口句柄轮询：游戏主窗口真正出现才提示成功（比日志关键词更可靠）
        _ = Task.Run(async () =>
        {
            var deadline = DateTime.Now.AddSeconds(120);
            while (!resultDecided && DateTime.Now < deadline)
            {
                if (proc.HasExited) return;
                try
                {
                    proc.Refresh();
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        DecideSuccess();
                        return;
                    }
                }
                catch { /* 忽略访问异常 */ }
                await Task.Delay(1000);
            }
        });

        var fatalPending = new List<string>();
        void HandleFatal(string line)
        {
            if (resultDecided || fatalPending.Count > 0) return;
            fatalPending.Add(line);
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                if (fatalPending.Count > 0 && !resultDecided)
                {
                    var first = fatalPending[0];
                    fatalPending.Clear();
                    var tail = string.Join("\n", lastErrorLines.TakeLast(8));
                    DecideFailed(first + (tail.Length > 0 ? $"\n{tail}" : ""));
                }
            });
        }

        proc.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            logSb.AppendLine(e.Data);
            GameOutput?.Invoke(e.Data);
            if (resultDecided) return;
            if (successPatterns.Any(p => Regex.IsMatch(e.Data, p, RegexOptions.IgnoreCase))) DecideSuccess();
            if (fatalPatterns.Any(p => Regex.IsMatch(e.Data, p, RegexOptions.IgnoreCase))) HandleFatal(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            logSb.AppendLine(e.Data);
            GameOutput?.Invoke($"[ERR] {e.Data}");
            lastErrorLines.Add(e.Data);
            if (lastErrorLines.Count > 30) lastErrorLines.RemoveAt(0);
            if (resultDecided) return;
            if (fatalPatterns.Any(p => Regex.IsMatch(e.Data, p, RegexOptions.IgnoreCase))) HandleFatal(e.Data);
        };

        proc.Exited += (_, _) =>
        {
            timeoutCts.Cancel();
            var code = proc.ExitCode;
            RunningGames.TryRemove(procId, out _);
            GameExit?.Invoke(procId, versionId, code);
            if (!resultDecided)
            {
                var aliveSec = (DateTime.Now - procStartTime).TotalSeconds;
                if (code != 0 || aliveSec < 15)
                {
                    var tail = lastErrorLines.Count > 0 ? string.Join("\n", lastErrorLines.TakeLast(6)) : $"游戏进程退出，退出码: {code}";
                    DecideFailed($"启动失败，退出码 {code}\n{tail}");
                }
                else DecideSuccess();
            }
            else if (code != 0)
            {
                // 已判定启动成功，但进程在短时间内以非 0 退出码结束 → 启动后崩溃（如模组加载失败），尝试自愈
                var aliveSec = (DateTime.Now - procStartTime).TotalSeconds;
                if (aliveSec < 45)
                {
                    _ = Task.Run(async () => await TryAutoRepairAndRelaunchAsync(versionId, logSb.ToString()));
                }
            }
        };

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        Emit(new LaunchProgress { Stage = "launching", Message = $"游戏进程已启动（{versionId}）", Percent = 50, OverallPercent = 90 });
        return (true, null);
    }

    private static void Emit(LaunchProgress p) => Progress?.Invoke(p);
}
