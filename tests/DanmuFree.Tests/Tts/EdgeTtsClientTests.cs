using System.Text.RegularExpressions;
using DanmuFree.Core.Tts;

namespace DanmuFree.Tests.Tts;

public class EdgeTtsClientTests
{
    // ── Sec-MS-GEC token：参考向量由 Python hashlib 预算（锁死算法，防回归） ──

    [Fact]
    public void SecMsGec_is_uppercase_hex_sha256_of_known_vector()
    {
        // unix 1700000070 → ticks 133444737000000000 → SHA256 大写 hex（Python 预算）
        Assert.Equal("AE4CF72E466874182A75878E20EADA83D29A1C12CAD9C3E0E014CCE0BFA55880",
            EdgeTtsClient.BuildSecMsGec(1_700_000_070));
    }

    [Fact]
    public void SecMsGec_is_always_64_uppercase_hex()
    {
        var token = EdgeTtsClient.BuildSecMsGec(1_700_000_000);
        Assert.Matches(new Regex("^[0-9A-F]{64}$"), token);
    }

    [Fact]
    public void SecMsGec_is_deterministic()
    {
        Assert.Equal(EdgeTtsClient.BuildSecMsGec(1_234_567_890), EdgeTtsClient.BuildSecMsGec(1_234_567_890));
    }

    [Fact]
    public void SecMsGec_floors_to_300s_bucket()
    {
        // 跨过 300s 桶边界 → token 变；同桶内（含边界点往后 299s）→ token 不变。
        // 边界点：unix 1700000070（total 恰为 300 的倍数）。
        Assert.NotEqual(EdgeTtsClient.BuildSecMsGec(1_700_000_069), EdgeTtsClient.BuildSecMsGec(1_700_000_070));
        Assert.Equal(EdgeTtsClient.BuildSecMsGec(1_700_000_070), EdgeTtsClient.BuildSecMsGec(1_700_000_369));
        Assert.NotEqual(EdgeTtsClient.BuildSecMsGec(1_700_000_369), EdgeTtsClient.BuildSecMsGec(1_700_000_370));
    }

    // ── SSML 构造 ──

    [Fact]
    public void Ssml_contains_voice_and_text()
    {
        var ssml = EdgeTtsClient.BuildSsml("zh-CN-XiaoxiaoNeural", "你好世界", "+0%");
        Assert.Contains("zh-CN-XiaoxiaoNeural", ssml);
        Assert.Contains("你好世界", ssml);
        Assert.Contains("rate='+0%'", ssml);
    }

    [Fact]
    public void Ssml_escapes_xml_special_chars()
    {
        // 文本含 < & > ' " 必须转义，否则破坏 SSML / 注入
        var ssml = EdgeTtsClient.BuildSsml("zh-CN-XiaoxiaoNeural", "a<b>&c'\"d", "+0%");
        Assert.Contains("a&lt;b&gt;&amp;c&apos;&quot;d", ssml);
        Assert.DoesNotContain("a<b>&c'\"d", ssml);
    }

    [Fact]
    public void Ssml_has_speak_voice_prosody_structure()
    {
        var ssml = EdgeTtsClient.BuildSsml("zh-CN-YunxiNeural", "测试", "-50%");
        Assert.StartsWith("<speak", ssml);
        Assert.Contains("<voice ", ssml);
        Assert.Contains("<prosody ", ssml);
        Assert.Contains("rate='-50%'", ssml);
    }

    // ── 语速映射 ──

    [Theory]
    [InlineData(1.0, "+0%")]
    [InlineData(2.0, "+100%")]
    [InlineData(0.5, "-50%")]
    [InlineData(1.5, "+50%")]
    public void SpeedToRate_maps_to_signed_percent(double speed, string expected)
        => Assert.Equal(expected, EdgeTtsClient.SpeedToRate(speed));
}
