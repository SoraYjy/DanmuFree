namespace DanmuFree.Core.Protocol;

/// <summary>
/// A single decoded Bilibili danmaku protocol frame.
/// Header layout (16 bytes, big-endian):
/// [packetLength:4][headerLength:2][protocolVersion:2][operation:4][sequence:4].
/// </summary>
public readonly record struct BiliFrame(uint Operation, ushort ProtocolVersion, ReadOnlyMemory<byte> Body);
