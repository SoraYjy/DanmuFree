using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace DanmuFree.Core.Protocol;

/// <summary>抖音房间解析结果：真实 room_id（WS 用）+ ttwid（WS 握手必需）。</summary>
public sealed record DouyinRoomInfo(string RoomId, string Ttwid);

/// <summary>
/// 解析抖音直播间真实 room_id（长号，WS 用）+ ttwid：
/// 1) GET https://live.douyin.com/{web_rid} —— 从 Set-Cookie 抠 ttwid（WS 握手必需）。
/// 2) GET webcast/room/web/enter 接口 —— 返回 data.data[0].id_str（真实 room_id）。**不需要 a_bogus**。
/// 风格延续 B站 <see cref="RoomResolver"/>：构造注入 HttpClient，每个请求自带 UA/Referer。
/// </summary>
public sealed class DouyinRoomResolver
{
    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    // enter 接口的 id_str = 真实 room_id（15~25 位数字）；HTML 里的 roomId 是 SSR 占位 $undefined，不能用。
    private static readonly Regex IdStrRegex = new("\"id_str\"\\s*:\\s*\"(\\d{15,25})\"", RegexOptions.Compiled);

    private readonly HttpClient _http;
    public DouyinRoomResolver(HttpClient http) => _http = http;

    public async Task<DouyinRoomInfo> ResolveAsync(string webRid, CancellationToken ct)
    {
        // 1) 主页：抠 ttwid + 触发风控放行。
        string? ttwid = null;
        using (var req0 = new HttpRequestMessage(HttpMethod.Get, $"https://live.douyin.com/{webRid}"))
        {
            SetHeaders(req0);
            using var resp0 = await _http.SendAsync(req0, ct);
            if (resp0.Headers.TryGetValues("Set-Cookie", out var cookies0))
                foreach (var c in cookies0)
                    if (c.StartsWith("ttwid=", StringComparison.Ordinal))
                        ttwid = c.Split(';', '=')[1].Trim();
        }

        // 2) enter 接口：真实 room_id。
        var url = "https://live.douyin.com/webcast/room/web/enter/" +
                  "?aid=6383&app_name=douyin_web&live_id=1&device_platform=web&language=zh-CN" +
                  "&cookie_enabled=true&screen_width=1920&screen_height=1080&browser_language=zh-CN" +
                  "&browser_platform=Win32&browser_name=Chrome&browser_version=126.0.0.0" +
                  $"&web_rid={webRid}&enter_from=web_live&is_need_double_stream=false";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(req);
        req.Headers.Referrer = new Uri($"https://live.douyin.com/{webRid}");
        using var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        var match = IdStrRegex.Match(json);
        var roomId = match.Success ? match.Groups[1].Value : webRid; // 取不到则兜底用短号
        return new DouyinRoomInfo(roomId, ttwid ?? "");
    }

    static void SetHeaders(HttpRequestMessage req)
    {
        req.Headers.UserAgent.ParseAdd(UA);
        req.Headers.AcceptLanguage.TryParseAdd("zh-CN,zh;q=0.9");
    }
}
