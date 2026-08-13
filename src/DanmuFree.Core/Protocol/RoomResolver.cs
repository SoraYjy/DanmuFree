using System.Collections.Specialized;
using System.Net.Http;
using System.Text.Json;

namespace DanmuFree.Core.Protocol;

/// <summary>
/// Resolves a B站 live room short-id (or real room id) into the connection info needed
/// to open a danmu websocket: real room_id, danmu token, wss url, and identity fields
/// (uid / buvid3) parsed from the cookie. The cookie, when provided, is attached to
/// every outbound HTTP request via the HttpClient default headers so authenticated
/// <c>getDanmuInfo</c> calls succeed.
/// </summary>
public sealed class RoomResolver
{
    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly string? _cookie;
    private readonly Action<string>? _log;

    public RoomResolver(HttpClient http, string? cookie, Action<string>? log = null)
    {
        _http = http;
        _cookie = cookie;
        _log = log;
        // 浏览器化请求头：B站直播接口（getInfoByRoom / getDanmuInfo）对无 UA / 无 Referer 的请求会
        // 返回 code:-352，或 code:0 但缺 room_info.room_id / token（被风控当机器人）。即便带上登录
        // cookie 也需要这三件套才稳——历史上「已登录却提示 cookie 无效」多由此而来。
        _http.DefaultRequestHeaders.Add("User-Agent", UA);
        _http.DefaultRequestHeaders.Add("Referer", "https://live.bilibili.com/");
        _http.DefaultRequestHeaders.Add("Origin", "https://live.bilibili.com");
        if (!string.IsNullOrWhiteSpace(cookie))
            _http.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    public async Task<RoomInfo> ResolveAsync(string roomIdInput, CancellationToken ct)
    {
        long uid = 0; string? buvid3 = null;
        if (!string.IsNullOrWhiteSpace(_cookie))
        {
            var kv = ParseCookie(_cookie);
            if (kv["DedeUserID"] is { } u && long.TryParse(u, out var parsed)) uid = parsed;
            buvid3 = kv["buvid3"];
        }
        // 记录 cookie 字段「有没有」（不记值）：诊断「UID 显示却 cookie 无效」时，判断是否漏了 SESSDATA。
        _log?.Invoke($"B站 cookie 字段：SESSDATA={Has(_cookie, "SESSDATA")}, DedeUserID={Has(_cookie, "DedeUserID")}, " +
                     $"bili_jct={Has(_cookie, "bili_jct")}, buvid3={(buvid3 is null ? "无" : "有")}；解析出 uid={uid}");

        int realId = await GetRealRoomId(roomIdInput, ct);
        _log?.Invoke($"B站 真实房间号解析成功：{roomIdInput} → {realId}");
        var (token, wss) = await GetDanmu(realId, ct);
        _log?.Invoke("B站 弹幕服务获取成功：token 有，wss 地址已拿到");
        return new RoomInfo(realId, token, wss, buvid3, uid);
    }

    private async Task<int> GetRealRoomId(string roomId, CancellationToken ct)
    {
        // 房间号为数字，无需 URL 编码（System.Web 在 net8.0 不可用）。
        // B站已弃用 getRoomInfoOld（统一返回 code:-400），改用 getInfoByRoom，
        // 其真实房间号在 data.room_info.room_id（多嵌套一层 room_info）。
        using var resp = await _http.GetAsync(
            $"https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByRoom?room_id={roomId}", ct);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement;
        if (root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var code) && code != 0)
        {
            if (code == -352)
                throw new InvalidOperationException("房间解析失败：被B站风控（code -352）。匿名连接已不可用，必须扫码登录。");
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : $"code {code}";
            throw new InvalidOperationException($"房间解析失败：{msg}（code {code}）");
        }
        if (root.TryGetProperty("data", out var data)
            && data.TryGetProperty("room_info", out var ri)
            && ri.TryGetProperty("room_id", out var ridEl)
            && ridEl.TryGetInt32(out var real))
            return real;

        // code=0 但缺 room_info.room_id：cookie 过期 / 被风控 / 房间异常等。给人话提示，而不是裸 KeyNotFoundException。
        throw new InvalidOperationException("房间解析失败：B站返回数据异常。cookie 可能已过期或被风控，请重新扫码登录后重试。");
    }

    private async Task<(string token, string wss)> GetDanmu(int realId, CancellationToken ct)
    {
        // getDanmuInfo 强制 WBI 签名：缺 w_rid 即 -352（实测带 cookie+UA 仍 -352，加 w_rid → code:0）。
        var mixinKey = await GetWbiMixinKey(ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signed = WbiSign.Sign(
            new Dictionary<string, string> { ["id"] = realId.ToString(), ["type"] = "0" }, mixinKey, now);
        using var resp = await _http.GetAsync(
            $"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?{signed}", ct);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement;

        if (root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var code) && code != 0)
        {
            var msg = root.TryGetProperty("message", out var mm) ? mm.GetString() : $"code {code}";
            _log?.Invoke($"B站 getDanmuInfo 仍被拒（已带 w_rid）：code={code}，message={msg}");
            throw new InvalidOperationException($"获取弹幕服务失败：B站返回 code {code}（{msg}）。可尝试重新扫码登录。");
        }
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("token", out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("获取弹幕服务失败：B站返回数据异常。cookie 可能已过期或被风控，请重新扫码登录后重试。");
        if (!data.TryGetProperty("host_list", out var hosts) || hosts.GetArrayLength() == 0
            || !hosts[0].TryGetProperty("host", out var h) || h.ValueKind != JsonValueKind.String
            || !hosts[0].TryGetProperty("wss_port", out var portEl) || !portEl.TryGetInt32(out var port))
            throw new InvalidOperationException("获取弹幕服务失败：B站未返回 wss 地址（建议扫码登录后重试）。");
        return (tokenEl.GetString()!, $"wss://{h.GetString()}:{port}/sub");
    }

    // 从 nav 取全站统一的 WBI 口令（img_key/sub_key），合成 mixin_key。匿名可得；口令约每日更替，
    // 故每次连接现取（连接不频繁，避免缓存过期）。getDanmuInfo 自 2026-08 起强制 WBI 鉴权。
    private async Task<string> GetWbiMixinKey(CancellationToken ct)
    {
        static string KeyFromUrl(string url)
        {
            var name = url[(url.LastIndexOf('/') + 1)..];
            return name.EndsWith(".png", StringComparison.Ordinal) ? name[..^4] : name;
        }
        using var resp = await _http.GetAsync("https://api.bilibili.com/x/web-interface/nav", ct);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("wbi_img", out var wbi)
            || !wbi.TryGetProperty("img_url", out var imgEl) || imgEl.ValueKind != JsonValueKind.String
            || !wbi.TryGetProperty("sub_url", out var subEl) || subEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("获取 WBI 签名口令失败：nav 接口未返回 img/sub（网络异常或接口变更）。");
        return WbiSign.GetMixinKey(KeyFromUrl(imgEl.GetString()!), KeyFromUrl(subEl.GetString()!));
    }

    private static StringDictionary ParseCookie(string cookie)
    {
        var d = new StringDictionary();
        foreach (var part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = part.IndexOf('=');
            if (i > 0) d[part[..i].Trim()] = part[(i + 1)..].Trim();
        }
        return d;
    }

    // 仅判断 cookie 里是否含某字段（取键名出现即视为「有」，不输出值——避免凭证落日志）。
    private static string Has(string? cookie, string key) =>
        cookie is not null && cookie.Contains(key + "=", StringComparison.Ordinal) ? "有" : "无";
}
