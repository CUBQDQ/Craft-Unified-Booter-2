// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace CraftUnifiedBooter.Services;

/// <summary>微软设备码授权信息（展示给用户）</summary>
public sealed class DeviceCodeInfo
{
    public string UserCode { get; init; } = "";
    public string VerificationUri { get; init; } = "";
    public string Message { get; init; } = "";
    public int Interval { get; init; } = 5;
    public int ExpiresIn { get; init; } = 900;
    internal string DeviceCode { get; init; } = "";
}

/// <summary>
/// 微软账户登录（设备码流程 → Xbox Live → XSTS → Minecraft 服务鉴权 → 游戏档案）。
/// 使用官方 Minecraft: Java 版客户端 ID，对应 wiki.vg Microsoft Authentication Scheme。
/// </summary>
public static class MicrosoftAuthService
{
    /// <summary>官方 Minecraft: Java 版客户端 ID（在 Minecraft 服务白名单内，设备码流程用）</summary>
    private const string ClientId = "00000000402b5328";
    private const string LiveScope = "service::user.auth.xboxlive.com::MBI_SSL";

    private const string DeviceCodeUrl = "https://login.live.com/oauth20_connect.srf";
    private const string TokenUrl = "https://login.live.com/oauth20_token.srf";
    private const string UserAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string LoginWithXboxUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private const string SkinUploadUrl = "https://api.minecraftservices.com/minecraft/profile/skins";
    private const string EntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";

    private static HttpClient Http => Downloader.Client;

    /// <summary>流程进度文本（中文，供 UI 显示）</summary>
    public static event Action<string>? Progress;

    // ===================== 登录主流程 =====================
    public static async Task<Account> LoginAsync(Action<DeviceCodeInfo> onDeviceCode, CancellationToken ct = default)
    {
        Report("正在向微软申请设备码…");
        var dc = await RequestDeviceCodeAsync(ct);
        onDeviceCode(dc);
        Report("请在浏览器中完成微软登录，正在等待…");

        var (msaToken, msaRefresh, _) = await PollForTokenAsync(dc, ct);

        Report("微软登录成功，正在验证 Xbox Live 账户…");
        var (xblToken, uhs) = await XboxUserAuthAsync(msaToken, ct);

        Report("正在获取 XSTS 授权…");
        var (xstsToken, xuid) = await XstsAuthAsync(xblToken, ct);

        Report("正在通过 Minecraft 服务验证…");
        var (mcToken, mcExpires) = await LoginWithXboxAsync(uhs, xstsToken, ct);

        Report("正在读取游戏档案…");
        var profile = await GetProfileAsync(mcToken, ct)
            ?? throw new Exception("该微软账户没有 Minecraft: Java 版档案（可能尚未购买游戏）。");

        var account = new Account
        {
            Uuid = profile.Uuid,
            Name = profile.Name,
            Type = "microsoft",
            AccessToken = mcToken,
            RefreshToken = msaRefresh,
            ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + mcExpires,
            Xuid = xuid,
            SkinUrl = profile.SkinUrl,
            SkinVariant = profile.SkinVariant,
            CapeUrl = profile.CapeUrl,
        };

        // 皮肤/披风缓存到本地，供皮肤库与 3D 预览使用（披风全部下载）
        Report("正在缓存皮肤与披风…");
        account.LocalSkin = await CacheTextureAsync(account.Uuid, "skin", profile.SkinUrl, ct);
        account.LocalCapes = await CacheAllCapesAsync(account.Uuid, profile, ct);
        account.LocalCape = account.LocalCapes.LastOrDefault();
        return account;
    }

