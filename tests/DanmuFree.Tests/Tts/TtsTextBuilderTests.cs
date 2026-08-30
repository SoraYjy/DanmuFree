using DanmuFree.Core.Models;
using DanmuFree.Core.Tts;

namespace DanmuFree.Tests.Tts;

public class TtsTextBuilderTests
{
    private static readonly DateTime T = new(2026, 8, 6, 12, 0, 0);

    private static RichMessage Msg(MessageType type, string user, string text, string? extra = null) =>
        new(type, user, text, extra, T);

    [Fact]
    public void Danmu_with_username()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "张三", "哈哈哈"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Equal("张三 说，哈哈哈", t);
    }

    [Fact]
    public void Danmu_without_username_just_text()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "", "哈哈哈"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Equal("哈哈哈", t);
    }

    [Fact]
    public void Danmu_skipped_when_flag_off()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "张三", "哈哈哈"),
            new TtsReadFlags(false, true, true), Array.Empty<string>(), 80);
        Assert.Null(t);
    }

    [Fact]
    public void SuperChat_announces_sender_and_price()
    {
        // SC 是事件型：念「xx 送了 N 元的 SC，内容」——用户名/价格必带（价格来自 Extra「¥30」）。
        var t = TtsTextBuilder.Build(Msg(MessageType.SuperChat, "李四", "感谢主播", "¥30"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Equal("李四 送了 30 元的 SC，感谢主播", t);
    }

    [Fact]
    public void SuperChat_without_price_still_announces()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.SuperChat, "李四", "感谢主播"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Equal("李四 送了 SC，感谢主播", t);
    }

    [Fact]
    public void Gift_not_handled_here_returns_null()
    {
        // 礼物朗读走 GiftReadAggregator（连送聚合 + 始终带用户名），Build 不处理 → null。
        var t = TtsTextBuilder.Build(Msg(MessageType.Gift, "王五", "", "佛跳墙 x2"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Null(t);
    }

    [Fact]
    public void Interact_never_read()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Interact, "路人", "进入直播间"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Null(t);
    }

    [Fact]
    public void OnlineCount_never_read()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.OnlineCount, "", "当前在线", "123"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Null(t);
    }

    [Fact]
    public void Blocked_word_drops_message()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "张三", "垃圾话"),
            new TtsReadFlags(true, true, true), new[] { "垃圾" }, 80);
        Assert.Null(t);
    }

    [Fact]
    public void Empty_text_returns_null()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "张三", ""),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Null(t);
    }

    [Fact]
    public void Long_text_truncated_to_maxLength_with_ellipsis()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "", new string('啊', 50)),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 10);
        Assert.Equal(new string('啊', 10) + "…", t);
    }

    // —— 读用户名开关（ReadUserName）：关掉则不带「xx 说，」前缀，只读正文 ——
    [Fact]
    public void Danmu_omits_username_when_readUser_off()
    {
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "张三", "哈哈哈"),
            new TtsReadFlags(true, true, true, ReadUserName: false), Array.Empty<string>(), 80);
        Assert.Equal("哈哈哈", t);
    }

    [Fact]
    public void SuperChat_username_read_regardless_of_readUser_flag()
    {
        // 「读用户名」开关只影响弹幕前缀；SC 恒带用户名（事件型，「谁送的」是语义必需，与礼物一致）。
        var t = TtsTextBuilder.Build(Msg(MessageType.SuperChat, "李四", "感谢主播", "¥30"),
            new TtsReadFlags(true, true, true, ReadUserName: false), Array.Empty<string>(), 80);
        Assert.Equal("李四 送了 30 元的 SC，感谢主播", t);
    }

    [Fact]
    public void Danmu_includes_username_when_readUser_on_by_default()
    {
        // TtsReadFlags 默认 ReadUserName=true：3 参构造保持旧行为。
        var t = TtsTextBuilder.Build(Msg(MessageType.Danmu, "张三", "哈哈哈"),
            new TtsReadFlags(true, true, true), Array.Empty<string>(), 80);
        Assert.Equal("张三 说，哈哈哈", t);
    }
}
