using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace DanmuFree.Core.Protocol;

/// <summary>
/// 一条抖音 WS message：method 名（如 WebcastChatMessage）+ 其 protobuf payload。
/// </summary>
public record DouyinMessage(string Method, byte[]? Payload);

/// <summary>抖音直播间统计。Online=当前在线(raw)；Watched=累计看过（服务端已格式化的显示串，如「1.3万」）。
/// null = 该消息未携带此项，不覆盖既有显示。</summary>
public sealed record DouyinRoomStats(long? Online, string? Watched);

/// <summary>
/// 抖音直播弹幕 protobuf 帧的编解码（手写 varint，零第三方）。字段号取自
/// saermart/protobuf/douyin.py 的 betterproto 定义并经探针实测确认：
///   PushFrame : f2=log_id, f8=payload(bytes, gzip)
///   Response  : f1=messages(repeated Message), f5=internal_ext(string), f9=need_ack(bool)
///   Message   : f1=method(string), f2=payload(bytes)
///   ChatMessage     : f2=user(User), f3=content(string)
///   MemberMessage   : f2=user(User)            （进场）
///   SocialMessage   : f2=user(User)            （关注）
///   GiftMessage     : f7=user(User), f15=gift(GiftStruct f16=name, f12=diamond_count), f5=repeat_count, f29=total_count
///   RoomUserSeqMessage : f3=total(int64)       （在线人数）
///   User            : f3=nick_name(string)
/// 风格延续 DanmuFree B站 MessageParser.DecodeInteractPb 的手写 varint。
/// </summary>
public static class DouyinProto
{
    /// <summary>解出的 PushFrame：log_id（ack 要回填）+ payload（已 gunzip 的 Response 字节）。</summary>
    public sealed class PushFrame { public ulong LogId; public byte[]? Payload; }

    /// <summary>读 PushFrame：取 f2=log_id、f8=payload（gzip 自动解压）。非 PushFrame 返回 null。</summary>
    public static PushFrame? ReadPushFrame(byte[] data)
    {
        var pf = new PushFrame();
        byte[]? raw = null;
        foreach (var f in DouyinRawPb.ReadFields(data))
        {
            if (f.Number == 2 && f.Wire == 0) pf.LogId = f.Varint;
            else if (f.Number == 8 && f.Wire == 2) raw ??= f.Bytes;
        }
        if (raw == null) return null;
        pf.Payload = IsGzip(raw) ? Gunzip(raw) : raw;
        return pf;
    }

    /// <summary>解 Response：f1=messages、f5=internal_ext、f9=need_ack。</summary>
    public static (List<DouyinMessage> Messages, bool NeedAck, string InternalExt) ReadResponse(byte[] resp)
    {
        var msgs = new List<DouyinMessage>();
        bool needAck = false;
        var internalExt = "";
        foreach (var f in DouyinRawPb.ReadFields(resp))
        {
            if (f.Number == 1 && f.Wire == 2)
            {
                string method = ""; byte[]? pl = null;
                foreach (var mf in DouyinRawPb.ReadFields(f.Bytes!))
                {
                    if (mf.Number == 1 && mf.Wire == 2) method = Utf8(mf.Bytes!);
                    else if (mf.Number == 2 && mf.Wire == 2) pl = mf.Bytes;
                }
                msgs.Add(new DouyinMessage(method, pl));
            }
            else if (f.Number == 5 && f.Wire == 2) internalExt = Utf8(f.Bytes!);
            else if (f.Number == 9 && f.Wire == 0) needAck = f.Varint != 0;
        }
        return (msgs, needAck, internalExt);
    }

    /// <summary>读 ChatMessage：f3=content、f2=user→(f3=nick_name, pay_grade 等级)。
    /// 抖音用户等级是 pay_grade(f23) 里的**图标**，等级数字嵌在图片 URL（new_user_grade_level_v1_N.png），
    /// User.level(f6) 实测几乎恒 0、不可用，故从 URL 抠 N。</summary>
    public static (string Nick, string Content, long Level) ReadChat(byte[] payload)
    {
        string nick = "", content = "";
        long level = 0;
        foreach (var f in DouyinRawPb.ReadFields(payload))
        {
            if (f.Number == 3 && f.Wire == 2) content = Utf8(f.Bytes!);
            else if (f.Number == 2 && f.Wire == 2)
            {
                nick = ReadUserNick(f.Bytes!);
                level = ReadUserGradeLevel(f.Bytes!);
            }
        }
        return (nick, content, level);
    }

