namespace DanmuFree.Core.Tts;

/// <summary>TTS 合成参数。RefAudioPath 为空 = 用服务端默认音色。
/// Temperature 仅 GPT-SoVITS 用（采样温度=语气表现力，高更丰富多变、低更平稳；默认 1.0 同 API）。</summary>
public sealed record TtsOptions(
    string RefAudioPath = "",
    string PromptText = "",
    string TextLang = "zh",
    string PromptLang = "zh",
    double Speed = 1.0,
    string MediaType = "wav",
    double Temperature = 1.0);
