using DanmuFree.Core.Models;

namespace DanmuFree.Core.Protocol;

// 抖音 method → DanmuFree RichMessage 的映射（纯函数，Core 单测）。
// Interact 的 Text 字面量「进入直播间」/「关注了主播」与 B站解析器一致，
// 供 DanmuViewModel 的 ShowEntry/ShowFollow 按动作路由复用。
public static class DouyinMapper
{
    public static RichMessage? ToRichMessage(DouyinMessage m)
    {
        if (m.Payload is null) return null;
        var now = DateTime.Now;
        switch (m.Method)
        {
            case "WebcastChatMessage":
            {
                var (nick, content, level) = DouyinProto.ReadChat(m.Payload);
                if (content.Length == 0) return null;
                // User.level(f6) 复用勋章位显示为「Lv.N」（抖音无文本勋章，medal 是图片）。
                string? medal = level > 0 ? $"Lv.{level}" : null;
                return new RichMessage(MessageType.Danmu, nick, content, null, now, medal);
            }
            case "WebcastMemberMessage":
                return new RichMessage(MessageType.Interact, DouyinProto.ReadNick(m.Payload), "进入直播间", null, now);
            case "WebcastSocialMessage":
                return new RichMessage(MessageType.Interact, DouyinProto.ReadNick(m.Payload), "关注了主播", null, now);
            case "WebcastGiftMessage":
            {
                var (nick, giftName, count) = DouyinProto.ReadGift(m.Payload);
                var extra = string.IsNullOrEmpty(giftName) ? "礼物" : $"{giftName} x{count}";
                return new RichMessage(MessageType.Gift, nick, "", extra, now);
            }
            // WebcastRoomUserSeqMessage / WebcastRoomStatsMessage 是统计，由 DouyinDanmuClient.StatsUpdated 处理，不进消息流。
            default:
                return null; // WebcastLikeMessage / Banner / Rank 等忽略
        }
    }
}
