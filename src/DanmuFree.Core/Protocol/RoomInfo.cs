namespace DanmuFree.Core.Protocol;

/// <summary>
/// Resolved B站 live room connection info: the real numeric room id (resolved from a
/// short id), the danmu auth token, the websocket URL to connect to, plus the identity
/// fields (buvid3 / uid) parsed from the user cookie.
/// </summary>
public sealed record RoomInfo(int RoomId, string Token, string WssUrl, string? Buvid3, long Uid);
