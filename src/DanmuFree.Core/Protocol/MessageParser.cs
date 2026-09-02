using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DanmuFree.Core.Models;

namespace DanmuFree.Core.Protocol;

/// <summary>
/// Parses a <see cref="BiliPacket"/> into a <see cref="RichMessage"/>. op 3 heartbeat
/// replies carry a big-endian int32 online count; op 5 packets carry a JSON object whose
/// <c>cmd</c> field selects the message kind. Any parse failure (malformed body, unknown
/// <c>cmd</c>, unexpected schema drift) yields <c>null</c> rather than throwing — B站
/// JSON field paths change over time and a single bad frame must not break the stream.
/// </summary>
public sealed class MessageParser
{
    public RichMessage? Parse(BiliPacket packet) => ParseAll(packet).FirstOrDefault();

    /// <summary>批量契约：一帧可产出多条消息（如 SEND_GIFT_V2 的 gift_list 重复字段）。</summary>
    public IReadOnlyList<RichMessage> ParseAll(BiliPacket packet)
    {
        try
        {
            return packet.Operation switch
            {
                3 => Wrap(ParseOnlineCountBody(packet.Body)),
                5 => ParseJsonAll(packet.Body),
                _ => Array.Empty<RichMessage>(),
            };
        }
        catch { return Array.Empty<RichMessage>(); } // 单帧解析失败不影响后续
    }

    static IReadOnlyList<RichMessage> Wrap(RichMessage? m) =>
        m is null ? Array.Empty<RichMessage>() : new[] { m };

    static RichMessage? ParseOnlineCountBody(ReadOnlyMemory<byte> body)
    {
        if (body.Length < 4) return null;
        int count = BinaryPrimitives.ReadInt32BigEndian(body.Span[..4]);
        return new RichMessage(MessageType.OnlineCount, "", "当前在线", count.ToString(), DateTime.Now);
    }

