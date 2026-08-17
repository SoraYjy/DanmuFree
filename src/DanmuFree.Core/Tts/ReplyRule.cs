namespace DanmuFree.Core.Tts;

/// <summary>定向回复动作：念一段固定文字 / 播放一个本地音频文件（wav/mp3）。</summary>
public enum ReplyAction { SpeakText, PlaySound }

/// <summary>定向回复规则：弹幕正文命中 <see cref="Keyword"/> → 不读原弹幕，改为执行 <see cref="Action"/>
/// （念 <see cref="Text"/> / 播 <see cref="SoundPath"/>）。<see cref="Enabled"/>=false 的规则不参与匹配
/// （控制窗行首勾选框，临时禁用保留配置）。UI 可编辑行模型在 App 层，匹配前转换。</summary>
public sealed record ReplyRule(string Keyword, ReplyAction Action, string Text = "", string SoundPath = "", bool Enabled = true);

/// <summary>
/// 定向回复匹配器（纯逻辑，Core 单测）。规则按传入顺序（= 控制窗里从上往下）匹配，
/// **首条命中即返回**，后面的规则不再判断。无效规则（关键词空 / 对应动作的载荷空）跳过不参与匹配，
/// 不吃掉后续规则的命中。子串匹配、忽略大小写（对中文无影响，英文关键词 gg 能命中 GG）。
/// </summary>
public static class ReplyRuleMatcher
{
    public static ReplyRule? MatchFirst(IEnumerable<ReplyRule> rules, string message)
    {
        foreach (var r in rules)
        {
            if (!r.Enabled) continue;   // 被禁用的规则不参与匹配（临时关闭，配置保留）
            if (string.IsNullOrWhiteSpace(r.Keyword) || !HasPayload(r)) continue;
            if (message.Contains(r.Keyword, StringComparison.OrdinalIgnoreCase))
                return r;
        }
        return null;
    }

    private static bool HasPayload(ReplyRule r) => r.Action switch
    {
        ReplyAction.SpeakText => !string.IsNullOrWhiteSpace(r.Text),
        ReplyAction.PlaySound => !string.IsNullOrWhiteSpace(r.SoundPath),
        _ => false,
    };
}
