using DanmuFree.Core.Protocol;
using DanmuFree.Tests.Helpers;

namespace DanmuFree.Tests.Protocol;

public class DouyinRoomResolverTests
{
    private const string EnterJson = """
    {"status_code":0,"data":{"data":[{"id_str":"7669017417082850102","id":7669017417082850102}]}}
    """;

    [Fact]
    public async Task Resolve_extracts_room_id_and_ttwid()
    {
        var handler = new FakeHttpHandler()
            .When("live.douyin.com/256438100956", "<html>room</html>")
            .WithSetCookies("live.douyin.com/256438100956", "ttwid=ABC123; Path=/; Domain=.douyin.com")
            .When("webcast/room/web/enter", EnterJson);
        var resolver = new DouyinRoomResolver(new HttpClient(handler));

        var info = await resolver.ResolveAsync("256438100956", CancellationToken.None);

        Assert.Equal("7669017417082850102", info.RoomId);
        Assert.Equal("ABC123", info.Ttwid);
    }

    [Fact]
    public async Task Resolve_falls_back_to_web_rid_when_no_id_str()
    {
        var handler = new FakeHttpHandler()
            .When("live.douyin.com/256438100956", "<html>room</html>")
            .When("webcast/room/web/enter", """{"data":{}}""");
        var resolver = new DouyinRoomResolver(new HttpClient(handler));

        var info = await resolver.ResolveAsync("256438100956", CancellationToken.None);

        Assert.Equal("256438100956", info.RoomId); // 兜底用短号
        Assert.Equal("", info.Ttwid);
    }
}
