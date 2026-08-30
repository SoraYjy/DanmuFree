using DanmuFree.Core.Models;

namespace DanmuFree.Core.Tts;

/// <summary>
/// RichMessage → 朗读文本（纯函数，Core 单测）。只处理 Danmu / SuperChat：
/// ReadUserName 开 → "{用户} 说，{内容}"，关 → 仅 "{内容}"。
/// **Gift 不在此处理**——礼物朗读走 GiftReadAggregator（连送聚合 + 始终带用户名），
/// 由 EnqueueForTts 按 MessageType 分流。
/// 命中屏蔽词 / 类型未开 / 文本空 → null（不读）。超 maxLength 截断 + …。
/// </summary>
public static class TtsTextBuilder
{
    public static string? Build(RichMessage m, TtsReadFlags flags, IReadOnlyList<string> blockedWords, int maxLength)
    {
        string? raw = m.Type switch
        {
            MessageType.Danmu     => flags.Danmu ? Combine(m.UserName, m.Text, flags.ReadUserName) : null,
            MessageType.SuperChat => flags.SuperChat ? BuildSuperChat(m) : null,
            _ => null, // Gift 走聚合器（EnqueueForTts 分流）；Interact / OnlineCount 不读
        };
        if (string.IsNullOrWhiteSpace(raw)) return null;

        foreach (var w in blockedWords)
            if (!string.IsNullOrEmpty(w) && raw.Contains(w)) return null;

        return raw.Length <= maxLength ? raw : raw[..maxLength] + "…";
    }

    private static string Combine(string user, string text, bool readUser)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (!readUser || string.IsNullOrWhiteSpace(user)) return text;
        return $"{user} 说，{text}";
    }

    /// <summary>SC 朗读文本（事件型，与礼物一致恒带用户名）：「xx 送了 30 元的 SC，内容」。
    /// 价格来自 Extra（「¥30」→「30 元的」），缺价格则「xx 送了 SC，内容」；空正文返回 ""（不读）。</summary>
    private static string BuildSuperChat(RichMessage m)
    {
        if (string.IsNullOrWhiteSpace(m.Text)) return "";
        var price = m.Extra is { Length: > 1 } && m.Extra[0] == '¥' ? $"{m.Extra[1..]} 元的 " : "";
        var who = string.IsNullOrWhiteSpace(m.UserName) ? "" : $"{m.UserName} 送了 ";
        return $"{who}{price}SC，{m.Text}";
    }
}
