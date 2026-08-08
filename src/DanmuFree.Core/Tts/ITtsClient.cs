namespace DanmuFree.Core.Tts;

public interface ITtsClient
{
    /// <summary>合成语音，返回可读、可 seek 的音频流（wav）。</summary>
    Task<Stream> SynthesizeAsync(string text, TtsOptions opts, CancellationToken ct);
}
