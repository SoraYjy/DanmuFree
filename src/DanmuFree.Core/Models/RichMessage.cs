namespace DanmuFree.Core.Models;

// RealOnlineCount / WatchedCount：B站 WS 实时统计推送（ONLINE_RANK_COUNT / WATCHED_CHANGE，
// 真实在线人数与看过累计）。与 OnlineCount（op3 人气口径，实测失真）区分，避免误用旧通道。
public enum MessageType { Danmu, Gift, Interact, SuperChat, OnlineCount, RealOnlineCount, WatchedCount }

public sealed record RichMessage(
    MessageType Type,
    string UserName,
    string Text,
    string? Extra,
    DateTime Time,
    string? Medal = null);
