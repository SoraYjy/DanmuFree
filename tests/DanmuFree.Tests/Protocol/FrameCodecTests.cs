using DanmuFree.Core.Protocol;

namespace DanmuFree.Tests.Protocol;

public class FrameCodecTests
{
    [Fact]
    public void Encode_writes_16_byte_header_big_endian()
    {
        byte[] body = { 1, 2, 3 };
        var frame = FrameCodec.Encode(op: 5, version: 1, body);

        // packet length = 16 header + 3 body
        Assert.Equal(19u, BinaryPrimitivesBE32(frame, 0));
        Assert.Equal(16, BinaryPrimitivesBE16(frame, 4));
        Assert.Equal(1, BinaryPrimitivesBE16(frame, 6));
        Assert.Equal(5u, BinaryPrimitivesBE32(frame, 8));
        Assert.Equal(body, frame[16..]);
    }

    [Fact]
    public void Parse_single_frame_returns_one_and_empty_remainder()
    {
        byte[] body = { 9, 9 };
        var frame = FrameCodec.Encode(op: 8, version: 0, body);
        var frames = FrameCodec.Parse(frame, out var rem);

        Assert.Single(frames);
        Assert.Equal(8u, frames[0].Operation);
        Assert.Equal(body, frames[0].Body.ToArray());
        Assert.True(rem.IsEmpty);
    }

    [Fact]
    public void Parse_two_concatenated_frames()
    {
        var a = FrameCodec.Encode(op: 3, version: 0, new byte[] { 0, 0, 0, 1 });
        var b = FrameCodec.Encode(op: 5, version: 1, new byte[] { 1 });
        var buf = a.Concat(b).ToArray();
        var frames = FrameCodec.Parse(buf, out var rem);

        Assert.Equal(2, frames.Count);
        Assert.Equal(3u, frames[0].Operation);
        Assert.Equal(5u, frames[1].Operation);
        Assert.True(rem.IsEmpty);
    }

    [Fact]
    public void Parse_partial_frame_returns_complete_part_as_remainder()
    {
        var full = FrameCodec.Encode(op: 5, version: 0, new byte[] { 1, 2 });
        var truncated = full[..^2]; // cut last 2 bytes -> incomplete
        var frames = FrameCodec.Parse(truncated, out var rem);

        Assert.Empty(frames);
        Assert.Equal(truncated.Length, rem.Length); // whole thing kept as remainder
    }

    static uint BinaryPrimitivesBE32(byte[] b, int off) =>
        (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
    static int BinaryPrimitivesBE16(byte[] b, int off) =>
        (b[off] << 8) | b[off + 1];
}
