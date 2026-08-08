using System.Buffers.Binary;

namespace DanmuFree.Core.Protocol;

/// <summary>
/// Encodes and decodes Bilibili danmaku binary frames.
/// Each frame carries a 16-byte big-endian header:
/// [packetLength:u32 BE][headerLength:u16 BE][protocolVersion:u16 BE][operation:u32 BE][sequence:u32 BE].
/// </summary>
public static class FrameCodec
{
    public const int HeaderLength = 16;

    /// <summary>
    /// Encodes a single frame: 16-byte header followed by the body.
    /// </summary>
    public static byte[] Encode(uint op, ushort version, ReadOnlyMemory<byte> body)
    {
        var buf = new byte[HeaderLength + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), (uint)buf.Length);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(4, 2), (ushort)HeaderLength);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(6, 2), version);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8, 4), op);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(12, 4), 1u); // sequence
        body.Span.CopyTo(buf.AsSpan(HeaderLength));
        return buf;
    }

    /// <summary>
    /// Parses one or more concatenated frames from <paramref name="buffer"/>.
    /// Any trailing incomplete frame is returned via <paramref name="remainder"/>.
    /// </summary>
    public static IReadOnlyList<BiliFrame> Parse(ReadOnlyMemory<byte> buffer, out ReadOnlyMemory<byte> remainder)
    {
        var frames = new List<BiliFrame>();
        int offset = 0;
        var span = buffer.Span;

        while (offset + HeaderLength <= buffer.Length)
        {
            uint packetLength = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));
            if (packetLength < HeaderLength || offset + packetLength > buffer.Length)
                break;

            ushort headerLen = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 4, 2));
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 6, 2));
            uint op = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + 8, 4));
            int bodyLen = (int)packetLength - headerLen;
            var body = buffer.Slice(offset + headerLen, bodyLen);
            frames.Add(new BiliFrame(op, version, body));
            offset += (int)packetLength;
        }

        remainder = buffer.Slice(offset);
        return frames;
    }
}
