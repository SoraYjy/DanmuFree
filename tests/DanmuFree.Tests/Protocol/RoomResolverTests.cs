using DanmuFree.Core.Protocol;
using DanmuFree.Tests.Helpers;

namespace DanmuFree.Tests.Protocol;

public class RoomResolverTests
{
    private const string RoomInfoJson = """
    {"code":0,"data":{"room_info":{"room_id":7777,"short_id":123,"uid":555}}}
    """;
    private const string DanmuInfoJson = """
    {"code":0,"data":{"token":"ABC","host_list":[{"host":"broadcast-msg.chat.bilibili.com","wss_port":443}]}}
    """;

    [Fact]
    public async Task Resolve_returns_real_id_token_wss()
    {
        var handler = new FakeHttpHandler()
            .When("getInfoByRoom", RoomInfoJson)
            .When("getDanmuInfo", DanmuInfoJson);
        var resolver = new RoomResolver(new HttpClient(handler), cookie: null);

        var info = await resolver.ResolveAsync("123", CancellationToken.None);

        Assert.Equal(7777, info.RoomId);
        Assert.Equal("ABC", info.Token);
        Assert.Equal("wss://broadcast-msg.chat.bilibili.com:443/sub", info.WssUrl);
        Assert.Equal(0, info.Uid); // 匿名
    }

    [Fact]
    public async Task Resolve_extracts_uid_and_buvid3_from_cookie()
    {
        var handler = new FakeHttpHandler()
            .When("getInfoByRoom", RoomInfoJson)
            .When("getDanmuInfo", DanmuInfoJson);
        var cookie = "DedeUserID=555; buvid3=BBB; SESSDATA=xxx";
        var resolver = new RoomResolver(new HttpClient(handler), cookie);

        var info = await resolver.ResolveAsync("123", CancellationToken.None);

        Assert.Equal(555, info.Uid);
        Assert.Equal("BBB", info.Buvid3);
    }

    [Fact]
    public async Task Resolve_sends_cookie_header_on_each_request()
    {
        var handler = new FakeHttpHandler()
            .When("getInfoByRoom", RoomInfoJson)
            .When("getDanmuInfo", DanmuInfoJson);
        var cookie = "DedeUserID=555; buvid3=BBB; SESSDATA=xxx";
        var resolver = new RoomResolver(new HttpClient(handler), cookie);

        await resolver.ResolveAsync("123", CancellationToken.None);

        // Bug 1 防回归：cookie 必须真正作为 HTTP header 发出
        Assert.Equal(cookie, handler.LastCookieHeader);
    }

    [Fact]
    public async Task Resolve_throws_on_bad_room_code()
    {
        var handler = new FakeHttpHandler()
            .When("getInfoByRoom", """{"code":60004,"message":"房间不存在"}""");
        var resolver = new RoomResolver(new HttpClient(handler), null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("0", CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_reports_friendly_error_when_room_info_missing()
    {
        // 匿名风控等场景：code=0 但 data 缺 room_info —— 不能抛裸 KeyNotFoundException（用户看不懂）
        var handler = new FakeHttpHandler()
            .When("getInfoByRoom", """{"code":0,"data":{}}""");
        var resolver = new RoomResolver(new HttpClient(handler), null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("123", CancellationToken.None));
        Assert.Contains("扫码登录", ex.Message);
    }

    [Fact]
    public async Task Resolve_reports_friendly_error_when_danmu_token_missing()
    {
        var handler = new FakeHttpHandler()
            .When("getInfoByRoom", RoomInfoJson)
            .When("getDanmuInfo", """{"code":0,"data":{}}""");
        var resolver = new RoomResolver(new HttpClient(handler), null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("123", CancellationToken.None));
        Assert.Contains("扫码登录", ex.Message);
    }

    [Fact]
    public async Task Resolve_reports_risk_control_on_code_352()
    {
        // B站对匿名连接返回 code:-352（风控，2026-08 实测）：必须能看懂、指向登录。
        var handler = new FakeHttpHandler()
            .When("getInfoByRoom", """{"code":-352,"message":"-352"}""");
        var resolver = new RoomResolver(new HttpClient(handler), null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("123", CancellationToken.None));
        Assert.Contains("风控", ex.Message);
    }
}
