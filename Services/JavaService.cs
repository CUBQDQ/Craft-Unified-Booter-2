// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CraftUnifiedBooter.Services;

/// <summary>Java 检测 / 版本要求计算 / 便携版 JRE 下载（对应原版 electron/ipc/settings.ts + utils/javaManager.ts）</summary>
public static class JavaService
{
    // ===================== Java 信息解析 =====================
    public static JavaInfo GetJavaInfo(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo(javaPath, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            var output = p!.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            var m = System.Text.RegularExpressions.Regex.Match(output, "version \"(\\d+)(?:\\.(\\d+))?(?:\\.(\\d+))?");
            if (!m.Success) return new JavaInfo { Path = javaPath, Version = "unknown", MajorVersion = 0, IsValid = false };
            var major = int.Parse(m.Groups[1].Value);
            var version = m.Groups[0].Value.Replace("version \"", "").TrimEnd('"');
            if (major == 1 && m.Groups[2].Success)
            {
                major = int.Parse(m.Groups[2].Value);
                version = $"1.{major}.{(m.Groups[3].Success ? m.Groups[3].Value : "0")}";
            }
            var is64 = output.Contains("64-Bit") || output.Contains("64-bit");
            var binDir = Path.GetDirectoryName(javaPath);
            var home = string.IsNullOrEmpty(binDir) ? "" : Path.GetDirectoryName(binDir) ?? "";
            var isJre = !string.IsNullOrEmpty(binDir) && !File.Exists(Path.Combine(binDir, "javac.exe"));
            return new JavaInfo
            {
                Path = javaPath,
                Folder = home,
                Version = version,
                MajorVersion = major,
                Is64Bit = is64,
                IsJre = isJre,
                IsValid = true,
            };
        }
        catch
        {
            return new JavaInfo { Path = javaPath, Version = "unknown", MajorVersion = 0, IsValid = false };
        }
    }

    private static bool IsDevToolJre(string javaPath)
    {
        var lower = javaPath.ToLowerInvariant();
        return lower.Contains("trae") || lower.Contains("code") || lower.Contains("intellij")
            || lower.Contains("androidstudio") || lower.Contains("jbr") || lower.Contains(".gradle");
    }

    /// <summary>Windows 下检测系统 Java（PATH + 常见目录 + 注册表 + JAVA_HOME）</summary>
    public static List<JavaInfo> DetectJava()
    {
        var results = new List<JavaInfo>();
        var paths = new List<string>();

        // 1) where java
        try
        {
            var psi = new ProcessStartInfo("where", "java") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var lines = p.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                    if (File.Exists(line.Trim()) && !paths.Contains(line.Trim()) && !IsDevToolJre(line.Trim()))
                        paths.Add(line.Trim());
                p.WaitForExit(5000);
            }
        }
        catch { /* ignore */ }

        // 2) Program Files 常见目录
        var programFiles = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        foreach (var pf in programFiles.Where(p => !string.IsNullOrEmpty(p)))
        {
            try
            {
                var javaDir = Path.Combine(pf, "Java");
                if (Directory.Exists(javaDir))
                    foreach (var entry in Directory.GetDirectories(javaDir))
                    {
                        var javaExe = Path.Combine(entry, "bin", "java.exe");
                        if (File.Exists(javaExe) && !paths.Contains(javaExe)) paths.Add(javaExe);
                    }
                foreach (var entry in Directory.GetDirectories(pf))
                {
                    var javaExe = Path.Combine(entry, "bin", "java.exe");
                    if (File.Exists(javaExe) && !paths.Contains(javaExe) && !IsDevToolJre(javaExe)) paths.Add(javaExe);
                }
            }
            catch { /* ignore */ }
        }

