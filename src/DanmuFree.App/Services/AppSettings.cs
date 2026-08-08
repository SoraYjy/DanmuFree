using System.Collections.Generic;

namespace DanmuFree.App.Services;

/// <summary>
/// User-facing application settings persisted to %AppData%/DanmuFree/settings.json.
/// </summary>
public sealed class AppSettings
{
    public string? Cookie { get; set; }
    public List<string> RecentRooms { get; set; } = new();
    public int MaxMessages { get; set; } = 1000;

    // 平台选择（"Bilibili" / "Douyin"，字符串以兼容旧 settings.json）。抖音为匿名，无需 Cookie。
    public string Platform { get; set; } = "Bilibili";
    public string DouyinRoomId { get; set; } = "";

    // 显示设置（v2）
    public bool Topmost { get; set; } = false;
    public double Opacity { get; set; } = 1.0;
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 13;

    // 账号信息开关（显示从 Cookie 解析出的 UID）
    public bool ShowUserInfo { get; set; } = true;

    // 粉丝勋章开关
    public bool ShowMedal { get; set; } = true;

    // 弹幕时间戳开关
    public bool ShowTime { get; set; } = true;

    // 用户名 / 弹幕 分开的字体与颜色（控制窗可配置）
    public string? UserNameFontFamily { get; set; }
    public string? UserNameColor { get; set; }
    public string? DanmuFontFamily { get; set; }
    public string? DanmuColor { get; set; }

    // 悬浮（沉浸置顶 · 鼠标穿透）：两窗各自持久化（关闭时悬浮，下次启动仍是悬浮态）。
    public bool IsFloating { get; set; } = false;         // 主弹幕窗悬浮
    public bool IsNotifyFloating { get; set; } = false;   // 进场/关注窗悬浮

    // 进场 / 关注 通知窗（独立窗口，独立大小 / 字体 / 透明度 / 沉浸）
    public bool ShowEntry { get; set; } = true;           // 显示「进入直播间」
    public bool ShowFollow { get; set; } = true;          // 显示「关注了主播」
    public bool ShowGift { get; set; } = true;            // 显示「礼物」（礼物归本窗，与进场/关注同属事件流）
    public bool ShowSuperChat { get; set; } = true;       // 显示「SC 醒目留言」（B站；留言在 Text、价格在 Extra）
    public bool ShowNotifyWindow { get; set; } = true;    // 显示整个进场/关注窗口
    public double NotifyOpacity { get; set; } = 1.0;      // 通知窗背景透明度
    public string? NotifyFontFamily { get; set; } = "Microsoft YaHei UI";
    public double NotifyFontSize { get; set; } = 13;

    // 两窗几何（位置 + 大小），null=未保存，用默认值。加载时做屏内夹取，避免落到已断开的显示器。
    public double? MainLeft { get; set; }
    public double? MainTop { get; set; }
    public double? MainWidth { get; set; }
    public double? MainHeight { get; set; }
    public double? NotifyLeft { get; set; }
    public double? NotifyTop { get; set; }
    public double? NotifyWidth { get; set; }
    public double? NotifyHeight { get; set; }
    // 控制窗几何（可调大小 + 持久化），null=未保存用默认。
    public double? ControlLeft { get; set; }
    public double? ControlTop { get; set; }
    public double? ControlWidth { get; set; }
    public double? ControlHeight { get; set; }

    // 弹幕朗读（TTS；朗读开关独立于显示开关）。
    public bool TtsEnabled { get; set; } = false;
    // 引擎："GptSoVits"（音色克隆，需参考音频 + 本地服务）/ "System"（Windows SAPI 内置，免参考音频）。
    public string TtsEngine { get; set; } = "GptSoVits";
    public string? TtsSystemVoice { get; set; } = "";   // 系统引擎选用的音色名（空=系统默认）
    public string TtsServerUrl { get; set; } = "http://127.0.0.1:9880";
    public bool TtsReadDanmu { get; set; } = true;
    public bool TtsReadSuperChat { get; set; } = true;
    public bool TtsReadGift { get; set; } = true;
    public bool TtsReadUserName { get; set; } = true;   // 读「xx 说，」前缀；关掉只读正文
    public string? TtsRefAudioPath { get; set; } = "";
    public string? TtsPromptText { get; set; } = "";
    public double TtsSpeed { get; set; } = 1.0;
    public double TtsTemperature { get; set; } = 1.0;   // GPT-SoVITS 采样温度=语气表现力（高丰富、低平稳；仅 GPT-SoVITS 引擎用）
    public double TtsVolume { get; set; } = 1.0;
    public int TtsMaxLength { get; set; } = 80;
    public int TtsQueueCapacity { get; set; } = 5;
    public string? TtsBlockedWords { get; set; } = "";
}
