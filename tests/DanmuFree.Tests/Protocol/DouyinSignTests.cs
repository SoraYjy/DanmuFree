using DanmuFree.Core.Protocol;

namespace DanmuFree.Tests.Protocol;

public class DouyinSignTests
{
    const string ExpectedParam =
        "live_id=1,aid=6383,version_code=180800,webcast_sdk_version=1.3.0,room_id=7668966157061409551," +
        "sub_room_id=,sub_channel_id=,did_rule=3,user_unique_id=1234567890123456789," +
        "device_platform=web,device_type=,ac=,identity=audience";

    [Fact]
    public void BuildParamString_uses_fixed_order_and_url_decodes()
    {
        var url = DouyinSign.BuildConnectUrl("7668966157061409551", "1234567890123456789");
        Assert.Equal(ExpectedParam, DouyinSign.BuildParamString(url));
    }

    [Fact]
    public void ComputeXBogusStub_is_md5_lower_hex()
    {
        // 独立计算锚点（bash md5sum），固定此值防回归：
        Assert.Equal("c1180e560e13ce8c5567b4002dbac516", DouyinSign.ComputeXBogusStub(ExpectedParam));
    }

    [Fact]
    public void AppendSignature_url_encodes_signature()
    {
        var url = DouyinSign.BuildConnectUrl("123", "456");
        var full = DouyinSign.AppendSignature(url, "ab c+");
        Assert.EndsWith("&signature=ab%20c%2B", full);
    }

    [Fact]
    public void BuildConnectUrl_uses_ws_web_host()
    {
        var url = DouyinSign.BuildConnectUrl("1", "2");
        Assert.StartsWith("wss://webcast3-ws-web-lf.douyin.com/webcast/im/push/v2/", url);
        Assert.Contains("room_id=1", url);
        Assert.Contains("user_unique_id=2", url);
    }
}
