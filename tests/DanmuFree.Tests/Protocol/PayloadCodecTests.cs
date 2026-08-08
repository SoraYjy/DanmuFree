using System.IO.Compression;
using System.Text;
using DanmuFree.Core.Protocol;
namespace DanmuFree.Tests.Protocol;

public class PayloadCodecTests
{
    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Decompress_brotli_version3_roundtrip()
    {
        var json = Utf8("{\"cmd\":\"DANMU_MSG\"}");
        var compressed = Compress(json, stream => new BrotliStream(stream, CompressionLevel.Optimal));
        var result = PayloadCodec.Decompress(3, compressed);
        Assert.Equal(json, result);
    }

    [Fact]
    public void Decompress_zlib_version2_roundtrip()
    {
        var json = Utf8("{\"cmd\":\"SEND_GIFT\"}");
        var compressed = Compress(json, stream => new ZLibStream(stream, CompressionLevel.Optimal));
        var result = PayloadCodec.Decompress(2, compressed);
        Assert.Equal(json, result);
    }

    [Fact]
    public void Decompress_unknown_version_returns_body_unchanged()
    {
        var raw = Utf8("hello");
        Assert.Equal(raw, PayloadCodec.Decompress(0, raw));
    }

    static byte[] Compress(byte[] data, Func<Stream, Stream> wrap)
    {
        using var ms = new MemoryStream();
        using (var compressor = wrap(ms)) compressor.Write(data);
        return ms.ToArray();
    }
}
