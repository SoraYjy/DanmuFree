using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace DanmuFree.Tests.Helpers;

// 按 URL 子串匹配，返回预设响应。
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, HttpContent> _responses = new();
    private readonly ConcurrentDictionary<string, string[]> _setCookies = new();
    public string? LastCookieHeader { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastMethod { get; private set; }
    public string? LastRequestUri { get; private set; }

    public FakeHttpHandler When(string urlContains, string jsonBody)
    {
        _responses[urlContains] = new StringContent(jsonBody);
        return this;
    }

    // 任意 HttpContent（用于音频二进制响应：ByteArrayContent 等）。
    public FakeHttpHandler When(string urlContains, HttpContent content)
    {
        _responses[urlContains] = content;
        return this;
    }

    // 给匹配该 URL 子串的响应附加 Set-Cookie 响应头（模拟 B站登录 cookie 下发）。
    public FakeHttpHandler WithSetCookies(string urlContains, params string[] setCookieValues)
    {
        _setCookies[urlContains] = setCookieValues;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (req.Headers.TryGetValues("Cookie", out var cookies))
            LastCookieHeader = string.Join(";", cookies);
        LastMethod = req.Method.Method;
        LastRequestUri = req.RequestUri?.ToString();
        if (req.Content is not null)
            LastRequestBody = await req.Content.ReadAsStringAsync(ct);

        foreach (var kv in _responses)
        {
            if (req.RequestUri!.ToString().Contains(kv.Key))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = kv.Value };
                if (_setCookies.TryGetValue(kv.Key, out var scs))
                    resp.Headers.TryAddWithoutValidation("Set-Cookie", scs);
                return resp;
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
