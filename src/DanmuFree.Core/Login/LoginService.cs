using System.Net.Http;
using System.Text.Json;

namespace DanmuFree.Core.Login;

public sealed class LoginService
{
    private static readonly string[] CookieKeys = { "SESSDATA", "DedeUserID", "bili_jct" };
    private readonly HttpClient _http;
    public LoginService(HttpClient http) => _http = http;

    public async Task<QrInfo> GenerateAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/generate", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var data = doc.RootElement.GetProperty("data");
        return new QrInfo(data.GetProperty("url").GetString()!, data.GetProperty("qrcode_key").GetString()!);
    }

    public async Task<QrStatus> PollAsync(string qrcodeKey, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(
            $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={qrcodeKey}", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var data = doc.RootElement.GetProperty("data");
        var code = data.GetProperty("code").GetInt32();
        return code switch
        {
            0 => QrStatus.Success(BuildCookie(resp, data)),
            86090 => QrStatus.Scanned,
            86038 => QrStatus.Expired,
            _ => QrStatus.Waiting,
        };
    }

    public async Task<string?> GetBuvid3Async(CancellationToken ct)
    {
        using var resp = await _http.GetAsync("https://api.bilibili.com/x/frontend/finger/spi", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("data").GetProperty("b_3").GetString();
    }

    // B站 web 扫码登录的 cookie 通过 Set-Cookie 响应头下发（data.url 已不再携带 cookie 参数）。
    // 优先从 Set-Cookie 提取；data.url query 作为旧版兜底；按 SESSDATA/DedeUserID/bili_jct 固定顺序输出。
    private static string BuildCookie(HttpResponseMessage resp, JsonElement data)
    {
        var dict = new Dictionary<string, string>();
        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var sc in setCookies)
            {
                var pair = sc.Contains(';') ? sc[..sc.IndexOf(';')] : sc;
                var eq = pair.IndexOf('=');
                if (eq > 0)
                {
                    var k = pair[..eq];
                    if (Array.IndexOf(CookieKeys, k) >= 0)
                        dict[k] = pair[(eq + 1)..];
                }
            }
        }
        if (data.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
        {
            var url = urlEl.GetString();
            if (!string.IsNullOrEmpty(url) && url.Contains('?'))
            {
                foreach (var pair in url[(url.IndexOf('?') + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        var k = pair[..eq];
                        if (Array.IndexOf(CookieKeys, k) >= 0)
                            dict.TryAdd(k, pair[(eq + 1)..]);
                    }
                }
            }
        }
        return string.Join("; ", CookieKeys.Where(k => dict.ContainsKey(k)).Select(k => $"{k}={dict[k]}"));
    }
}
