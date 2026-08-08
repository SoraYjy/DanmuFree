using System.Buffers.Binary;
using System.IO;
using System.Text;
using DanmuFree.Core.Models;
using DanmuFree.Core.Protocol;
namespace DanmuFree.Tests.Protocol;

public class MessageParserTests
{
    readonly MessageParser _parser = new();

    static BiliPacket Op5(string json) => new(5, Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Heartbeat_reply_op3_parses_online_count()
    {
        var body = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(body, 12345);
        var m = _parser.Parse(new BiliPacket(3, body))!;
        Assert.Equal(MessageType.OnlineCount, m.Type);
        Assert.Equal("12345", m.Extra);
    }

    [Fact]
    public void Danmu_msg_parses_text_and_user()
    {
        var json = """
        {"cmd":"DANMU_MSG","info":["","你好",["12345","alice"]]}
        """;
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.Danmu, m.Type);
        Assert.Equal("你好", m.Text);
        Assert.Equal("alice", m.UserName);
    }

    [Fact]
    public void Danmu_msg_with_medal()
    {
        var json = """{"cmd":"DANMU_MSG","info":["","你好",["123","alice"],[29,"德云色","主播名",545068]]}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.Danmu, m.Type);
        Assert.Equal("alice", m.UserName);
        Assert.Equal("德云色·29", m.Medal);
    }

    [Fact]
    public void Danmu_msg_without_medal_yields_null()
    {
        var json = """{"cmd":"DANMU_MSG","info":["","你好",["123","alice"],[0,""]]}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Null(m.Medal);
    }

    [Fact]
    public void Send_gift_parses_gift_and_count()
    {
        var json = """{"cmd":"SEND_GIFT","data":{"uname":"bob","giftName":"辣条","num":10}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.Gift, m.Type);
        Assert.Equal("bob", m.UserName);
        Assert.Equal("辣条 x10", m.Extra);
    }

    [Fact]
    public void Interact_word_enter_room()
    {
        var json = """{"cmd":"INTERACT_WORD","data":{"type":1,"uname":"carol"}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.Interact, m.Type);
        Assert.Equal("carol", m.UserName);
        Assert.Equal("进入直播间", m.Text);
    }

