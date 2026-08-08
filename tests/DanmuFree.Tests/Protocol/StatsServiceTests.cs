using DanmuFree.Core.Protocol;
using DanmuFree.Tests.Helpers;
namespace DanmuFree.Tests.Protocol;

public class StatsServiceTests
{
    private const string InfoJson = """
    {"code":0,"data":{"room_info":{"online":1234},"watched_show":{"num":5678},"like_info_v3":{"total_likes":9012}}}
    """;

    [Fact]
    public async Task GetAsync_parses_online_watched_likes()
    {
        var handler = new FakeHttpHandler().When("getInfoByRoom", InfoJson);
        var svc = new StatsService(new HttpClient(handler), cookie: null);
        var stats = await svc.GetAsync("1", CancellationToken.None);
        Assert.NotNull(stats);
        Assert.Equal(1234, stats!.Online);
        Assert.Equal(5678, stats.Watched);
        Assert.Equal(9012, stats.Likes);
    }

    [Fact]
    public async Task GetAsync_returns_null_on_bad_code()
    {
        var handler = new FakeHttpHandler().When("getInfoByRoom", """{"code":-400,"message":"请求错误"}""");
        var svc = new StatsService(new HttpClient(handler), null);
        Assert.Null(await svc.GetAsync("1", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_returns_null_on_missing_fields()
    {
        var handler = new FakeHttpHandler().When("getInfoByRoom", """{"code":0,"data":{}}""");
        var svc = new StatsService(new HttpClient(handler), null);
        Assert.Null(await svc.GetAsync("1", CancellationToken.None));
    }

    [Fact]
    public async Task Ctor_attaches_cookie_header()
    {
        var handler = new FakeHttpHandler().When("getInfoByRoom", InfoJson);
        var svc = new StatsService(new HttpClient(handler), "DedeUserID=1; SESSDATA=x");
        await svc.GetAsync("1", CancellationToken.None);
        Assert.Equal("DedeUserID=1; SESSDATA=x", handler.LastCookieHeader);
    }
}