    /// <summary>读含 user 的 message（Member/Social/CommonText 等）：取 f2=user→f3=nick_name。</summary>
    public static string ReadNick(byte[] payload)
    {
        foreach (var f in DouyinRawPb.ReadFields(payload))
            if (f.Number == 2 && f.Wire == 2) return ReadUserNick(f.Bytes!);
        return "";
    }

    /// <summary>读 GiftMessage：f7=user→nick、f15=gift→f16=name、f5=repeat_count/f29=total_count。</summary>
    public static (string Nick, string GiftName, long Count) ReadGift(byte[] payload)
    {
        string nick = "", giftName = "";
        long repeatCount = 0, totalCount = 0;
        foreach (var f in DouyinRawPb.ReadFields(payload))
        {
            if (f.Number == 7 && f.Wire == 2) nick = ReadUserNick(f.Bytes!);
            else if (f.Number == 15 && f.Wire == 2) giftName = ReadGiftName(f.Bytes!);
            else if (f.Number == 5 && f.Wire == 0) repeatCount = (long)f.Varint;
            else if (f.Number == 29 && f.Wire == 0) totalCount = (long)f.Varint;
        }
        var count = totalCount > 0 ? totalCount : (repeatCount > 0 ? repeatCount : 1);
        return (nick, giftName, count);
    }

    /// <summary>读 RoomUserSeqMessage：f3=total（当前观看/在线，varint）、f11=total_pv_for_anchor（累计看过，**string**，服务端已格式化如「1.3万」）。
    /// （字段语义取自 saermart _parseRoomUserSeqMsg：当前观看=total，累计观看=total_pv_for_anchor。）</summary>
    public static (long Current, string Cumulative) ReadRoomUserSeq(byte[] payload)
    {
        long current = 0;
        string cumulative = "";
        foreach (var f in DouyinRawPb.ReadFields(payload))
        {
            if (f.Number == 3 && f.Wire == 0) current = (long)f.Varint;
            else if (f.Number == 11 && f.Wire == 2) cumulative = Utf8(f.Bytes!);
        }
        return (current, cumulative);
    }

    /// <summary>读 RoomStatsMessage：f5=count（在线观众数，即 display_long「N在线观众」里的 N）。</summary>
    public static long ReadRoomStatsOnline(byte[] payload)
    {
        foreach (var f in DouyinRawPb.ReadFields(payload))
            if (f.Number == 5 && f.Wire == 0) return (long)f.Varint;
        return 0;
    }

    /// <summary>ack = PushFrame{ f2=log_id, f7="ack"(payload_type), f8=internal_ext(utf8, 不 gzip) }。
    /// 探针实测：缺 f7 → cursor 不推进、每帧重推状态；带 f7 → 正常推进。</summary>
    public static byte[] BuildAck(ulong logId, string internalExt)
    {
        using var ms = new MemoryStream();
        WriteVarintField(ms, 2, logId);                              // f2 log_id
        WriteBytesField(ms, 7, Encoding.UTF8.GetBytes("ack"));       // f7 payload_type="ack"
        WriteBytesField(ms, 8, Encoding.UTF8.GetBytes(internalExt)); // f8 payload = Response.internal_ext
        return ms.ToArray();
    }

    /// <summary>心跳 = 空 PushFrame{ f8=gzip(空) }。每 10s 发一次。</summary>
    public static byte[] BuildHeartbeat()
    {
        using var ms = new MemoryStream();
        WriteBytesField(ms, 8, Gzip(Array.Empty<byte>()));
        return ms.ToArray();
    }

