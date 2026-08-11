namespace DanmuFree.Core.Tts;

public interface ITtsClient
{
    /// <summary>合成语音，返回可读、可 seek 的音频流（**WAV 或 MP3**：GPT-SoVITS/SystemSpeech 返回 WAV，
    /// Edge 返回 MP3）。TtsSpeaker 嗅探头部（RIFF？）后选 WaveFileReader 或 MP3 解码器。</summary>
    Task<Stream> SynthesizeAsync(string text, TtsOptions opts, CancellationToken ct);
}
