namespace DanmuFree.Core.Tts;

/// <summary>朗读类型开关（独立于显示开关：可「看着礼物，但只听弹幕」）。
/// ReadUserName 控制是否带「xx 说，」前缀（默认 true，保后向兼容）。</summary>
public sealed record TtsReadFlags(bool Danmu, bool SuperChat, bool Gift, bool ReadUserName = true);
