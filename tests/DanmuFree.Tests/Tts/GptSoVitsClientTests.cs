using System.Net.Http;
using DanmuFree.Core.Tts;
using DanmuFree.Tests.Helpers;

namespace DanmuFree.Tests.Tts;

public class GptSoVitsClientTests
{
    [Fact]
    public async Task Gets_tts_endpoint_with_query_and_returns_audio_bytes()
    {
        var wav = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new FakeHttpHandler().When("/tts", new ByteArrayContent(wav));
        var client = new GptSoVitsClient(new HttpClient(handler), "http://127.0.0.1:9880");

        var stream = await client.SynthesizeAsync("你好", new TtsOptions(RefAudioPath: "D:/r.wav"), CancellationToken.None);
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(wav, ms.ToArray());
        Assert.Equal("GET", handler.LastMethod);
        Assert.Contains("/tts?", handler.LastRequestUri);
        Assert.Contains("text=", handler.LastRequestUri);
        Assert.Contains("speed_factor=", handler.LastRequestUri);   // V2 语速字段名
        Assert.Contains("temperature=", handler.LastRequestUri);    // 语气表现力（采样温度）
    }

    [Fact]
    public async Task Sends_temperature_value_from_options()
    {
        var handler = new FakeHttpHandler().When("/tts", new ByteArrayContent(new byte[] { 1 }));
        var client = new GptSoVitsClient(new HttpClient(handler), "http://127.0.0.1:9880");

        await client.SynthesizeAsync("你好", new TtsOptions(RefAudioPath: "D:/r.wav", Temperature: 0.6), CancellationToken.None);

        Assert.Contains("temperature=0.6", handler.LastRequestUri);
    }

    [Fact]
    public async Task Always_sends_required_ref_fields_in_query()
    {
        // V2 的 ref_audio_path / prompt_lang 必填：始终在 query 发送（空 ref 由服务回 400，提示用户填）。
        var handler = new FakeHttpHandler().When("/tts", new ByteArrayContent(new byte[] { 1 }));
        var client = new GptSoVitsClient(new HttpClient(handler), "http://127.0.0.1:9880");

        await client.SynthesizeAsync("你好", new TtsOptions(), CancellationToken.None);

        Assert.Contains("ref_audio_path=", handler.LastRequestUri);
        Assert.Contains("prompt_lang=zh", handler.LastRequestUri);
        Assert.Contains("text_lang=zh", handler.LastRequestUri);
    }

    [Fact]
    public async Task Includes_ref_audio_and_prompt_text_when_set()
    {
        var handler = new FakeHttpHandler().When("/tts", new ByteArrayContent(new byte[] { 1 }));
        var client = new GptSoVitsClient(new HttpClient(handler), "http://127.0.0.1:9880");

        await client.SynthesizeAsync("你好",
            new TtsOptions(RefAudioPath: "D:/a.wav", PromptText: "你好"), CancellationToken.None);

        // ref_audio_path / prompt_text 经 URL encode，断言 key 存在
        Assert.Contains("ref_audio_path=", handler.LastRequestUri);
        Assert.Contains("prompt_text=", handler.LastRequestUri);
    }

    [Fact]
    public async Task Throws_on_error_status()
    {
        // 未 When 匹配 → FakeHttpHandler 返回 404 → EnsureSuccessStatusCode 抛
        var handler = new FakeHttpHandler();
        var client = new GptSoVitsClient(new HttpClient(handler), "http://127.0.0.1:9880");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SynthesizeAsync("你好", new TtsOptions(), CancellationToken.None));
        Assert.Contains("404", ex.Message);          // 失败时把状态码 + 服务返回写进异常，便于 FileLogger 辨识
        Assert.Contains("GPT-SoVITS", ex.Message);
    }
}
