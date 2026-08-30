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
    public RichMessage? Parse(BiliPacket packet)
    {
        try
        {
            return packet.Operation switch
            {
                3 => ParseOnlineCountBody(packet.Body),
                5 => ParseJson(packet.Body),
                _ => null,
            };
        }
        catch { return null; } // 单帧解析失败不影响后续
    }

    static RichMessage? ParseOnlineCountBody(ReadOnlyMemory<byte> body)
    {
        if (body.Length < 4) return null;
        int count = BinaryPrimitives.ReadInt32BigEndian(body.Span[..4]);
        return new RichMessage(MessageType.OnlineCount, "", "当前在线", count.ToString(), DateTime.Now);
    }

    static RichMessage? ParseJson(ReadOnlyMemory<byte> body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("cmd", out var cmdEl)) return null;
        var cmd = cmdEl.GetString() ?? "";
        var now = DateTime.Now;
        // INTERACT_WORD 与升级版 INTERACT_WORD_V2（直播流现发 V2）共用解析；V1=data.type，V2=data.msg_type，
        // 两者都读，且兼容带后缀的 cmd（如 INTERACT_WORD_V2@xxx）。
        if (cmd.StartsWith("INTERACT_WORD", StringComparison.Ordinal))
            return ParseInteract(root, now);
        switch (cmd)
        {
            case "DANMU_MSG":
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
            case "SEND_GIFT":
            {
                var d = root.GetProperty("data");
                return new RichMessage(MessageType.Gift,
                    d.GetProperty("uname").GetString() ?? "",
                    "",
                    $"{d.GetProperty("giftName").GetString()} x{d.GetProperty("num").GetInt32()}",
                    now);
            }
            case "GUARD_BUY":
            {
                // 上舰/续费（舰长/提督/总督）按礼物路由：显示进通知窗、朗读走礼物聚合器（「xx 送了 舰长」）。
                // 字段名与 SEND_GIFT 不同：username / gift_name（num=购买/续费月数，通常 1）。
                var d = root.GetProperty("data");
                return new RichMessage(MessageType.Gift,
                    d.GetProperty("username").GetString() ?? "",
                    "",
                    $"{d.GetProperty("gift_name").GetString()} x{d.GetProperty("num").GetInt32()}",
                    now);
            }
            case "SUPER_CHAT_MESSAGE":
            {
                var d = root.GetProperty("data");
                return new RichMessage(MessageType.SuperChat,
                    d.GetProperty("user_info").GetProperty("uname").GetString() ?? "",
                    d.GetProperty("message").GetString() ?? "",
                    $"¥{d.GetProperty("price").GetInt32()}",
                    now);
            }
            case "ONLINE_GUEST_COUNT":
            {
                var d = root.GetProperty("data");
                return new RichMessage(MessageType.OnlineCount, "", "当前在线",
                    d.GetProperty("online_count").GetInt32().ToString(), now);
            }
            default:
                return null;
        }
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
