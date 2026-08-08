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
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (root.GetProperty("code").GetInt32() != 0)
            throw new InvalidOperationException($"房间解析失败：{root.GetProperty("message").GetString()}");
        return root.GetProperty("data").GetProperty("room_info").GetProperty("room_id").GetInt32();
    }

    private async Task<(string token, string wss)> GetDanmu(int realId, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(
            $"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id={realId}&type=0", ct);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement;
        var data = root.GetProperty("data");
        var token = data.GetProperty("token").GetString()!;
        var host = data.GetProperty("host_list")[0];
        var wss = $"wss://{host.GetProperty("host").GetString()}:{host.GetProperty("wss_port").GetInt32()}/sub";
        return (token, wss);
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
