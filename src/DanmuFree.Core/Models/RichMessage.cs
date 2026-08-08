namespace DanmuFree.Core.Models;

public enum MessageType { Danmu, Gift, Interact, SuperChat, OnlineCount }

public sealed record RichMessage(
    MessageType Type,
    string UserName,
    string Text,
    string? Extra,
    DateTime Time,
    string? Medal = null);
