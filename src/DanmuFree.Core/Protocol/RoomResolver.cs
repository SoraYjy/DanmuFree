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
    private readonly HttpClient _http;
    private readonly string? _cookie;

    public RoomResolver(HttpClient http, string? cookie)
    {
        _http = http;
        _cookie = cookie;
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

        int realId = await GetRealRoomId(roomIdInput, ct);
        var (token, wss) = await GetDanmu(realId, ct);
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
        using var resp = await _http.GetAsync(
            $"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id={realId}&type=0", ct);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement;
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("token", out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("获取弹幕服务失败：B站返回数据异常。cookie 可能已过期或被风控，请重新扫码登录后重试。");
        if (!data.TryGetProperty("host_list", out var hosts) || hosts.GetArrayLength() == 0
            || !hosts[0].TryGetProperty("host", out var h) || h.ValueKind != JsonValueKind.String
            || !hosts[0].TryGetProperty("wss_port", out var portEl) || !portEl.TryGetInt32(out var port))
            throw new InvalidOperationException("获取弹幕服务失败：B站未返回 wss 地址（建议扫码登录后重试）。");
        return (tokenEl.GetString()!, $"wss://{h.GetString()}:{port}/sub");
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
}
