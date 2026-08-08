using DanmuFree.Core.Tts;

namespace DanmuFree.Tests.Tts;

public class GiftReadAggregatorTests
{
    // —— TryParse ——
    [Theory]
    [InlineData("佛跳墙 x2", "佛跳墙", 2)]
    [InlineData("辣条 x10", "辣条", 10)]
    public void Parse_name_and_count(string extra, string name, int count)
    {
        Assert.True(GiftReadAggregator.TryParse(extra, out var n, out var c));
        Assert.Equal(name, n);
        Assert.Equal(count, c);
    }

    [Theory]
    [InlineData("礼物", "礼物", 1)]      // 抖音兜底：无数量 → 1
    [InlineData("小电视", "小电视", 1)]
    public void Parse_without_count_defaults_one(string extra, string name, int count)
    {
        Assert.True(GiftReadAggregator.TryParse(extra, out var n, out var c));
        Assert.Equal(name, n);
        Assert.Equal(count, c);
    }

    [Fact]
    public void Parse_empty_returns_false()
    {
        Assert.False(GiftReadAggregator.TryParse("", out _, out _));
        Assert.False(GiftReadAggregator.TryParse(null, out _, out _));
        Assert.False(GiftReadAggregator.TryParse("   ", out _, out _));
    }

    // —— Format：始终带用户名 ——
    [Fact]
    public void Format_single_omits_count()
    {
        Assert.Equal("张三 送了 辣条", GiftReadAggregator.Format("张三", "辣条", 1));
    }

    [Fact]
    public void Format_multiple_with_count()
    {
        Assert.Equal("张三 送了 3 个 辣条", GiftReadAggregator.Format("张三", "辣条", 3));
    }

    // —— 聚合状态机 ——
    [Fact]
    public void Same_user_gift_accumulates_no_intermediate_flush()
    {
        var a = new GiftReadAggregator();
        // 连送 5 个辣条（每条 x1，同用户同礼物）→ 中间都 null，不吐
        for (var i = 0; i < 5; i++)
            Assert.Null(a.Add("张三", "辣条 x1"));
        // 窗口到期 flush → 累计 5
        Assert.Equal("张三 送了 5 个 辣条", a.Flush());
    }

    [Fact]
    public void Multi_count_per_gift_accumulates()
    {
        var a = new GiftReadAggregator();
        Assert.Null(a.Add("张三", "佛跳墙 x2"));
        Assert.Null(a.Add("张三", "佛跳墙 x3"));  // 同组累加 → 5
        Assert.Equal("张三 送了 5 个 佛跳墙", a.Flush());
    }

    [Fact]
    public void Different_user_flushes_previous_immediately()
    {
        var a = new GiftReadAggregator();
        Assert.Null(a.Add("张三", "辣条 x1"));
        Assert.Null(a.Add("张三", "辣条 x2"));   // 张三 累计 3
        // 换用户：立即吐张三的，李四开始攒
        Assert.Equal("张三 送了 3 个 辣条", a.Add("李四", "辣条 x1"));
        Assert.Equal("李四 送了 辣条", a.Flush());
    }

    [Fact]
    public void Different_gift_flushes_previous_immediately()
    {
        var a = new GiftReadAggregator();
        Assert.Null(a.Add("张三", "辣条 x4"));
        // 同用户不同礼物：吐辣条，开始佛跳墙
        Assert.Equal("张三 送了 4 个 辣条", a.Add("张三", "佛跳墙 x1"));
        Assert.Equal("张三 送了 佛跳墙", a.Flush());
    }

    [Fact]
    public void Flush_empty_returns_null()
    {
        var a = new GiftReadAggregator();
        Assert.Null(a.Flush());
    }

    [Fact]
    public void Invalid_extra_does_not_pollute_state()
    {
        var a = new GiftReadAggregator();
        Assert.Null(a.Add("张三", "辣条 x2"));   // 攒
        Assert.Null(a.Add("张三", null));         // 无效 extra → 忽略，不影响累计
        Assert.Equal("张三 送了 2 个 辣条", a.Flush());
    }

    [Fact]
    public void Spam_100_gifts_produces_single_line()
    {
        var a = new GiftReadAggregator();
        for (var i = 0; i < 100; i++)
            Assert.Null(a.Add("张三", "辣条 x1"));   // 连送 100 个，中间一句都不出
        Assert.Equal("张三 送了 100 个 辣条", a.Flush());
    }
}