    // ---- 字段级读取 ----
    static string ReadUserNick(byte[] userBytes)
    {
        foreach (var uf in DouyinRawPb.ReadFields(userBytes))
            if (uf.Number == 3 && uf.Wire == 2) return Utf8(uf.Bytes!);
        return "";
    }
    // 抖音用户等级：pay_grade 图标 URL 形如 .../new_user_grade_level_v1_21.png，抠末尾数字。
    static long ReadUserGradeLevel(byte[] userBytes)
    {
        foreach (var s in AllStringFields(userBytes))
        {
            var m = GradeLevelRegex.Match(s);
            if (m.Success && long.TryParse(m.Groups[1].ValueSpan, out var n)) return n;
        }
        return 0;
    }
    static readonly Regex GradeLevelRegex = new("grade_level_v\\d+_(\\d+)", RegexOptions.Compiled);

    // 递归收集所有合法 UTF-8 string 字段（用于在嵌套 message 里找图标 URL 等）。
    static IEnumerable<string> AllStringFields(byte[] data)
    {
        foreach (var f in DouyinRawPb.ReadFields(data))
        {
            if (f.Wire != 2 || f.Bytes is null) continue;
            if (TryUtf8(f.Bytes, out var s) && s.Length > 0) yield return s;
            else foreach (var sub in AllStringFields(f.Bytes)) yield return sub;
        }
    }
    static string ReadGiftName(byte[] giftBytes)
    {
        foreach (var gf in DouyinRawPb.ReadFields(giftBytes))
            if (gf.Number == 16 && gf.Wire == 2) return Utf8(gf.Bytes!);
        return "";
    }

    // ---- 编码原语 ----
    static void WriteVarintField(Stream ms, int num, ulong v) { WriteVarint(ms, ((ulong)num << 3) | 0); WriteVarint(ms, v); }
    static void WriteBytesField(Stream ms, int num, byte[] data) { WriteVarint(ms, ((ulong)num << 3) | 2); WriteVarint(ms, (ulong)data.Length); ms.Write(data, 0, data.Length); }
    static void WriteVarint(Stream ms, ulong v) { while (v >= 0x80) { ms.WriteByte((byte)(v | 0x80)); v >>= 7; } ms.WriteByte((byte)v); }

    // ---- gzip ----
    static bool IsGzip(byte[] d) => d.Length >= 2 && d[0] == 0x1f && d[1] == 0x8b;
    static byte[] Gunzip(byte[] d)
    {
        using var ms = new MemoryStream(d);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var o = new MemoryStream();
        gz.CopyTo(o);
        return o.ToArray();
    }
    static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(data);
        return ms.ToArray();
    }

    static string Utf8(byte[] b) { try { return Encoding.UTF8.GetString(b); } catch { return ""; } }
    static bool TryUtf8(byte[] b, out string s)
    {
        s = "";
        try
        {
            var decoded = Encoding.UTF8.GetString(b);
            if (!Encoding.UTF8.GetBytes(decoded).AsSpan().SequenceEqual(b)) return false; // 防乱码误判
            s = decoded;
            return true;
        }
        catch { return false; }
    }
}

// schemaless protobuf 读取器（等价 protoc --decode_raw）：按 wire format 逐字段拆，遇非法即停。
internal static class DouyinRawPb
{
    public sealed class Field { public int Number; public int Wire; public ulong Varint; public byte[]? Bytes; }

    public static List<Field> ReadFields(byte[] data)
    {
        var list = new List<Field>();
        int i = 0;
        while (i < data.Length)
        {
            if (!TryReadVarint(data, ref i, out ulong key)) break;
            int num = (int)(key >> 3);
            int wire = (int)(key & 7);
            if (num <= 0) break;
            var f = new Field { Number = num, Wire = wire };
            if (wire == 0) { if (!TryReadVarint(data, ref i, out ulong v)) break; f.Varint = v; }
            else if (wire == 1) { if (i + 8 > data.Length) break; f.Bytes = data[i..(i + 8)]; i += 8; }
            else if (wire == 5) { if (i + 4 > data.Length) break; f.Bytes = data[i..(i + 4)]; i += 4; }
            else if (wire == 2)
            {
                if (!TryReadVarint(data, ref i, out ulong len)) break;
                int l = (int)len;
                if (l < 0 || i + l > data.Length) break;
                f.Bytes = data[i..(i + l)];
                i += l;
            }
            else break; // wire 3/4 已废弃的 group，遇则停
            list.Add(f);
        }
        return list;
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
