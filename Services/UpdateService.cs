// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CraftUnifiedBooter.Services;

/// <summary>服务器返回的最新版本信息</summary>
public record UpdateInfo(
    string Version,
    string Url,
    string FileName,
    long Size,
    string Sha256,
    bool Mandatory,
    string Changelog,
    string PubDate,
    string Signature);

/// <summary>启动器更新检查：请求更新服务器的 /api/update/latest，与当前版本比较。</summary>
public static class UpdateService
{
    /// <summary>更新服务器公网地址（映射后，加密存储）</summary>
    public static string Server => Protect.D("m/XOyq4uxHOOz2z7ikp3Ep3twM6zO8gzy5U2");
    /// <summary>更新服务器本机地址（更新服务器与启动器同一台机器时兜底，加密存储）</summary>
    public static string ServerLocal => Protect.D("ynaM2WT/lVRgEoL0xcuzO8gzy5U2");

    /// <summary>启动器当前版本号（csproj 的 &lt;Version&gt;，如 1.0.0）</summary>
    public static string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>检查是否有新版本。返回 null 表示已是最新或服务器不可达。</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        foreach (var baseUrl in new[] { Server, ServerLocal })
        {
            try
            {
                var raw = await Downloader.Client.GetStringAsync($"{baseUrl}/api/update/latest");
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                var version = GetProp(root, "version");
                var url = GetProp(root, "url");
                if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url)) continue;
                if (!IsNewer(version, CurrentVersion)) return null; // 已是最新

                return new UpdateInfo(
                    version,
                    url,
                    GetProp(root, "fileName"),
                    root.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    GetProp(root, "sha256"),
                    root.TryGetProperty("mandatory", out var mandatory) && mandatory.GetBoolean(),
                    GetProp(root, "changelog"),
                    GetProp(root, "pubDate"),
                    GetProp(root, "signature"));
            }
            catch
            {
                // 当前地址不可用，尝试下一个
            }
        }
        return null;
    }

    private static string GetProp(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    /// <summary>比较版本号（"1.0.1" &gt; "1.0.0" → true）。提取数字段逐段比较。</summary>
    public static bool IsNewer(string remote, string current)
    {
        var r = Parse(remote);
        var c = Parse(current);
        var max = Math.Max(r.Length, c.Length);
        for (var i = 0; i < max; i++)
        {
            var rv = i < r.Length ? r[i] : 0;
            var cv = i < c.Length ? c[i] : 0;
            if (rv != cv) return rv > cv;
        }
        return false;
    }

    private static int[] Parse(string v)
        => v.Split('.', '-', '+', ' ')
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToArray();

    // ===================== 更新包验签（P0 安全） =====================

    /// <summary>更新包签名公钥（与服务器私钥配对）。服务端用私钥对 exe 的 SHA256 签名，客户端用此公钥验签。
    /// 若服务器尚未配置签名（不返回 signature），客户端会自动降级为仅 SHA256 校验。</summary>
    private const string PublicKeyPem = """
        -----BEGIN RSA PUBLIC KEY-----
        MIIBCgKCAQEAxfgpvgkvYD4xeBpSy8CrCzc0dJjXa1QL+S2yU+a40HwEeH3o4AJNRP/mV+vssnQF
        BrafOTzRU8EVx4T1SbR4poYoAEdPO/E3JpIuwkyRQlOpQu+ItqhGuS0C8xN0tBoXYVtr3mWmol/A
        jx27ScwXV+87Q5NiUvVOR6jXqvUcUJ5tWl411lRJ6SZqeICsFLVTt+6Oi4lFLqolhM9o0jRfxglN
        XSdBY0SxXJ/DuokS7aIFvf9/SWjtKx4VqnvTT+MlpP4VUvdlOlxGpZJ8KguPLeS/gcexnjH0pJ8W
        JQPODraTAORewjNH1lmLsqyHFC5W+fFMtNIp2bgoReYradesAQIDAQAB
        -----END RSA PUBLIC KEY-----
        """;

    /// <summary>校验已下载的更新包：先强制比对 SHA256（防传输损坏/文件替换），
    /// 若服务器提供了签名则再用内置公钥做 RSA 严格验签；未提供签名时降级为仅哈希校验（保证文件与服务器声明一致）。</summary>
    public static bool VerifyUpdate(string filePath, string expectedSha256, string signatureB64)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
                return false;

            var fileBytes = File.ReadAllBytes(filePath);

            // 1. SHA256 校验：防传输损坏 / 文件替换
            var actualHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                return false;

            // 2. RSA 验签：服务器未配置签名时降级（仅哈希校验）；配置了签名则严格验签
            if (string.IsNullOrWhiteSpace(signatureB64))
                return true;

            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            var hash = SHA256.HashData(fileBytes);
            return rsa.VerifyHash(hash, Convert.FromBase64String(signatureB64),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
}