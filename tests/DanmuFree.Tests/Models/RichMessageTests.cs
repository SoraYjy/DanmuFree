using DanmuFree.Core.Models;

namespace DanmuFree.Tests.Models;

public class RichMessageTests
{
    [Fact]
    public void Record_holds_all_fields()
    {
        var m = new RichMessage(MessageType.Danmu, "alice", "你好", null, new DateTime(2026,7,27,12,0,0));
        Assert.Equal(MessageType.Danmu, m.Type);
        Assert.Equal("alice", m.UserName);
        Assert.Equal("你好", m.Text);
        Assert.Null(m.Extra);
    }

    [Fact]
    public void MessageType_has_seven_kinds()
    {
        // Danmu/Gift/Interact/SuperChat/OnlineCount + RealOnlineCount/WatchedCount（B站 WS 实时统计推送）。
        Assert.Equal(7, Enum.GetNames<MessageType>().Length);
    }
}
