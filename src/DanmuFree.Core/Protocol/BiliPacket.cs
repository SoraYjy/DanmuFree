namespace DanmuFree.Core.Protocol;

/// <summary>
/// A leaf danmaku packet produced by <see cref="PacketDecoder"/>: the operation
/// code plus the (already decompressed, non-recursive) body payload.
/// </summary>
public readonly record struct BiliPacket(uint Operation, ReadOnlyMemory<byte> Body);