    static IReadOnlyList<RichMessage> ParseJsonAll(ReadOnlyMemory<byte> body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("cmd", out var cmdEl)) return Array.Empty<RichMessage>();
        var cmd = cmdEl.GetString() ?? "";
        var now = DateTime.Now;
        // INTERACT_WORD 与升级版 INTERACT_WORD_V2（直播流现发 V2）共用解析；V1=data.type，V2=data.msg_type，
        // 两者都读，且兼容带后缀的 cmd（如 INTERACT_WORD_V2@xxx）。
        if (cmd.StartsWith("INTERACT_WORD", StringComparison.Ordinal))
            return Wrap(ParseInteract(root, now));
        // SEND_GIFT_V2（2026-07 灰度的新礼物协议）：一帧可带多条礼物（gift_list 重复字段），走批量路径。
        if (cmd == "SEND_GIFT_V2")
            return ParseGiftV2(root, now);
        return Wrap(cmd switch
        {
            "DANMU_MSG" => ParseDanmu(root, now),
            "SEND_GIFT" => ParseSendGift(root, now),
            "GUARD_BUY" => ParseGuardBuy(root, now),
            "SUPER_CHAT_MESSAGE" => ParseSuperChat(root, now),
            "ONLINE_GUEST_COUNT" => ParseOnlineGuestCount(root, now),
            _ => null,
        });
    }

    static RichMessage? ParseDanmu(JsonElement root, DateTime now)
    {
        var info = root.GetProperty("info");
        // info[1] is the danmu text (plain string per live B站 protocol);
        // info[2] = [uid, username, …].
        var text = info[1].GetString() ?? "";
        var user = info[2][1].GetString() ?? "";
        // info[3] = [medal_level(int), medal_name(string), anchor_name, room_id, …];
        // absent or [0,""] when the user wears no 粉丝勋章.
        string? medal = null;
        if (info.GetArrayLength() > 3 && info[3].GetArrayLength() > 1)
        {
            var medalName = info[3][1].GetString();
            if (!string.IsNullOrEmpty(medalName))
                medal = $"{medalName}·{info[3][0].GetInt32()}";
        }
        return new RichMessage(MessageType.Danmu, user, text, null, now, medal);
    }

    static RichMessage? ParseSendGift(JsonElement root, DateTime now)
    {
        var d = root.GetProperty("data");
        return new RichMessage(MessageType.Gift,
            d.GetProperty("uname").GetString() ?? "",
            "",
            $"{d.GetProperty("giftName").GetString()} x{d.GetProperty("num").GetInt32()}",
            now);
    }

    // 上舰/续费（舰长/提督/总督）按礼物路由：显示进通知窗、朗读走礼物聚合器（「xx 送了 舰长」）。
    // 字段名与 SEND_GIFT 不同：username / gift_name（num=购买/续费月数，通常 1）。
    static RichMessage? ParseGuardBuy(JsonElement root, DateTime now)
    {
        var d = root.GetProperty("data");
        return new RichMessage(MessageType.Gift,
            d.GetProperty("username").GetString() ?? "",
            "",
            $"{d.GetProperty("gift_name").GetString()} x{d.GetProperty("num").GetInt32()}",
            now);
    }

    static RichMessage? ParseSuperChat(JsonElement root, DateTime now)
    {
        var d = root.GetProperty("data");
        return new RichMessage(MessageType.SuperChat,
            d.GetProperty("user_info").GetProperty("uname").GetString() ?? "",
            d.GetProperty("message").GetString() ?? "",
            $"¥{d.GetProperty("price").GetInt32()}",
            now);
    }

    static RichMessage? ParseOnlineGuestCount(JsonElement root, DateTime now)
    {
        var d = root.GetProperty("data");
        return new RichMessage(MessageType.OnlineCount, "", "当前在线",
            d.GetProperty("online_count").GetInt32().ToString(), now);
    }

    // SEND_GIFT_V2：礼物数据编码在 data.pb（base64 protobuf，JSON 里没有 uname/giftName）。
    // 字段号跟踪 blivedm SendGiftBroadcast：顶层 f2=uname；f10=gift_list[]（重复），每项 f2=gift_name、f3=num。
    // 与 SEND_GIFT 产出同构（Gift + Extra「{名} x{N}」），显示/开关/聚合朗读全链路自动复用。
    // pb 缺失/解不出 → 空列表（不污染流）。
    static IReadOnlyList<RichMessage> ParseGiftV2(JsonElement root, DateTime now)
    {
        if (!root.TryGetProperty("data", out var d)) return Array.Empty<RichMessage>();
        if (!d.TryGetProperty("pb", out var pbEl) || pbEl.ValueKind != JsonValueKind.String)
            return Array.Empty<RichMessage>();
        var b64 = pbEl.GetString();
        if (string.IsNullOrEmpty(b64)) return Array.Empty<RichMessage>();
        var (uname, gifts) = DecodeGiftV2Pb(Convert.FromBase64String(b64));
        if (uname.Length == 0 || gifts.Count == 0) return Array.Empty<RichMessage>();
        var list = new RichMessage[gifts.Count];
        for (int k = 0; k < gifts.Count; k++)
            list[k] = new RichMessage(MessageType.Gift, uname, "", $"{gifts[k].name} x{gifts[k].num}", now);
        return list;
    }

    // 解码 SEND_GIFT_V2 的 data.pb：顶层只取 f2(uname, string) 与 f10(gift_item, 重复 submessage)，其余跳过。
    static (string uname, List<(string name, int num)> gifts) DecodeGiftV2Pb(byte[] pb)
    {
        string uname = "";
        var gifts = new List<(string name, int num)>();
        int i = 0;
        while (i < pb.Length)
        {
            if (!TryReadVarint(pb, ref i, out ulong key)) break;
            int field = (int)(key >> 3), wt = (int)(key & 7);
            if (wt == 0)
            {
                if (!TryReadVarint(pb, ref i, out _)) break;
            }
            else if (wt == 2)
            {
                if (!TryReadVarint(pb, ref i, out ulong len)) break;
                int l = (int)len;
                if (l < 0 || i + l > pb.Length) break;
                if (field == 2) uname = Encoding.UTF8.GetString(pb, i, l);
                else if (field == 10) gifts.Add(DecodeGiftV2Item(pb.AsSpan(i, l)));
                i += l;
            }
            else if (wt == 5) { if (i + 4 > pb.Length) break; i += 4; }
            else if (wt == 1) { if (i + 8 > pb.Length) break; i += 8; }
            else break;
        }
        return (uname, gifts);
    }

    // gift_list 单项：只取 f2(gift_name, string) 与 f3(num, varint)；其余（gift_id/price/gift_info 等）跳过。
    static (string name, int num) DecodeGiftV2Item(ReadOnlySpan<byte> item)
    {
        string name = ""; int num = 0;
        int i = 0;
        while (i < item.Length)
        {
            if (!TryReadVarint(item, ref i, out ulong key)) break;
            int field = (int)(key >> 3), wt = (int)(key & 7);
            if (wt == 0)
            {
                if (!TryReadVarint(item, ref i, out ulong v)) break;
                if (field == 3) num = v > int.MaxValue ? int.MaxValue : (int)v;
            }
            else if (wt == 2)
            {
                if (!TryReadVarint(item, ref i, out ulong len)) break;
                int l = (int)len;
                if (l < 0 || i + l > item.Length) break;
                if (field == 2) name = Encoding.UTF8.GetString(item.Slice(i, l));
                i += l;
            }
            else if (wt == 5) { if (i + 4 > item.Length) break; i += 4; }
            else if (wt == 1) { if (i + 8 > item.Length) break; i += 8; }
            else break;
        }
        return (name, num);
    }

    // varint 读取（ReadOnlySpan 重载，供 gift_list 子消息解码；字节数组版本见下）。
    static bool TryReadVarint(ReadOnlySpan<byte> d, ref int i, out ulong v)
    {
        v = 0; int s = 0;
        while (i < d.Length)
        {
            byte b = d[i++];
            v |= (ulong)(b & 0x7f) << s;
            if ((b & 0x80) == 0) return true;
            s += 7;
            if (s > 63) return false;
        }
        return false;
    }

    // INTERACT_WORD（旧）/ INTERACT_WORD_V2（现直播流发的）：msg_type 1=进入直播间, 2=关注了主播, 3=分享了直播间。
    //   - V2：交互信息编码在 data.pb（base64 protobuf），顶层字段 f2=uname、f5=msg_type、f1=uid；
    //     JSON 里没有 msg_type / uname，必须解码 pb（实测 7777 等房间）。
    //   - V1：直接 data.type（或 data.msg_type）+ data.uname。
    //   - 未知 / 缺字段类型返回 null（不污染流）。
    static RichMessage? ParseInteract(JsonElement root, DateTime now)
    {
        if (!root.TryGetProperty("data", out var d)) return null;
        int type;
        string uname;
        if (d.TryGetProperty("pb", out var pbEl) && pbEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(pbEl.GetString()))
        {
            (uname, type) = DecodeInteractPb(Convert.FromBase64String(pbEl.GetString()!));
        }
        else
        {
            type = d.TryGetProperty("msg_type", out var mt) ? mt.GetInt32()
                 : d.TryGetProperty("type", out var t) ? t.GetInt32() : 0;
            uname = d.TryGetProperty("uname", out var u) ? u.GetString() ?? "" : "";
        }
        string text = type switch
        {
            1 => "进入直播间",
            2 => "关注了主播",
            3 => "分享了直播间",
            _ => "",
        };
        if (text.Length == 0) return null;
        return new RichMessage(MessageType.Interact, uname, text, null, now);
    }

    // 解码 INTERACT_WORD_V2 的 data.pb：只取顶层 f2(uname, string) 与 f5(msg_type, varint)，其余跳过。
    static (string uname, int msgType) DecodeInteractPb(byte[] pb)
    {
        string uname = "";
        int msgType = 0;
        int i = 0;
        while (i < pb.Length)
        {
            if (!TryReadVarint(pb, ref i, out ulong key)) break;
            int field = (int)(key >> 3), wt = (int)(key & 7);
            if (wt == 0)
            {
                if (!TryReadVarint(pb, ref i, out ulong v)) break;
                if (field == 5) msgType = (int)v;
            }
            else if (wt == 2)
            {
                if (!TryReadVarint(pb, ref i, out ulong len)) break;
                int l = (int)len;
                if (l < 0 || i + l > pb.Length) break;
                if (field == 2) uname = Encoding.UTF8.GetString(pb, i, l);
                i += l;
            }
            else if (wt == 5) { if (i + 4 > pb.Length) break; i += 4; }
            else if (wt == 1) { if (i + 8 > pb.Length) break; i += 8; }
            else break;
        }
        return (uname, msgType);
    }

    static bool TryReadVarint(byte[] d, ref int i, out ulong v)
    {
        v = 0; int s = 0;
        while (i < d.Length)
        {
            byte b = d[i++];
            v |= (ulong)(b & 0x7f) << s;
            if ((b & 0x80) == 0) return true;
            s += 7;
            if (s > 63) return false;
        }
        return false;
    }
}