    /// <summary>刷新已保存的微软账户令牌（刷新失败返回 false）</summary>
    public static async Task<bool> RefreshAsync(Account account, CancellationToken ct = default)
    {
        if (account.Type != "microsoft" || string.IsNullOrEmpty(account.RefreshToken)) return false;
        try
        {
            Report("正在刷新微软账户令牌…");
            var (msaToken, msaRefresh, _) = await RefreshMsaTokenAsync(account.RefreshToken, ct);
            var (xblToken, uhs) = await XboxUserAuthAsync(msaToken, ct);
            var (xstsToken, xuid) = await XstsAuthAsync(xblToken, ct);
            var (mcToken, mcExpires) = await LoginWithXboxAsync(uhs, xstsToken, ct);

            account.AccessToken = mcToken;
            account.RefreshToken = msaRefresh;
            account.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + mcExpires;
            account.Xuid = xuid;
            var profile = await GetProfileAsync(mcToken, ct);
            if (profile != null)
            {
                account.Name = profile.Name;
                account.SkinUrl = profile.SkinUrl;
                account.SkinVariant = profile.SkinVariant;
                account.CapeUrl = profile.CapeUrl;
                account.LocalSkin = await CacheTextureAsync(account.Uuid, "skin", profile.SkinUrl, ct);
                account.LocalCapes = await CacheAllCapesAsync(account.Uuid, profile, ct);
                account.LocalCape = account.LocalCapes.Contains(account.LocalCape ?? "")
                    ? account.LocalCape
                    : account.LocalCapes.LastOrDefault();
            }
            AccountsStore.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>令牌即将过期时自动刷新（启动游戏前调用）</summary>
    public static async Task EnsureValidAsync(Account account, CancellationToken ct = default)
    {
        if (account.Type != "microsoft") return;
        var remain = account.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (string.IsNullOrEmpty(account.AccessToken) || remain < 300)
            await RefreshAsync(account, ct);
    }

    // ===================== 皮肤 / 披风 =====================
    /// <summary>上传本地皮肤到微软账户（variant: classic | slim）
    /// 官方接口为 PUT multipart/form-data：variant 字段 + 二进制图片文件。</summary>
    public static async Task UploadSkinAsync(Account account, string localPng, string variant, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(account.AccessToken))
            throw new Exception("账户未登录，无法同步皮肤。");
        var bytes = await File.ReadAllBytesAsync(localPng, ct);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(variant == "slim" ? "slim" : "classic"), "variant");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "skin.png");

        using var req = new HttpRequestMessage(HttpMethod.Put, SkinUploadUrl)
        {
            Content = form
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        req.Headers.TryAddWithoutValidation("User-Agent", "MinecraftLauncher/2.2.10675");
        var (status, _, raw) = await SendAsync(req, ct);
        if (status is < 200 or > 299)
            throw new Exception($"皮肤同步失败（HTTP {status}）：{raw}");
    }

    /// <summary>把远程贴图缓存到 CUB/skin_cache，返回本地路径</summary>
    public static async Task<string?> CacheTextureAsync(string uuid, string kind, string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            Store.EnsureDir(Store.CubSkinCache);
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var path = Path.Combine(Store.CubSkinCache, $"{uuid}_{kind}{ext}");
            var bytes = await Http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
        catch
        {
            return null;
        }
    }