    [Fact]
    public void Interact_word_follow()
    {
        var json = """{"cmd":"INTERACT_WORD","data":{"type":2,"uname":"dave"}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.Interact, m.Type);
        Assert.Equal("dave", m.UserName);
        Assert.Equal("关注了主播", m.Text);
    }

    [Fact]
    public void Interact_word_v2_enter_room_decodes_pb()
    {
        // INTERACT_WORD_V2：交互信息编码在 data.pb（base64 protobuf），JSON 里没有 msg_type/uname。
        var m = _parser.Parse(Op5(V2Json(MakePb("eve", 1))))!;
        Assert.Equal(MessageType.Interact, m.Type);
        Assert.Equal("eve", m.UserName);
        Assert.Equal("进入直播间", m.Text);
    }

    [Fact]
    public void Interact_word_v2_follow_decodes_pb()
    {
        var m = _parser.Parse(Op5(V2Json(MakePb("frank", 2))))!;
        Assert.Equal("frank", m.UserName);
        Assert.Equal("关注了主播", m.Text);
    }

    [Fact]
    public void Interact_word_v2_share_decodes_pb()
    {
        var m = _parser.Parse(Op5(V2Json(MakePb("gina", 3))))!;
        Assert.Equal("分享了直播间", m.Text);
    }

    [Fact]
    public void Interact_word_v2_with_cmd_suffix_still_parsed()
    {
        // B站有时给 cmd 带后缀（@房间号等），前缀匹配需命中。
        var m = _parser.Parse(Op5(V2JsonSuffix(MakePb("hex", 1))))!;
        Assert.Equal(MessageType.Interact, m.Type);
        Assert.Equal("hex", m.UserName);
        Assert.Equal("进入直播间", m.Text);
    }

    [Fact]
    public void Interact_word_v2_unknown_msg_type_returns_null()
    {
        Assert.Null(_parser.Parse(Op5(V2Json(MakePb("x", 99)))));
    }

    [Fact]
    public void Interact_word_v2_real_wire_sample()
    {
        // 实测 7777 房间抓到的真实 V2 帧：uname=VictorNoob, msg_type=1（进入）。
        var json = """{"cmd":"INTERACT_WORD_V2","data":{"dmscore":22,"pb":"CKnX+AMSClZpY3Rvck5vb2IiAgMBKAEwrKIhOLT5tdMGQNqJhv37M0owCJW1lQQQIRoJ5b635LqR6ImyIKOI6AMoo4joAzC7jaYHOKOI6ANAAWCsoiFombcDYgB4h/ih3cuy5eMYmgEAsgHQAQip1/gDElgKClZpY3Rvck5vb2ISSmh0dHBzOi8vaTIuaGRzbGIuY29tL2Jmcy9mYWNlL2IzZjYxNWExNDc3MDljYTgxMmVmNTIyMTIzNjViZmIwMDBiYjcyNTMuanBnGmkKCeW+t+S6keiJshAhGKOI6AMgu42mByijiOgDMKOI6AM4weMBSAFQlbWVBGCZtwN6CSM0QzdERkY5OYIBCSM0QzdERkY5OYoBCSM0QzdERkY5OZIBByNGRkZGRkaaAQkjNEM3REZGRTYiAggbMgC6AQDCAQA="}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal("VictorNoob", m.UserName);
        Assert.Equal("进入直播间", m.Text);
    }

    // 构造 INTERACT_WORD_V2 的 data.pb：顶层 f2=uname(string)、f5=msg_type(varint)。
    static string MakePb(string uname, int msgType)
    {
        using var ms = new MemoryStream();
        var u = Encoding.UTF8.GetBytes(uname);
        WriteField(ms, 2, u);                       // f2 uname
        WriteVarint(ms, (5u << 3) | 0u);            // f5 key
        WriteVarint(ms, (ulong)msgType);            // f5 value
        return Convert.ToBase64String(ms.ToArray());
    }
    static string V2Json(string pb) =>
        $"{{\"cmd\":\"INTERACT_WORD_V2\",\"data\":{{\"pb\":\"{pb}\"}}}}";
    static string V2JsonSuffix(string pb) =>
        $"{{\"cmd\":\"INTERACT_WORD_V2@123\",\"data\":{{\"pb\":\"{pb}\"}}}}";
    static void WriteField(Stream ms, uint field, byte[] data)
    {
        WriteVarint(ms, (field << 3) | 2u);
        WriteVarint(ms, (uint)data.Length);
        ms.Write(data, 0, data.Length);
    }
    static void WriteVarint(Stream ms, ulong v)
    {
        while (v >= 0x80) { ms.WriteByte((byte)(v | 0x80)); v >>= 7; }
        ms.WriteByte((byte)v);
    }

    [Fact]
    public void Super_chat_parses_message_and_price()
    {
        var json = """{"cmd":"SUPER_CHAT_MESSAGE","data":{"user_info":{"uname":"dave"},"message":"加油","price":50}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.SuperChat, m.Type);
        Assert.Equal("dave", m.UserName);
        Assert.Equal("加油", m.Text);
        Assert.Equal("¥50", m.Extra);
    }

    [Fact]
    public void Online_guest_count_cmd()
    {
        var json = """{"cmd":"ONLINE_GUEST_COUNT","data":{"online_count":999}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.OnlineCount, m.Type);
        Assert.Equal("999", m.Extra);
    }

    [Fact]
    public void Unknown_cmd_returns_null_without_throwing()
    {
        var m = _parser.Parse(Op5("""{"cmd":"SOMETHING_NEW","data":{}}"""));
        Assert.Null(m);
    }

    [Fact]
    public void Unknown_op_returns_null()
    {
        Assert.Null(_parser.Parse(new BiliPacket(99, Array.Empty<byte>())));
    }
}
