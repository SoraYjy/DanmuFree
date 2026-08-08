using DanmuFree.Core.Login;
using DanmuFree.Tests.Helpers;
namespace DanmuFree.Tests.Login;

public class LoginServiceTests
{
    private const string GenerateJson = """
    {"code":0,"data":{"url":"https://account.bilibili.com/scan?qrcode_key=KEY123","qrcode_key":"KEY123"}}
    """;
    private const string SuccessPollJson = """
    {"code":0,"data":{"code":0,"url":"https://passport.biligame.com/x/passport-login/web/crossDomain?DedeUserID=19091214&DedeUserID__ckMd5=abc&Expires=0&SESSDATA=xyz&bili_jct=def&gourl=https%3A%2F%2F"}}
    """;
    // 真实 B站：成功响应的 url 不再带 cookie，cookie 走 Set-Cookie 头。
    private const string SuccessPollJsonUrlOnly = """
    {"code":0,"data":{"code":0,"url":"https://passport.biligame.com/x/passport-login/web/crossDomain?gourl=https%3A%2F%2Fwww.bilibili.com"}}
    """;

    [Fact]
    public async Task Generate_parses_url_and_key()
    {
        var h = new FakeHttpHandler().When("qrcode/generate", GenerateJson);
        var svc = new LoginService(new HttpClient(h));
        var info = await svc.GenerateAsync(CancellationToken.None);
        Assert.Equal("https://account.bilibili.com/scan?qrcode_key=KEY123", info.Url);
        Assert.Equal("KEY123", info.QrcodeKey);
    }

    [Theory]
    [InlineData(86101, QrState.Waiting)]
    [InlineData(86090, QrState.Scanned)]
    [InlineData(86038, QrState.Expired)]
    public async Task Poll_maps_codes(int code, QrState expected)
    {
        var json = $$$"""{"code":0,"data":{"code":{{{code}}},"url":""}}""";
        var h = new FakeHttpHandler().When("qrcode/poll", json);
        var svc = new LoginService(new HttpClient(h));
        var st = await svc.PollAsync("KEY", CancellationToken.None);
        Assert.Equal(expected, st.State);
    }

    [Fact]
    public async Task Poll_success_extracts_cookie_from_url_query()
    {
        var h = new FakeHttpHandler().When("qrcode/poll", SuccessPollJson);
        var svc = new LoginService(new HttpClient(h));
        var st = await svc.PollAsync("KEY", CancellationToken.None);
        Assert.Equal(QrState.Success, st.State);
        Assert.Equal("SESSDATA=xyz; DedeUserID=19091214; bili_jct=def", st.Cookie);
    }

    [Fact]
    public async Task Poll_success_extracts_cookie_from_set_cookie_header()
    {
        var h = new FakeHttpHandler()
            .When("qrcode/poll", SuccessPollJsonUrlOnly)
            .WithSetCookies("qrcode/poll",
                "SESSDATA=token123; Path=/; Domain=.bilibili.com; HttpOnly",
                "DedeUserID=19091214; Path=/; Domain=.bilibili.com",
                "bili_jct=jct456; Path=/; Domain=.bilibili.com; HttpOnly");
        var svc = new LoginService(new HttpClient(h));
        var st = await svc.PollAsync("KEY", CancellationToken.None);
        Assert.Equal(QrState.Success, st.State);
        Assert.Equal("SESSDATA=token123; DedeUserID=19091214; bili_jct=jct456", st.Cookie);
    }

    [Fact]
    public async Task Poll_success_prefers_set_cookie_over_url_query()
    {
        // url 带旧值（SESSDATA=xyz 等），Set-Cookie 带新值 → 应取 Set-Cookie。
        var h = new FakeHttpHandler()
            .When("qrcode/poll", SuccessPollJson)
            .WithSetCookies("qrcode/poll", "SESSDATA=hdr; Path=/", "DedeUserID=hdr; Path=/", "bili_jct=hdr; Path=/");
        var svc = new LoginService(new HttpClient(h));
        var st = await svc.PollAsync("KEY", CancellationToken.None);
        Assert.Equal("SESSDATA=hdr; DedeUserID=hdr; bili_jct=hdr", st.Cookie);
    }

    [Fact]
    public async Task GetBuvid3_parses_b_3()
    {
        var h = new FakeHttpHandler().When("finger/spi", """{"code":0,"data":{"b_3":"BUVID3VAL","b_4":"x"}}""");
        var svc = new LoginService(new HttpClient(h));
        var b = await svc.GetBuvid3Async(CancellationToken.None);
        Assert.Equal("BUVID3VAL", b);
    }
}
