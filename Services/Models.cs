// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftUnifiedBooter.Services;

// ===================== 版本清单 (version_manifest_v2.json) =====================
public class VersionManifest
{
    public VersionLatest? Latest { get; set; }
    public List<VersionEntry> Versions { get; set; } = new();
}

public class VersionLatest
{
    public string? Release { get; set; }
    public string? Snapshot { get; set; }
}

public class VersionEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Time { get; set; }
    public string? ReleaseTime { get; set; }
    public string? Sha1 { get; set; }
    [JsonIgnore] public bool Installed { get; set; }
    [JsonIgnore] public string? Loader { get; set; }
    [JsonIgnore] public bool IsExtra { get; set; }
}

// ===================== 版本详情 (versions/<id>/<id>.json) =====================
public class VersionDetail
{
    public string Id { get; set; } = "";
    public string? Type { get; set; }
    public string? MainClass { get; set; }
    public string? InheritsFrom { get; set; }
    public string? MinecraftArguments { get; set; }
    public VersionDownloads? Downloads { get; set; }
    public List<VersionLibrary> Libraries { get; set; } = new();
    public VersionAssetIndex? AssetIndex { get; set; }
    public VersionArguments? Arguments { get; set; }
    public Dictionary<string, string>? Natives { get; set; }
    public JavaVersionInfo? JavaVersion { get; set; }
    public string? ReleaseTime { get; set; }
}

public class JavaVersionInfo
{
    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

public class VersionDownloads
{
    public VersionDownloadFile? Client { get; set; }
    public VersionDownloadFile? Server { get; set; }
}

public class VersionDownloadFile
{
    public string Url { get; set; } = "";
    public string? Sha1 { get; set; }
    public long Size { get; set; }
}

public class VersionAssetIndex
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Sha1 { get; set; }
    public long Size { get; set; }
    public long TotalSize { get; set; }
}

public class VersionLibrary
{
    public string Name { get; set; } = "";
    /// <summary>Maven 仓库根 URL（Fabric/OptiFine profile 使用顶级 url 字段）</summary>
    public string? Url { get; set; }
    public VersionLibDownloads? Downloads { get; set; }
    public List<VersionRule>? Rules { get; set; }
    public Dictionary<string, string>? Natives { get; set; }
}

public class VersionLibDownloads
{
    public VersionLibFile? Artifact { get; set; }
    public Dictionary<string, VersionLibFile>? Classifiers { get; set; }
}

public class VersionLibFile
{
    public string Url { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Sha1 { get; set; }
    public long Size { get; set; }
}

public class VersionRule
{
    public string Action { get; set; } = "";
    public VersionRuleOs? Os { get; set; }
    public VersionRuleFeatures? Features { get; set; }
}

public class VersionRuleOs
{
    public string? Name { get; set; }
    public string? Arch { get; set; }
}

public class VersionRuleFeatures
{
    [JsonPropertyName("is_demo_user")] public bool? IsDemoUser { get; set; }
    [JsonPropertyName("is_quick_play")] public bool? IsQuickPlay { get; set; }
    [JsonPropertyName("has_custom_resolution")] public bool? HasCustomResolution { get; set; }
}

public class VersionArguments
{
    public List<JsonElement>? Game { get; set; }
    public List<JsonElement>? Jvm { get; set; }
}

// ===================== 账户 =====================
public class Account
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "offline"; // offline | microsoft
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public long ExpiresAt { get; set; }
    public bool Selected { get; set; }
    public string? SkinUrl { get; set; }
    public string? SkinVariant { get; set; }
    /// <summary>披风贴图地址（正版账户）</summary>
    public string? CapeUrl { get; set; }
    /// <summary>Xbox 用户 ID（正版账户启动参数用）</summary>
    public string? Xuid { get; set; }
    /// <summary>本地已缓存的皮肤贴图路径（皮肤库使用，优先于 SkinUrl）</summary>
    public string? LocalSkin { get; set; }
    /// <summary>本地已缓存的披风贴图路径（当前使用）</summary>
    public string? LocalCape { get; set; }
    /// <summary>本地已缓存的全部披风贴图路径（微软账户可能拥有多个披风）</summary>
    public List<string> LocalCapes { get; set; } = new();
}

// ===================== 下载任务（UI 展示用） =====================
public enum DownloadStatus
{
    Queued,        // 等待中
    Downloading,   // 下载中
    Paused,        // 已暂停
    Completed,     // 已完成
    Canceled,      // 已取消
    Failed         // 失败
}

public class DownloadTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "版本"; // 版本/Mod/资源包/Java
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public long Total { get; set; }
    public long Downloaded { get; set; }
    public int Percent { get; set; }
    public bool Done { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool IsActive => !Done;
    /// <summary>本次下载的总文件数（整个版本包所有文件，不是单个模块）</summary>
    public int TotalFiles { get; set; }
    /// <summary>已下载完成的文件数</summary>
    public int DownloadedFiles { get; set; }
    /// <summary>任务状态（驱动 UI 状态徽标与操作按钮）</summary>
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    /// <summary>优先级：0-10，默认 5，数值越大越优先</summary>
    public int Priority { get; set; } = 5;
    /// <summary>暂停请求标志（下载循环检查）</summary>
    public volatile bool PauseRequested;
    /// <summary>取消令牌源（DownloadManager 内部使用）</summary>
    public CancellationTokenSource? Cts { get; set; }
}

// ===================== Java 信息 =====================
public class JavaInfo
{
    public string Path { get; set; } = "";
    /// <summary>Java 安装根目录（bin 的上一级，用于 PATH 设置）</summary>
    public string Folder { get; set; } = "";
    public string Version { get; set; } = "";
    public int MajorVersion { get; set; }
    public bool Is64Bit { get; set; }
    public bool IsValid { get; set; }
    /// <summary>是否为 JRE（不存在 javac.exe 视为 JRE）</summary>
    public bool IsJre { get; set; }
}

// ===================== 版本文件缓存（SHA1 完整性校验） =====================
public class VersionFileCache
{
    public string Version { get; set; } = "";
    public long SavedAt { get; set; }
    public List<VersionCachedFile> Files { get; set; } = new();
}

public class VersionCachedFile
{
    /// <summary>相对 .minecraft 根目录的路径（如 versions/1.20.1/1.20.1.jar）</summary>
    public string Path { get; set; } = "";
    /// <summary>文件 SHA1（Mojang 元数据提供；缺失则留空，校验时跳过）</summary>
    public string Sha1 { get; set; } = "";
    public long Size { get; set; }
}