        // 3) 注册表 JavaSoft
        try
        {
            var psi = new ProcessStartInfo("reg", "query \"HKLM\\SOFTWARE\\JavaSoft\" /s") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                foreach (var m in System.Text.RegularExpressions.Regex.Matches(output, @"JavaHome\s+REG_SZ\s+(.+)"))
                {
                    var home = m.ToString()!.Split("REG_SZ")[^1].Trim();
                    var javaExe = Path.Combine(home, "bin", "java.exe");
                    if (File.Exists(javaExe) && !paths.Contains(javaExe) && !IsDevToolJre(javaExe)) paths.Add(javaExe);
                }
            }
        }
        catch { /* ignore */ }

        // 4) JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var javaExe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(javaExe) && !paths.Contains(javaExe) && !IsDevToolJre(javaExe)) paths.Add(javaExe);
        }

        // 5) 常见自定义目录
        foreach (var root in new[] { @"F:\java", @"D:\java", @"C:\java" })
            if (Directory.Exists(root))
                foreach (var entry in Directory.GetDirectories(root))
                {
                    var javaExe = Path.Combine(entry, "bin", "java.exe");
                    if (File.Exists(javaExe) && !paths.Contains(javaExe)) paths.Add(javaExe);
                }

        foreach (var p in paths.Distinct())
        {
            var info = GetJavaInfo(p);
            if (info.IsValid) results.Add(info);
        }

        // 按偏好排序：21 > 17 > 8 > 其余按新到旧
        results.Sort((a, b) =>
        {
            int Pref(int m) => m is 21 or 17 or 8 ? new[] { 21, 17, 8 }.ToList().IndexOf(m) : -1;
            var ai = Pref(a.MajorVersion); var bi = Pref(b.MajorVersion);
            if (ai >= 0 && bi >= 0) return ai - bi;
            if (ai >= 0) return -1;
            if (bi >= 0) return 1;
            return b.MajorVersion - a.MajorVersion;
        });
        return results;
    }

    public static string AutoDetectJavaPath()
    {
        var list = DetectJava();
        return list.Count > 0 ? list[0].Path : "java";
    }

    // ===================== 内存自动分配 =====================
    public static (int Min, int Max) AutoAllocateMemory()
    {
        try
        {
            var totalMb = (long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
            if (totalMb <= 0) return (1024, 4096);
            var max = Math.Max(2048, Math.Min((int)(totalMb * 0.6), 8192));
            var min = Math.Max(512, Math.Min(max / 4, 1024));
            return (min, max);
        }
        catch { return (1024, 4096); }
    }

    // ===================== 版本 → 所需 Java（PCL2 逻辑：最低 + 最高版本范围） =====================

    /// <summary>Java 版本需求范围（Max 为 999 表示不限制最高版本）。</summary>
    public readonly record struct JavaRequirement(int Min, int Max);

    /// <summary>从版本 ID 解析 Minecraft 主/次版本号（如 "1.21.4" → (1,21)，"23w41a" → (23,41)）。</summary>
    private static (int Major, int Minor) ParseMcVersion(string versionId)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(versionId, @"1\.(\d+)(?:\.(\d+))?");
        if (matches.Count > 0)
        {
            var last = matches[^1];
            var m = System.Text.RegularExpressions.Regex.Match(last.Value, @"1\.(\d+)(?:\.(\d+))?");
            return (int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0);
        }
        var cal = System.Text.RegularExpressions.Regex.Match(versionId, @"(?:^|\D)(\d{2})\.(\d+)");
        if (cal.Success) return (int.Parse(cal.Groups[1].Value), int.Parse(cal.Groups[2].Value));
        return (0, 0);
    }

    /// <summary>计算版本所需的 Java 范围（考虑加载器兼容性，参考 PCL2 McLaunchJava）。</summary>
    public static JavaRequirement GetJavaRequirement(string versionId)
    {
        var min = 8;
        var max = 999;
        var (major, minor) = ParseMcVersion(versionId);
        string? inheritsFrom = null;
        var javaVersionKnown = false;

        // 1) 已安装版本 JSON 的 javaVersion.majorVersion 最准确
        var jsonPath = Path.Combine(Store.GetVersionsDir(), versionId, $"{versionId}.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var vd = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath), Store.JsonReadOpts);
                if (vd?.JavaVersion?.MajorVersion >= 8)
                {
                    min = vd.JavaVersion.MajorVersion;
                    javaVersionKnown = true;
                }
                inheritsFrom = vd?.InheritsFrom;
                var loader = vd == null ? null : VersionsService.DetectLoader(vd);

                // 加载器兼容性限制（参考 PCL2）
                if (loader is "forge" or "neoforge")
                {
                    if (major == 1 && minor <= 12) max = 8;          // 老 Forge 必须 Java 8
                    else if (major == 1 && minor <= 14) max = 10;    // 1.13-1.14
                    else if (major == 1 && minor == 15) max = 15;    // 1.15
                    else if (major == 1 && minor == 16) max = 17;    // 1.16 放宽到 17
                    else if (major == 1 && minor >= 18) min = Math.Max(min, 17);
                }
                else if (loader == "optifine")
                {
                    if (major == 1 && minor <= 7) max = 8;
                    else if (major == 1 && minor is >= 8 and <= 11) { min = 8; max = 8; }
                    else if (major == 1 && minor == 12) max = 8;
                }
                else if (loader == "fabric")
                {
                    if (major == 1 && minor is >= 15 and <= 16) min = Math.Max(min, 8);
                    else if (major == 1 && minor >= 18) min = Math.Max(min, 17);
                }
            }
            catch { /* 解析失败则按版本号推断 */ }
        }

        // 1.5) Fabric/Forge profile 通常没有 javaVersion，但会 inheritsFrom 原版版本 → 用父版本补充
        if (!javaVersionKnown && !string.IsNullOrEmpty(inheritsFrom))
        {
            var parentJsonPath = Path.Combine(Store.GetVersionsDir(), inheritsFrom, $"{inheritsFrom}.json");
            if (File.Exists(parentJsonPath))
            {
                try
                {
                    var parent = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(parentJsonPath), Store.JsonReadOpts);
                    if (parent?.JavaVersion?.MajorVersion >= 8)
                    {
                        min = Math.Max(min, parent.JavaVersion.MajorVersion);
                        javaVersionKnown = true;
                    }
                }
                catch { /* 忽略 */ }
            }

            // 同时用父版本 ID 做版本号推断（如 "26.2" → major=26, minor=2）
            var (pmajor, pminor) = ParseMcVersion(inheritsFrom);
            if (pmajor > major)
            {
                major = pmajor;
                minor = pminor;
            }
        }

        // 2) 版本号推断（版本 JSON 缺失/损坏/无 javaVersion 时）
        if (!javaVersionKnown)
        {
            if (major >= 25) min = 25;
            else if (major >= 21) min = 21;
            else if (major == 20 && minor >= 5) min = 21;
            else if (major >= 18) min = 17;
            else if (major == 17) min = 16;
            else min = 8;
        }

        return new JavaRequirement(min, max);
    }

    /// <summary>最低所需 Java 主版本号（兼容旧调用）</summary>
    public static int GetRequiredJavaVersion(string versionId) =>
        GetJavaRequirement(versionId).Min;

    /// <summary>为版本选择 Java：便携版优先，过滤版本范围后按 PCL2 风格权重排序。</summary>
    public static JavaInfo SelectJavaForVersion(string versionId)
    {
        var req = GetJavaRequirement(versionId);
        var list = new List<JavaInfo>();

        // 便携版 JRE（优先）
        foreach (var m in ListPortableJava())
        {
            var info = GetJavaInfo(m);
            if (info.IsValid) list.Add(info);
        }

        // 系统已安装 Java
        list.AddRange(DetectJava());
        if (list.Count == 0) return new JavaInfo { Path = "java", Version = "unknown", MajorVersion = 0, IsValid = false };

        // 过滤：优先在 [Min, Max] 范围内
        var suitable = list.Where(j => j.MajorVersion >= req.Min && j.MajorVersion <= req.Max).ToList();
        if (suitable.Count == 0)
        {
            // 无满足范围时：优先 >= Min 的最低版本；否则取最高版本
            suitable = list.Where(j => j.MajorVersion >= req.Min).OrderBy(j => j.MajorVersion).ToList();
            if (suitable.Count == 0) suitable = list.OrderByDescending(j => j.MajorVersion).ToList();
        }

        suitable.Sort((a, b) => CompareJavaPriority(a, b, req.Min, Store.GetActiveGameRoot(), Store.CubDir));
        return suitable[0];
    }

    /// <summary>PCL2 风格 Java 排序：便携版 > 特定目录 > 64位 > JRE > 版本权重 > 接近最低需求。</summary>
    private static int CompareJavaPriority(JavaInfo a, JavaInfo b, int min, string mcRoot, string launcherDir)
    {
        // 1. 便携版（启动器自带）优先
        bool aPortable = a.Path.StartsWith(Store.CubJavaDir, StringComparison.OrdinalIgnoreCase);
        bool bPortable = b.Path.StartsWith(Store.CubJavaDir, StringComparison.OrdinalIgnoreCase);
        if (aPortable != bPortable) return aPortable ? -1 : 1;

        // 2. 特定目录优先（Minecraft 根目录 / 启动器目录附近）
        bool aLocal = IsNearGameDir(a.Path, mcRoot) || a.Path.StartsWith(launcherDir, StringComparison.OrdinalIgnoreCase);
        bool bLocal = IsNearGameDir(b.Path, mcRoot) || b.Path.StartsWith(launcherDir, StringComparison.OrdinalIgnoreCase);
        if (aLocal != bLocal) return aLocal ? -1 : 1;

        // 3. 64 位优先
        if (a.Is64Bit != b.Is64Bit) return a.Is64Bit ? -1 : 1;

        // 4. JRE 优先（PCL2 倾向 JRE；便携版是 JRE）
        if (a.IsJre != b.IsJre) return a.IsJre ? -1 : 1;

        // 5. 大版本权重（Minecraft 常用版本优先，参考 PCL2 的权重表思想）
        int w = JavaVersionWeight(a.MajorVersion) - JavaVersionWeight(b.MajorVersion);
        if (w != 0) return -w;

        // 6. 更接近最低需求的版本优先
        return a.MajorVersion.CompareTo(b.MajorVersion);
    }

    /// <summary>Minecraft 场景下的 Java 版本权重（越高越优先）。</summary>
    private static int JavaVersionWeight(int major) => major switch
    {
        8 => 30,    // 老版本 MC / 老 Forge 兼容性最好
        11 => 8,
        15 => 9,
        16 => 10,
        17 => 31,   // 1.18+ 推荐
        18 => 15,
        19 => 13,
        20 => 12,
        21 => 29,   // 1.20.5+ 推荐
        22 => 11,
        >= 23 => 7,
        _ => major,
    };

    /// <summary>判断 Java 是否位于 Minecraft 根目录附近（PCL2 会优先使用 MC 文件夹下的 Java）。</summary>
    private static bool IsNearGameDir(string javaPath, string mcRoot)
    {
        if (string.IsNullOrEmpty(mcRoot)) return false;
        var dir = Path.GetDirectoryName(javaPath) ?? "";
        while (!string.IsNullOrEmpty(dir))
        {
            if (dir.Equals(mcRoot, StringComparison.OrdinalIgnoreCase)) return true;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        return false;
    }

    // ===================== 便携版 Java =====================
    public static List<string> ListPortableJava()
    {
        var result = new List<string>();
        // 推荐版本优先（含现代 MC 需要的 25），再补上目录中实际已安装的其他版本
        foreach (var m in new[] { 25, 21, 17, 16, 8 })
        {
            var exe = GetPortableJavaExe(m);
            if (exe != null && !result.Contains(exe)) result.Add(exe);
        }
        if (Directory.Exists(Store.CubJavaDir))
        {
            foreach (var dir in Directory.GetDirectories(Store.CubJavaDir))
            {
                if (int.TryParse(Path.GetFileName(dir), out var major))
                {
                    var exe = GetPortableJavaExe(major);
                    if (exe != null && !result.Contains(exe)) result.Add(exe);
                }
            }
        }
        return result;
    }

    public static string? GetPortableJavaExe(int major)
    {
        var dir = Path.Combine(Store.CubJavaDir, major.ToString());
        if (!Directory.Exists(dir)) return null;
        try
        {
            foreach (var e in Directory.GetDirectories(dir))
            {
                var exe = Path.Combine(e, "bin", "java.exe");
                if (File.Exists(exe)) return exe;
            }
            var flat = Path.Combine(dir, "bin", "java.exe");
            return File.Exists(flat) ? flat : null;
        }
        catch { return null; }
    }

    /// <summary>确保某主版本便携 JRE 已安装，未安装则从 Adoptium 下载解压（对应 ensureJavaForVersion）</summary>
    public static async Task<string> EnsureJavaAsync(int majorVersion, Action<string, int>? stage = null, CancellationToken ct = default)
    {
        var existing = GetPortableJavaExe(majorVersion);
        if (existing != null) return existing;

        var installDir = Path.Combine(Store.CubJavaDir, majorVersion.ToString());
        Store.EnsureDir(installDir);
        stage?.Invoke($"查询 Java {majorVersion} 最新版本…", 0);

        // 查询 Adoptium API
        var apiUrl = $"https://api.adoptium.net/v3/assets/latest/{majorVersion}/hotspot?os=windows&architecture=x64&image_type=jre&heap_size=normal&vendor=eclipse";
        var asset = await FetchAdoptiumAsset(apiUrl, ct);

        // 下载 zip
        var zipDest = Path.Combine(installDir, asset.AssetName);
        if (!File.Exists(zipDest) || new FileInfo(zipDest).Length != asset.Size)
        {
            try { if (File.Exists(zipDest)) File.Delete(zipDest); } catch { /* ignore */ }
            stage?.Invoke($"正在下载 Java {majorVersion} ({asset.ReleaseName})…", 30);
            await Downloader.DownloadFileAsync(asset.Url, zipDest, asset.Size, null, ct);
        }

        // 清掉旧 jdk-* 目录，解压
        stage?.Invoke($"正在解压 Java {majorVersion}…", 70);
        try
        {
            foreach (var d in Directory.GetDirectories(installDir))
                if (Path.GetFileName(d).ToLowerInvariant().StartsWith("jdk"))
                    Directory.Delete(d, true);
        }
        catch { /* ignore */ }

        ZipFile.ExtractToDirectory(zipDest, installDir);
        stage?.Invoke($"正在验证 Java {majorVersion}…", 90);

        var javaExe = GetPortableJavaExe(majorVersion);
        if (javaExe == null) throw new Exception($"[java] 解压后找不到 bin/java.exe，asset={asset.AssetName}");
        stage?.Invoke($"Java {majorVersion} ({asset.ReleaseName}) 已就绪", 100);
        return javaExe;
    }

    private static async Task<(string Url, long Size, string ReleaseName, string AssetName)> FetchAdoptiumAsset(string url, CancellationToken ct)
    {
        using var resp = await Downloader.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!el.TryGetProperty("binary", out var bin)) continue;
            var os = bin.GetProperty("os").GetString();
            var arch = bin.GetProperty("architecture").GetString();
            var imageType = bin.GetProperty("image_type").GetString();
            var heap = bin.GetProperty("heap_size").GetString();
            var jvmImpl = bin.GetProperty("jvm_impl").GetString();
            if (os != "windows" || arch != "x64" || imageType != "jre" || heap != "normal" || jvmImpl != "hotspot") continue;
            var pkg = bin.GetProperty("package");
            var releaseName = el.TryGetProperty("release_name", out var rn) ? rn.GetString()! : $"Java {url.Split('/')[^2]}";
            return (pkg.GetProperty("link").GetString()!, pkg.GetProperty("size").GetInt64(), releaseName, pkg.GetProperty("name").GetString()!);
        }
        throw new Exception($"[java] 未找到 Java 对应 Windows x64 JRE 资产");
    }
}
