using CommunityToolkit.Mvvm.ComponentModel;
using DanmuFree.Core.Tts;

namespace DanmuFree.App.ViewModels;

/// <summary>
/// 定向回复规则的可编辑行模型（控制窗「定向回复」TAB 绑定；Collection 属 DanmuViewModel）。
/// Action 用字符串 "text" / "sound"：ComboBox 双向绑定直观、settings.json 里人眼可读。
/// 匹配时经 <see cref="ToRule"/> 转成 Core 的 <see cref="ReplyRule"/>（首条命中即停的纯逻辑在 Core，可单测）。
/// </summary>
public partial class ReplyRuleViewModel : ObservableObject
{
    /// <summary>动作下拉选项（所有行共用一份，XAML 以 x:Static 引用）。</summary>
    public sealed record ReplyOption(string Id, string Display);

    public static readonly IReadOnlyList<ReplyOption> ActionOptions = new[]
    {
        new ReplyOption("text", "念文字"),
        new ReplyOption("sound", "播音频"),
    };

    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private string _action = "text";   // "text" 念文字 / "sound" 播音频
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _soundPath = "";

    /// <summary>转 Core 匹配用规则（去空白；字符串动作 → 枚举）。</summary>
    public ReplyRule ToRule() => new(
        Keyword.Trim(),
        Action == "sound" ? ReplyAction.PlaySound : ReplyAction.SpeakText,
        Text.Trim(),
        SoundPath.Trim());
}
