using DanmuFree.Core.Tts;

namespace DanmuFree.Tests.Tts;

public class TtsErrorClassifierTests
{
    // —— Edge 侧错误（消息来自 App 层 client/runner 抛的 HttpRequestException 人话文案）——

    [Fact]
    public void Edge_server_outage_page()
    {
        var msg = "HTTP 500：微软 Edge 朗读服务端故障（返回服务中断错误页），不是本机问题，请稍后再试";
        Assert.Contains("服务端故障", TtsErrorClassifier.Classify(msg));
    }

    [Fact]
    public void Bing_panda_page_raw_text()
    {
        // 用户侧拿到原始英文错误时也能归到「微软故障」
        var t = TtsErrorClassifier.Classify("Unexpected server response: 200 It's not you, it's us");
        Assert.Contains("微软", t);
    }

    [Fact]
    public void Edge_403_clock_hint()
    {
        var msg = "Edge TTS 握手被拒（403）：已自动尝试 ±10 分钟时间桶仍失败——本机时钟偏差可能超 10 分钟";
        Assert.Contains("403", TtsErrorClassifier.Classify(msg));
        Assert.Contains("时钟", TtsErrorClassifier.Classify(msg));
    }

    [Fact]
    public void Timeout()
    {
        Assert.Contains("超时", TtsErrorClassifier.Classify("Edge TTS 超时（30s 未完成，可能网络不通或端点再变更）。"));
    }

    [Fact]
    public void Missing_runner_script()
    {
        Assert.Contains("文件缺失", TtsErrorClassifier.Classify("找不到 Edge 合成脚本：C:\\x\\edge\\edge_tts_runner.js（确认 edge/ 已随程序分发）"));
    }

    [Fact]
    public void Node_launch_failed()
    {
        Assert.Contains("文件缺失", TtsErrorClassifier.Classify("node 启动失败（分发自带 node/node.exe，开发机需 PATH 有 node）"));
    }

    // —— GPT-SoVITS / 网络层 ——（socket 错误常在 InnerException 里，Exception 重载要串起来看）

    [Fact]
    public void Local_server_refused_via_inner_exception()
    {
        var inner = new Exception("No connection could be made because the target machine actively refused it. (127.0.0.1:9880)");
        var ex = new HttpRequestException("网络错误", inner);
        Assert.Contains("服务不可达", TtsErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Gpt_ref_audio_invalid()
    {
        Assert.Contains("参考音频", TtsErrorClassifier.Classify("参考音频在3~10秒范围外，请更换！"));
    }

    [Fact]
    public void Unknown_message_truncated_head()
    {
        var t = TtsErrorClassifier.Classify(new string('x', 200));
        Assert.True(t.Length <= 61);
        Assert.EndsWith("…", t);
    }

    [Fact]
    public void Null_and_empty_safe()
    {
        Assert.Equal("", TtsErrorClassifier.Classify((Exception?)null));
        Assert.Equal("未知错误", TtsErrorClassifier.Classify("  "));
    }
}