    // ===================== 具体请求 =====================
    private static async Task<DeviceCodeInfo> RequestDeviceCodeAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, DeviceCodeUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["scope"] = LiveScope,
                ["client_id"] = ClientId,
                ["response_type"] = "device_code",
            })
        };
        var (status, json, raw) = await SendAsync(req, ct);
        if (status != 200 || json == null)
            throw new Exception($"申请设备码失败（HTTP {status}）：{Truncate(raw)}");
        return new DeviceCodeInfo
        {
            UserCode = json["user_code"]?.GetValue<string>() ?? "",
            VerificationUri = json["verification_uri"]?.GetValue<string>() ?? "https://microsoft.com/link",
            Message = json["message"]?.GetValue<string>() ?? "",
            Interval = json["interval"]?.GetValue<int>() ?? 5,
            ExpiresIn = json["expires_in"]?.GetValue<int>() ?? 900,
            DeviceCode = json["device_code"]?.GetValue<string>() ?? "",
        };
    }

    private static async Task<(string AccessToken, string RefreshToken, int ExpiresIn)> PollForTokenAsync(
        DeviceCodeInfo dc, CancellationToken ct)
    {
        var interval = Math.Max(dc.Interval, 3);
        var deadline = DateTime.UtcNow.AddSeconds(dc.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{TokenUrl}?client_id={ClientId}")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = dc.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                })
            };
            var (_, json, raw) = await SendAsync(req, ct);
            var err = json?["error"]?.GetValue<string>();
            if (err == "authorization_pending") continue;
            if (err == "slow_down") { interval += 2; continue; }
            if (err == "expired_token") break;
            if (err != null)
                throw new Exception($"微软授权被拒绝（{err}）：{json?["error_description"]?.GetValue<string>()}");
            if (json?["access_token"] != null)
                return (
                    json["access_token"]!.GetValue<string>(),
                    json["refresh_token"]?.GetValue<string>() ?? "",
                    json["expires_in"]?.GetValue<int>() ?? 3600);
            if (string.IsNullOrEmpty(raw)) continue;
            throw new Exception($"微软授权返回异常：{Truncate(raw)}");
        }
        throw new Exception("设备码已过期，请重新登录。");
    }

    private static async Task<(string AccessToken, string RefreshToken, int ExpiresIn)> RefreshMsaTokenAsync(
        string refreshToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["scope"] = LiveScope,
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            })
        };
        var (status, json, raw) = await SendAsync(req, ct);
        if (status != 200 || json?["access_token"] == null)
            throw new Exception($"刷新微软令牌失败（HTTP {status}）：{Truncate(raw)}");
        return (
            json["access_token"]!.GetValue<string>(),
            json["refresh_token"]?.GetValue<string>() ?? refreshToken,
            json["expires_in"]?.GetValue<int>() ?? 3600);
    }

    private static async Task<(string Token, string Uhs)> XboxUserAuthAsync(string msaToken, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["RelyingParty"] = "http://auth.xboxlive.com",
            ["TokenType"] = "JWT",
            ["Properties"] = new JsonObject
            {
                ["AuthMethod"] = "RPS",
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RpsTicket"] = "t=" + msaToken,
            }
        };
        using var req = JsonPost(UserAuthUrl, body);
        req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "2");
        var (status, json, raw) = await SendAsync(req, ct);
        if (status != 200 || json == null)
            throw new Exception($"Xbox Live 认证失败（HTTP {status}）：{Truncate(raw)}");
        var token = json["Token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token)) throw new Exception("Xbox Live 未返回令牌。");
        var uhs = json["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>() ?? "";
        return (token, uhs);
    }

    private static async Task<(string Token, string Xuid)> XstsAuthAsync(string xblToken, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["RelyingParty"] = "rp://api.minecraftservices.com/",
            ["TokenType"] = "JWT",
            ["Properties"] = new JsonObject
            {
                ["SandboxId"] = "RETAIL",
                ["UserTokens"] = new JsonArray { xblToken },
            }
        };
        using var req = JsonPost(XstsUrl, body);
        req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        var (status, json, raw) = await SendAsync(req, ct);
        if (status != 200 || json == null)
        {
            var xerr = json?["XErr"]?.GetValue<long>();
            throw new Exception(XstsErrorMessage(xerr) ?? $"XSTS 授权失败（HTTP {status}）：{Truncate(raw)}");
        }
        var token = json["Token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token)) throw new Exception("XSTS 未返回令牌。");
        var xuid = json["DisplayClaims"]?["xui"]?[0]?["xid"]?.GetValue<string>() ?? "";
        return (token, xuid);
    }

    private static async Task<(string Token, int ExpiresIn)> LoginWithXboxAsync(string uhs, string xstsToken, CancellationToken ct)
    {
        var body = new JsonObject { ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}" };
        using var req = JsonPost(LoginWithXboxUrl, body);
        req.Headers.TryAddWithoutValidation("User-Agent", "MinecraftLauncher/2.2.10675");
        var (status, json, raw) = await SendAsync(req, ct);
        if (status == 403)
            throw new Exception("Minecraft 服务拒绝了该应用（HTTP 403 应用未注册），请检查登录客户端 ID 是否在允许列表内。");
        if (status != 200 || json?["access_token"] == null)
            throw new Exception($"Minecraft 鉴权失败（HTTP {status}）：{Truncate(raw)}");
        return (json["access_token"]!.GetValue<string>(), json["expires_in"]?.GetValue<int>() ?? 86400);
    }

    private sealed class McProfile
    {
        public string Uuid = "";
        public string Name = "";
        public string? SkinUrl;
        public string? SkinVariant;
        public string? CapeUrl;
        /// <summary>账户拥有的全部披风地址（可能多个）</summary>
        public List<(string Url, string Alias)> CapeUrls = new();
    }

    private static async Task<McProfile?> GetProfileAsync(string mcToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
        req.Headers.TryAddWithoutValidation("User-Agent", "MinecraftLauncher/2.2.10675");
        var (status, json, _) = await SendAsync(req, ct);
        if (status == 404 || json == null) return null;

        var id = json["id"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(id)) return null;

        var profile = new McProfile
        {
            Uuid = AddDashes(id),
            Name = json["name"]?.GetValue<string>() ?? "Player",
        };

        if (json["skins"] is JsonArray skins)
        {
            foreach (var s in skins)
            {
                if (s?["state"]?.GetValue<string>() != "ACTIVE") continue;
                profile.SkinUrl = s["url"]?.GetValue<string>();
                profile.SkinVariant = s["variant"]?.GetValue<string>() ?? "classic";
                break;
            }
        }
        if (json["capes"] is JsonArray capes)
        {
            // 收集账户拥有的全部披风（含未激活的），激活的优先作为当前披风
            foreach (var c in capes)
            {
                var url = c?["url"]?.GetValue<string>();
                if (string.IsNullOrEmpty(url)) continue;
                var alias = c?["alias"]?.GetValue<string>() ?? "披风";
                profile.CapeUrls.Add((url, alias));
                if (c?["state"]?.GetValue<string>() == "ACTIVE" && profile.CapeUrl == null)
                    profile.CapeUrl = url;
            }
            profile.CapeUrl ??= profile.CapeUrls.FirstOrDefault().Url;
        }
        return profile;
    }

    /// <summary>把账户的全部披风下载到本地缓存，返回本地路径列表</summary>
    private static async Task<List<string>> CacheAllCapesAsync(string uuid, McProfile profile, CancellationToken ct)
    {
        var result = new List<string>();
        for (var i = 0; i < profile.CapeUrls.Count; i++)
        {
            var path = await CacheTextureAsync(uuid, $"cape{i}", profile.CapeUrls[i].Url, ct);
            if (!string.IsNullOrEmpty(path)) result.Add(path);
        }
        // 当前激活的披风放到列表末尾，便于 UI 默认选中
        var active = await CacheTextureAsync(uuid, "cape", profile.CapeUrl, ct);
        if (!string.IsNullOrEmpty(active)) result.Add(active);
        return result;
    }

    /// <summary>是否拥有 Minecraft: Java 版（正版验证）</summary>
    public static async Task<bool> HasGameOwnershipAsync(string mcToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{EntitlementsUrl}?requestId={Guid.NewGuid()}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
        var (status, json, _) = await SendAsync(req, ct);
        if (status != 200 || json == null) return false;
        return json["items"] is JsonArray { Count: > 0 };
    }

    // ===================== 工具 =====================
    private static HttpRequestMessage JsonPost(string url, JsonNode body) =>
        new(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

    private static async Task<(int Status, JsonNode? Json, string Raw)> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await Http.SendAsync(req, ct);
        var status = (int)resp.StatusCode;
        var raw = await resp.Content.ReadAsStringAsync(ct);
        JsonNode? node = null;
        try { node = JsonNode.Parse(raw); } catch { /* 非 JSON 响应 */ }
        return (status, node, raw);
    }

    private static string AddDashes(string hex)
    {
        if (hex.Length != 32) return hex;
        return $"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex[20..]}";
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;

    private static void Report(string message) => Progress?.Invoke(message);

    private static string? XstsErrorMessage(long? xerr) => xerr switch
    {
        2148916227 => "该账户已被 Xbox 封禁（违反社区准则），无法使用。",
        2148916229 => "该账户受家长控制限制，监护人未授予联机权限。",
        2148916233 => "该微软账户尚未创建 Xbox 档案，请先到 https://signup.live.com/signup 创建。",
        2148916234 => "该账户尚未接受 Xbox 服务条款，请登录并接受后再试。",
        2148916235 => "Xbox 未在您所在地区提供服务，登录被拒绝。",
        2148916236 => "该账户需要验证年龄，请到 https://login.live.com/login.srf 完成验证。",
        2148916237 => "该账户游玩时间已达上限，登录被阻止。",
        2148916238 => "该账户未满 18 岁且未加入家庭组，需由成年人将其加入 Microsoft 家庭后使用。",
        _ => null,
    };
}