using System.IO.Compression;
namespace DanmuFree.Core.Protocol;

public static class PayloadCodec
{
    public static byte[] Decompress(ushort version, ReadOnlyMemory<byte> body) => version switch
    {
        2 => Inflate(body, s => new ZLibStream(s, CompressionMode.Decompress)),
        3 => Inflate(body, s => new BrotliStream(s, CompressionMode.Decompress)),
        _ => body.ToArray(),
    };

    static byte[] Inflate(ReadOnlyMemory<byte> data, Func<Stream, Stream> open)
    {
        using var input = new MemoryStream(data.ToArray());
        using var decomp = open(input);
        using var output = new MemoryStream();
        decomp.CopyTo(output);
        return output.ToArray();
    }
}
