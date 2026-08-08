using System.IO.Compression;
using System.Text;
using DanmuFree.Core.Protocol;
namespace DanmuFree.Tests.Protocol;

public class PacketDecoderTests
{
    readonly PacketDecoder _decoder = new();

    [Fact]
    public void Decode_plain_frame_yields_leaf_packet()
    {
        var body = Encoding.UTF8.GetBytes("{\"cmd\":\"X\"}");
        var frame = FrameCodec.Encode(op: 5, version: 0, body);

        var packets = _decoder.Decode(frame, out var rem);

        Assert.Single(packets);
        Assert.Equal(5u, packets[0].Operation);
        Assert.Equal(body, packets[0].Body.ToArray());
        Assert.True(rem.IsEmpty);
    }

    [Fact]
    public void Decode_brotli_wrapped_multi_frames_yields_all_leaves()
    {
        // build two inner plain frames, concatenate, brotli-compress, wrap in one outer frame v3 op5
        byte[] inner = FrameCodec.Encode(5, 0, Utf8("{\"cmd\":\"A\"}"))
                .Concat(FrameCodec.Encode(5, 0, Utf8("{\"cmd\":\"B\"}"))).ToArray();
        byte[] compressed = BrotliCompress(inner);
        var outer = FrameCodec.Encode(op: 5, version: 3, compressed);

        var packets = _decoder.Decode(outer, out _);

        Assert.Equal(2, packets.Count);
        Assert.Contains("A", Utf8(packets[0].Body));
        Assert.Contains("B", Utf8(packets[1].Body));
    }

    [Fact]
    public void Decode_partial_outer_returns_remainder()
    {
        var full = FrameCodec.Encode(op: 5, version: 0, Utf8("{}"));
        var packets = _decoder.Decode(full[..^3], out var rem);
        Assert.Empty(packets);
        Assert.Equal(full.Length - 3, rem.Length);
    }

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    static string Utf8(ReadOnlyMemory<byte> b) => Encoding.UTF8.GetString(b.Span);
    static byte[] BrotliCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var b = new BrotliStream(ms, CompressionLevel.Optimal)) b.Write(data);
        return ms.ToArray();
    }
}
