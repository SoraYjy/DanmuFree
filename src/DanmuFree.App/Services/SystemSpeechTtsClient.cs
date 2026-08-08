using System.IO;
using System.Speech.Synthesis;
using DanmuFree.Core.Tts;

namespace DanmuFree.App.Services;

/// <summary>
/// 内置语音引擎（Windows SAPI / System.Speech）：零配置、无需参考音频。
/// 实现 Core 的 <see cref="ITtsClient"/>，把文本合成到内存 WAV 流，复用 TtsSpeaker 的 NAudio 串行播放。
/// 语速 <c>TtsOptions.Speed</c> 映射到 SAPI Rate（-10..10）：1.0=0，0.5=-5，2.0=+10。
/// 音量由 TtsSpeaker 的 WaveOutEvent.Volume 统一控制，此处不重复设。
/// </summary>
public sealed class SystemSpeechTtsClient : ITtsClient
{
    private readonly string? _voice;

    public SystemSpeechTtsClient(string? voice) => _voice = voice;

    public Task<Stream> SynthesizeAsync(string text, TtsOptions opts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ms = new MemoryStream();
        using (var synth = new SpeechSynthesizer())
        {
            if (!string.IsNullOrWhiteSpace(_voice))
            {
                try { synth.SelectVoice(_voice); }
                catch { /* 指定音色名不存在（卸载/换机）→ 回落系统默认音色 */ }
            }
            synth.Rate = MapRate(opts.Speed);
            synth.SetOutputToWaveStream(ms);
            synth.Speak(text);
            synth.SetOutputToNull();   // 释放 WAV writer，确保 RIFF/data 长度写回头部
        }
        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    /// <summary>枚举本机已安装的 SAPI 语音名（供 UI 下拉选择）。失败返回空列表。</summary>
    public static IReadOnlyList<string> ListVoiceNames()
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            return synth.GetInstalledVoices()
                        .Select(v => v.VoiceInfo.Name)
                        .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static int MapRate(double speed) =>
        (int)Math.Round(Math.Clamp((speed - 1.0) * 10.0, -10, 10));
}
