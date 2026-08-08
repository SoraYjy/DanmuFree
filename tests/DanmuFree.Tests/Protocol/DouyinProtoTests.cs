using System.IO.Compression;
using System.Text;
using DanmuFree.Core.Models;
using DanmuFree.Core.Protocol;

namespace DanmuFree.Tests.Protocol;

// 用本地 mini-protobuf 编码器构造「真实结构」的抖音帧（Chat/Gift/RoomUserSeq → Message →
// Response → gzip → PushFrame），再交给 DouyinProto 解码回来，回归整条解码路径。
// 字段号 / 嵌套 / gzip 与线上 wire format 完全一致，并刻意混入噪声字段验证「跳过未知字段」。
public class DouyinProtoTests
{
    // ---- 本地编码原语 ----
    static void Wv(List<byte> b, ulong v) { while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; } b.Add((byte)v); }
    static void WVarintField(List<byte> b, int num, ulong v) { Wv(b, ((ulong)num << 3) | 0); Wv(b, v); }
    static void WBytesField(List<byte> b, int num, byte[] data) { Wv(b, ((ulong)num << 3) | 2); Wv(b, (ulong)data.Length); b.AddRange(data); }
    static void WStrField(List<byte> b, int num, string s) => WBytesField(b, num, Encoding.UTF8.GetBytes(s));
    static byte[] Build(Action<List<byte>> f) { var b = new List<byte>(); f(b); return b.ToArray(); }

    static byte[] UserBytes(string nick, int gradeLevel = 0) => Build(b =>
    {
        WVarintField(b, 1, 999); WStrField(b, 3, nick); WVarintField(b, 4, 2);
        if (gradeLevel > 0)
        {
            // pay_grade(f23) 图标 URL，等级数字嵌在文件名里（实测线上即此格式）
            var payGrade = Build(p => WStrField(p, 1, $"webcast/new_user_grade_level_v1_{gradeLevel}.png"));
            WBytesField(b, 23, payGrade);
        }
    });
    static byte[] ChatBytes(string nick, string content, int gradeLevel = 0) => Build(b => { WBytesField(b, 1, Array.Empty<byte>()); WBytesField(b, 2, UserBytes(nick, gradeLevel)); WStrField(b, 3, content); });
    static byte[] GiftStructBytes(string name) => Build(b => { WStrField(b, 16, name); WVarintField(b, 12, 9); });
    static byte[] GiftBytes(string nick, string name, long repeat, long total) =>
        Build(b => { WBytesField(b, 7, UserBytes(nick)); WBytesField(b, 15, GiftStructBytes(name)); WVarintField(b, 5, (ulong)repeat); if (total > 0) WVarintField(b, 29, (ulong)total); });
    static byte[] RoomUserSeqBytes(long current, string cumulative) => Build(b => { WVarintField(b, 3, (ulong)current); if (!string.IsNullOrEmpty(cumulative)) WStrField(b, 11, cumulative); });
    static byte[] RoomStatsBytes(long online) => Build(b => { WVarintField(b, 5, (ulong)online); });
    static byte[] MessageBytes(string method, byte[] payload) => Build(b => { WStrField(b, 1, method); WBytesField(b, 2, payload); });
    static byte[] ResponseBytes(List<byte[]> msgs, bool needAck, string ext)
    {
        var b = new List<byte>();
        foreach (var m in msgs) WBytesField(b, 1, m);
        WStrField(b, 5, ext);
        if (needAck) WVarintField(b, 9, 1);
        return b.ToArray();
    }
    static byte[] GzipBytes(byte[] data) { using var ms = new MemoryStream(); using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) gz.Write(data); return ms.ToArray(); }
    static byte[] PushFrameBytes(ulong logId, byte[] response, bool gzip = true) =>
        Build(b => { WVarintField(b, 2, logId); WBytesField(b, 8, gzip ? GzipBytes(response) : response); });

    // 本地字段读取（用于断言 ack 的编码）
    static List<(int Num, int Wire, ulong Varint, byte[]? Bytes)> ReadFields(byte[] data)
    {
        var list = new List<(int, int, ulong, byte[]?)>();
        int i = 0;
        while (i < data.Length)
        {
            if (!TryVarint(data, ref i, out ulong key)) break;
            int num = (int)(key >> 3), wire = (int)(key & 7);
            if (num <= 0) break;
            if (wire == 0) { if (!TryVarint(data, ref i, out ulong v)) break; list.Add((num, wire, v, null)); }
            else if (wire == 2) { if (!TryVarint(data, ref i, out ulong len)) break; int l = (int)len; list.Add((num, wire, 0, data[i..(i + l)])); i += l; }
            else break;
        }
        return list;
    }
    static bool TryVarint(byte[] d, ref int i, out ulong v) { v = 0; int s = 0; while (i < d.Length) { byte b = d[i++]; v |= (ulong)(b & 0x7f) << s; if ((b & 0x80) == 0) return true; s += 7; if (s > 63) return false; } return false; }

    [Fact]
    public void ReadPushFrame_gunzips_payload_and_reads_log_id()
    {
        var resp = ResponseBytes(new() { MessageBytes("WebcastChatMessage", ChatBytes("测试用户", "你好")) }, false, "");
        var pf = DouyinProto.ReadPushFrame(PushFrameBytes(42, resp, gzip: true));
        Assert.NotNull(pf);
        Assert.Equal(42UL, pf!.LogId);
        Assert.Equal(resp, pf.Payload);
    }

    [Fact]
    public void ReadPushFrame_accepts_non_gzip_payload()
    {
        var resp = ResponseBytes(new() { MessageBytes("WebcastChatMessage", ChatBytes("A", "x")) }, false, "");
        var pf = DouyinProto.ReadPushFrame(PushFrameBytes(1, resp, gzip: false));
        Assert.Equal(resp, pf!.Payload);
    }

    [Fact]
    public void ReadResponse_decodes_messages_ext_needack()
    {
        var resp = ResponseBytes(new() {
            MessageBytes("WebcastChatMessage", ChatBytes("A", "x")),
            MessageBytes("WebcastMemberMessage", UserBytes("B")),
        }, needAck: true, "ext-token");
        var pf = DouyinProto.ReadPushFrame(PushFrameBytes(7, resp));
        var (msgs, needAck, ext) = DouyinProto.ReadResponse(pf!.Payload!);

        Assert.Equal(2, msgs.Count);
        Assert.True(needAck);
        Assert.Equal("ext-token", ext);
        Assert.Equal("WebcastChatMessage", msgs[0].Method);
        Assert.Equal("WebcastMemberMessage", msgs[1].Method);
    }

    [Fact]
    public void ReadChat_decodes_nick_content_level_ignoring_noise()
    {
        var (nick, content, level) = DouyinProto.ReadChat(ChatBytes("四川吴彦祖", "我的假牙", 42));
        Assert.Equal("四川吴彦祖", nick);
        Assert.Equal("我的假牙", content);
        Assert.Equal(42L, level); // User.level(f6)
    }

    [Fact]
    public void ReadNick_reads_user_from_member_payload()
    {
        var memberPayload = Build(b => { WBytesField(b, 2, UserBytes("进场哥")); WVarintField(b, 3, 5); });
        Assert.Equal("进场哥", DouyinProto.ReadNick(memberPayload));
    }

    [Fact]
    public void ReadGift_decodes_nick_name_and_count()
    {
        var (nick, name, count) = DouyinProto.ReadGift(GiftBytes("土豪", "嘉心糖", repeat: 3, total: 0));
        Assert.Equal("土豪", nick);
        Assert.Equal("嘉心糖", name);
        Assert.Equal(3L, count);
    }

    [Fact]
    public void ReadGift_prefers_total_count_over_repeat()
    {
        var (_, _, count) = DouyinProto.ReadGift(GiftBytes("U", "G", repeat: 2, total: 10));
        Assert.Equal(10L, count);
    }

    [Fact]
    public void ReadRoomUserSeq_reads_current_and_cumulative()
    {
        // f3=total(当前在线, varint), f11=total_pv_for_anchor(累计看过, **string** 已格式化如「1.3万」)
        var (current, cumulative) = DouyinProto.ReadRoomUserSeq(RoomUserSeqBytes(279, "1.3万"));
        Assert.Equal(279L, current);
        Assert.Equal("1.3万", cumulative);
    }

    [Fact]
    public void ReadRoomStatsOnline_reads_f5()
    {
        // RoomStatsMessage f5=count（display_long「1635在线观众」里的 N）
        Assert.Equal(1635L, DouyinProto.ReadRoomStatsOnline(RoomStatsBytes(1635)));
    }

    [Fact]
    public void BuildAck_encodes_logid_ack_internal_ext()
    {
        var ack = DouyinProto.BuildAck(99, "ext99");
        var fields = ReadFields(ack);
        Assert.Contains(fields, f => f.Num == 2 && f.Varint == 99);
        Assert.Contains(fields, f => f.Num == 7 && f.Bytes != null && Encoding.UTF8.GetString(f.Bytes) == "ack");
        Assert.Contains(fields, f => f.Num == 8 && f.Bytes != null && Encoding.UTF8.GetString(f.Bytes) == "ext99");
    }

    [Fact]
    public void BuildHeartbeat_is_pushframe_with_empty_gzip_payload()
    {
        var pf = DouyinProto.ReadPushFrame(DouyinProto.BuildHeartbeat());
        Assert.NotNull(pf);
        Assert.Empty(pf!.Payload!);
    }

    [Fact]
    public void Mapper_chat_to_danmu_with_level_medal()
    {
        var rm = DouyinMapper.ToRichMessage(new DouyinMessage("WebcastChatMessage", ChatBytes("尼", "hi", 7)));
        Assert.Equal(MessageType.Danmu, rm!.Type);
        Assert.Equal("尼", rm.UserName);
        Assert.Equal("hi", rm.Text);
        Assert.Equal("Lv.7", rm.Medal); // User.level 复用勋章位
    }

    [Fact]
    public void Mapper_chat_without_level_has_no_medal()
    {
        var rm = DouyinMapper.ToRichMessage(new DouyinMessage("WebcastChatMessage", ChatBytes("尼", "hi")));
        Assert.Null(rm!.Medal);
    }

    [Fact]
    public void Mapper_empty_chat_dropped()
    {
        Assert.Null(DouyinMapper.ToRichMessage(new DouyinMessage("WebcastChatMessage", ChatBytes("尼", ""))));
    }

    [Fact]
    public void Mapper_member_and_social_to_interact()
    {
        var member = DouyinMapper.ToRichMessage(new DouyinMessage("WebcastMemberMessage", UserBytes("甲")));
        Assert.Equal(MessageType.Interact, member!.Type);
        Assert.Equal("进入直播间", member.Text);

        var social = DouyinMapper.ToRichMessage(new DouyinMessage("WebcastSocialMessage", UserBytes("乙")));
        Assert.Equal(MessageType.Interact, social!.Type);
        Assert.Equal("关注了主播", social.Text);
    }

    [Fact]
    public void Mapper_gift_to_gift_message()
    {
        var gift = DouyinMapper.ToRichMessage(new DouyinMessage("WebcastGiftMessage", GiftBytes("土豪", "玫瑰", 3, 0)));
        Assert.Equal(MessageType.Gift, gift!.Type);
        Assert.Equal("土豪", gift.UserName);
        Assert.Equal("玫瑰 x3", gift.Extra);
    }

    [Fact]
    public void Mapper_stats_methods_not_in_message_stream()
    {
        // RoomUserSeq / RoomStats 由 DouyinDanmuClient.StatsUpdated 处理，mapper 不产出。
        Assert.Null(DouyinMapper.ToRichMessage(new DouyinMessage("WebcastRoomUserSeqMessage", RoomUserSeqBytes(8888, "9999"))));
        Assert.Null(DouyinMapper.ToRichMessage(new DouyinMessage("WebcastRoomStatsMessage", RoomStatsBytes(8888))));
    }

    [Fact]
    public void Mapper_unknown_method_dropped()
    {
        Assert.Null(DouyinMapper.ToRichMessage(new DouyinMessage("WebcastLikeMessage", new byte[] { 1, 2, 3 })));
        Assert.Null(DouyinMapper.ToRichMessage(new DouyinMessage("WebcastBannerMessage", null)));
    }
}
