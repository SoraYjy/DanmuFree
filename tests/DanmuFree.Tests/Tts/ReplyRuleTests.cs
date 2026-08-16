using DanmuFree.Core.Tts;

namespace DanmuFree.Tests.Tts;

public class ReplyRuleTests
{
    [Fact]
    public void No_rules_returns_null()
    {
        Assert.Null(ReplyRuleMatcher.MatchFirst(Array.Empty<ReplyRule>(), "主播好"));
    }

    [Fact]
    public void No_keyword_hit_returns_null()
    {
        var rules = new[] { new ReplyRule("怎么加好友", ReplyAction.SpeakText, "粉丝群号在简介里哦") };
        Assert.Null(ReplyRuleMatcher.MatchFirst(rules, "主播玩得真好"));
    }

    [Fact]
    public void Hit_returns_the_rule()
    {
        var rules = new[] { new ReplyRule("怎么加好友", ReplyAction.SpeakText, "粉丝群号在简介里哦") };
        var hit = ReplyRuleMatcher.MatchFirst(rules, "请问怎么加好友呀");
        Assert.NotNull(hit);
        Assert.Equal("粉丝群号在简介里哦", hit!.Text);
    }

    [Fact]
    public void First_hit_wins_even_if_later_rule_also_matches()
    {
        // 规则按上往下匹配，前面命中了就不判断后面的：两条都命中 → 返回第 1 条。
        var rules = new[]
        {
            new ReplyRule("主播", ReplyAction.SpeakText, "第一条"),
            new ReplyRule("游戏", ReplyAction.SpeakText, "第二条"),
        };
        var hit = ReplyRuleMatcher.MatchFirst(rules, "主播这游戏玩得真好");
        Assert.Equal("第一条", hit!.Text);
    }

    [Fact]
    public void Miss_falls_through_to_later_rule()
    {
        var rules = new[]
        {
            new ReplyRule("怎么加好友", ReplyAction.SpeakText, "群号在简介"),
            new ReplyRule("几点下播", ReplyAction.PlaySound, SoundPath: "xia.bo.wav"),
        };
        var hit = ReplyRuleMatcher.MatchFirst(rules, "今天几点下播");
        Assert.NotNull(hit);
        Assert.Equal(ReplyAction.PlaySound, hit!.Action);
    }

    [Fact]
    public void Match_is_substring_and_case_insensitive()
    {
        var rules = new[] { new ReplyRule("gg", ReplyAction.SpeakText, "别骂了别骂了") };
        var hit = ReplyRuleMatcher.MatchFirst(rules, "这把GG了");
        Assert.NotNull(hit);
    }

    [Fact]
    public void Empty_keyword_rule_is_skipped()
    {
        // 空关键词会匹配一切弹幕 → 视为无效规则跳过，继续看后面的（防误伤）。
        var rules = new[]
        {
            new ReplyRule("", ReplyAction.SpeakText, "全匹配"),
            new ReplyRule("  ", ReplyAction.SpeakText, "全空白"),
            new ReplyRule("唱歌", ReplyAction.SpeakText, "这就唱"),
        };
        var hit = ReplyRuleMatcher.MatchFirst(rules, "主播唱歌真好听");
        Assert.NotNull(hit);
        Assert.Equal("这就唱", hit!.Text);
    }

    [Fact]
    public void Speak_rule_with_empty_text_is_skipped()
    {
        // 载荷为空（没填要念的话）→ 无效规则跳过，不能吃掉命中。
        var rules = new[]
        {
            new ReplyRule("唱歌", ReplyAction.SpeakText, "  "),
            new ReplyRule("唱歌", ReplyAction.PlaySound, SoundPath: "sing.wav"),
        };
        var hit = ReplyRuleMatcher.MatchFirst(rules, "唱歌助兴");
        Assert.NotNull(hit);
        Assert.Equal(ReplyAction.PlaySound, hit!.Action);
    }

    [Fact]
    public void Sound_rule_with_empty_path_is_skipped()
    {
        var rules = new[]
        {
            new ReplyRule("唱歌", ReplyAction.PlaySound, SoundPath: ""),
            new ReplyRule("唱歌", ReplyAction.SpeakText, "这就唱"),
        };
        var hit = ReplyRuleMatcher.MatchFirst(rules, "唱歌助兴");
        Assert.NotNull(hit);
        Assert.Equal("这就唱", hit!.Text);
    }
}
