using System.Globalization;
using System.Net.Http;

namespace DanmuFree.Core.Tts;

/// <summary>
/// GPT-SoVITS api_v2 /tts 客户端。实测用 **GET + query 参数**返回 wav 流
///（POST JSON body 会被服务以「There was an error parsing the body」拒绝）。
/// ⚠ ref_audio_path 必填（空则服务回 400 ref_audio_path is required）；
/// 语速字段是 speed_factor；语气表现力是 temperature（采样温度，默认 1.0）。字段实测，不同版本只改这里。
/// </summary>
public sealed class GptSoVitsClient : ITtsClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public GptSoVitsClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<Stream> SynthesizeAsync(string text, TtsOptions opts, CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["text"] = text,
            ["text_lang"] = opts.TextLang,
            ["ref_audio_path"] = opts.RefAudioPath ?? "",
            ["prompt_text"] = opts.PromptText ?? "",
            ["prompt_lang"] = opts.PromptLang,
            ["speed_factor"] = opts.Speed.ToString(CultureInfo.InvariantCulture),
            // 采样温度 = 语气表现力（高更丰富多变、低更平稳）；基础音色仍由参考音频定。默认 1.0 同 API。
            ["temperature"] = opts.Temperature.ToString(CultureInfo.InvariantCulture),
            ["media_type"] = opts.MediaType,
            ["streaming_mode"] = "false",
        };
        using var form = new FormUrlEncodedContent(query);
        var qs = await form.ReadAsStringAsync(ct);
        using var resp = await _http.GetAsync($"{_baseUrl}/tts?{qs}", ct);
        if (!resp.IsSuccessStatusCode)
        {
            // 把服务的拒绝原因(ref required / 文件 not exists / 字段错误等)写进异常，
            // 经 TtsSpeaker 的 catch → FileLogger 落盘，便于定位（否则日志只有“请求失败”）。
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"GPT-SoVITS /tts {(int)resp.StatusCode} {resp.ReasonPhrase}: {err}");
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return new MemoryStream(bytes, writable: false);
    }
}
