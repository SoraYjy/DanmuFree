namespace DanmuFree.Core.Protocol;

/// <summary>
/// Recursively decodes Bilibili danmaku frames into flat leaf packets.
/// Frames whose <see cref="BiliFrame.ProtocolVersion"/> is 2 (zlib) or 3 (brotli)
/// carry a compressed stream of nested sub-frames; this decoder decompresses them
/// and recurses. All other versions are leaf packets yielded directly as
/// <see cref="BiliPacket"/> (operation, body).
/// </summary>
public sealed class PacketDecoder
{
    /// <summary>
    /// Decodes <paramref name="buffer"/> into a flat list of leaf packets.
    /// Any trailing incomplete frame is returned via <paramref name="remainder"/>
    /// (propagated from the top-level <see cref="FrameCodec.Parse"/> call).
    /// </summary>
    public IReadOnlyList<BiliPacket> Decode(ReadOnlyMemory<byte> buffer, out ReadOnlyMemory<byte> remainder)
    {
        var frames = FrameCodec.Parse(buffer, out remainder);
        var packets = new List<BiliPacket>();
        foreach (var f in frames)
        {
            if (f.ProtocolVersion is 2 or 3)
            {
                var decompressed = PayloadCodec.Decompress(f.ProtocolVersion, f.Body);
                // Decompressed bytes are themselves a sequence of sub-frames; recurse
                // and ignore any sub-remainder (sub-frames are complete by construction).
                packets.AddRange(Decode(decompressed, out _));
            }
            else
            {
                packets.Add(new BiliPacket(f.Operation, f.Body));
            }
        }
        return packets;
    }
}
