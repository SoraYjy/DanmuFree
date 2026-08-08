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
    public void MessageType_has_five_kinds()
    {
        Assert.Equal(5, Enum.GetNames<MessageType>().Length);
    }
}
