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
    public void Guard_buy_parses_as_gift_with_count()
    {
        // GUARD_BUY（上舰/续费：舰长/提督/总督）按礼物路由。字段名与 SEND_GIFT 不同：username / gift_name。
        var json = """{"cmd":"GUARD_BUY","data":{"uid":208259,"username":"神明X","guard_level":3,"num":1,"price":198000,"gift_id":10003,"gift_name":"舰长"}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal(MessageType.Gift, m.Type);
        Assert.Equal("神明X", m.UserName);
        Assert.Equal("舰长 x1", m.Extra);
    }

    [Fact]
    public void Guard_buy_renewal_months_in_count()
    {
        var json = """{"cmd":"GUARD_BUY","data":{"username":"bob","guard_level":2,"num":3,"gift_name":"提督"}}""";
        var m = _parser.Parse(Op5(json))!;
        Assert.Equal("bob", m.UserName);
        Assert.Equal("提督 x3", m.Extra);
    }

    [Fact]
    public void Send_gift_v2_decodes_pb_gift_list()
    {
        // SEND_GIFT_V2（2026-07 灰度的新礼物协议）：礼物编码在 data.pb（base64 protobuf），
        // 顶层 f2=uname、f10=gift_list[]（重复字段，一帧可带多条），每项 f2=gift_name、f3=num。
        var m = _parser.Parse(Op5(GiftV2Json(MakeGiftV2Pb("张三", ("人气票", 2)))))!;
        Assert.Equal(MessageType.Gift, m.Type);
        Assert.Equal("张三", m.UserName);
        Assert.Equal("人气票 x2", m.Extra);
    }

    [Fact]
    public void Send_gift_v2_batch_gifts_parse_all()
    {
        var all = _parser.ParseAll(Op5(GiftV2Json(MakeGiftV2Pb("bob", ("辣条", 10), ("小心心", 3)))));
        Assert.Equal(2, all.Count);
        Assert.Equal(MessageType.Gift, all[0].Type);
        Assert.Equal("bob", all[0].UserName);
        Assert.Equal("辣条 x10", all[0].Extra);
        Assert.Equal("小心心 x3", all[1].Extra);
        // Parse（单条契约）取首条，兼容旧调用。
        var first = _parser.Parse(Op5(GiftV2Json(MakeGiftV2Pb("bob", ("辣条", 10), ("小心心", 3)))))!;
        Assert.Equal("辣条 x10", first.Extra);
    }

    [Fact]
    public void Send_gift_v2_skips_unrelated_fields()
    {
        // pb 里夹着 uid/face/guard_level/勋章/盲盒等未用字段（varint、内嵌 submessage、string），
        // 解码器须跳过后仍取到 uname 与 gift_list。
        using var ms = new MemoryStream();
        WriteVarint(ms, (1u << 3) | 0u); WriteVarint(ms, 12345);          // f1 uid
        WriteField(ms, 3, Encoding.UTF8.GetBytes("https://face.jpg"));   // f3 face
        WriteVarint(ms, (5u << 3) | 0u); WriteVarint(ms, 3);             // f5 guard_level
        using var medal = new MemoryStream();
        WriteVarint(medal, (5u << 3) | 0u); WriteVarint(medal, 20);      // 勋章 f5 level
        WriteField(medal, 6, Encoding.UTF8.GetBytes("德云色"));          // 勋章 f6 name
        WriteField(ms, 8, medal.ToArray());                              // f8 medal_info
        using var item = new MemoryStream();
        WriteVarint(item, (1u << 3) | 0u); WriteVarint(item, 31536);     // item f1 gift_id
        WriteField(item, 2, Encoding.UTF8.GetBytes("人气票"));          // item f2 gift_name
        WriteVarint(item, (3u << 3) | 0u); WriteVarint(item, 1);         // item f3 num
        WriteField(item, 35, Encoding.UTF8.GetBytes("img"));             // item f35 gift_info
        WriteField(ms, 10, item.ToArray());                              // f10 gift_list
        WriteField(ms, 2, Encoding.UTF8.GetBytes("carol"));              // f2 uname

        var m = _parser.Parse(Op5(GiftV2Json(Convert.ToBase64String(ms.ToArray()))))!;
        Assert.Equal("carol", m.UserName);
        Assert.Equal("人气票 x1", m.Extra);
    }

    [Fact]
    public void Send_gift_v2_without_pb_yields_nothing()
    {
        Assert.Null(_parser.Parse(Op5("""{"cmd":"SEND_GIFT_V2","data":{"dmscore":5}}""")));
        Assert.Empty(_parser.ParseAll(Op5("""{"cmd":"SEND_GIFT_V2","data":{}}""")));
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

    // 构造 SEND_GIFT_V2 的 data.pb：顶层 f2=uname(string)、f10=gift_list[]，每项 f2=gift_name、f3=num。
    static string MakeGiftV2Pb(string uname, params (string Name, int Num)[] gifts)
    {
        using var ms = new MemoryStream();
        WriteField(ms, 2, Encoding.UTF8.GetBytes(uname));                // f2 uname
        foreach (var (name, num) in gifts)
        {
            using var item = new MemoryStream();
            WriteField(item, 2, Encoding.UTF8.GetBytes(name));           // item f2 gift_name
            WriteVarint(item, (3u << 3) | 0u);                           // item f3 key
            WriteVarint(item, (ulong)num);                               // item f3 num
            WriteField(ms, 10, item.ToArray());                          // f10 gift_list[]
        }
        return Convert.ToBase64String(ms.ToArray());
    }
    static string GiftV2Json(string pb) =>
        $"{{\"cmd\":\"SEND_GIFT_V2\",\"data\":{{\"pb\":\"{pb}\"}}}}";
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
