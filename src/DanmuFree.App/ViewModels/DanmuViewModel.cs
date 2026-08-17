using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanmuFree.App.Services;
using DanmuFree.Core;
using DanmuFree.Core.Client;
using DanmuFree.Core.Models;
using DanmuFree.Core.Protocol;
using DanmuFree.Core.Tts;

namespace DanmuFree.App.ViewModels;

public partial class DanmuViewModel : ViewModelBase
{
    private readonly SettingsService _settings = new();
    private readonly FileLogger _log = new();
    private BilibiliDanmuClient? _client;
    private DouyinDanmuClient? _douyinClient;
    private DouyinSigner? _douyinSigner;   // node 签名 sidecar（懒构造，抖音连接时才用）
    private UiBatchPump? _pump;
    private UiBatchPump? _notifyPump;
    private StatsService? _stats;
    private CancellationTokenSource? _cts;
    private ITtsClient? _ttsClient;
    private TtsSpeaker? _ttsSpeaker;
    private GiftTtsPump? _giftPump;            // 礼物朗读聚合泵（连送 debounce 合并）
    private const int GiftAggregateMs = 1200;  // 礼物聚合窗口：停顿超过此值才念累计

    public ObservableCollection<RichMessage> Messages { get; } = new();
    // 进场 / 关注：单独一个集合 + 独立通知窗（与弹幕窗分离）。
    public ObservableCollection<RichMessage> NotifyMessages { get; } = new();

    // 定向回复规则（可编辑行，控制窗「定向回复」TAB 绑定）：弹幕正文命中关键词 → 不读原文，
    // 改念指定文字/播指定音频（Core.ReplyRuleMatcher 首条命中即停）。
    public ObservableCollection<ReplyRuleViewModel> ReplyRules { get; } = new();
    // 收包线程（WS 回调里匹配）与 UI 线程（增删/上移/下移）共用一把锁，防枚举中途集合被改。
    private readonly object _replyLock = new();

    // 系统字体名（供设置面板下拉）
    public string[] SystemFonts { get; } =
        System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToArray();

    [ObservableProperty] private string _roomIdInput = "";
    [ObservableProperty] private string _douyinRoomId = "";

    // 平台选择：决定连哪个 client、显示哪个房间输入。B站用 RoomIdInput，抖音用 DouyinRoomId。
    [ObservableProperty] private Platform _platform = Platform.Bilibili;
    public bool IsDouyin => Platform == Platform.Douyin;
    public bool IsBilibili => Platform == Platform.Bilibili;
    partial void OnPlatformChanged(Platform value)
    {
        OnPropertyChanged(nameof(IsDouyin));
        OnPropertyChanged(nameof(IsBilibili));
    }

    [ObservableProperty] private string? _cookie;
    [ObservableProperty] private string _status = "未连接";
    [ObservableProperty] private string _onlineCount = "-";
    [ObservableProperty] private string _watchedCount = "-";
    [ObservableProperty] private string _likesCount = "-";

    // 显示设置（实时绑定到窗口）
    [ObservableProperty] private bool _topmost;
    [ObservableProperty] private double _opacity = 1.0;
    [ObservableProperty] private string _fontFamily = "Consolas";
    [ObservableProperty] private double _fontSize = 13;
    [ObservableProperty] private int _maxMessages = 1000;

    // 模式开关
    [ObservableProperty] private bool _isFloating;

    // 账号信息（从 Cookie 的 DedeUserID 解析）
    [ObservableProperty] private string _userInfo = "未登录";
    [ObservableProperty] private bool _showUserInfo = true;

    // 粉丝勋章开关
    [ObservableProperty] private bool _showMedal = true;

    // 弹幕时间戳开关
    [ObservableProperty] private bool _showTime = true;

    // 用户名 / 弹幕 分开的字体与颜色（可在控制窗配置）
    [ObservableProperty] private string _userNameFontFamily = "Microsoft YaHei UI";
    [ObservableProperty] private string _userNameColor = "#FF69B7FF";
    [ObservableProperty] private string _danmuFontFamily = "Microsoft YaHei UI";
    [ObservableProperty] private string _danmuColor = "#FFFFFFFF";

    // 文字描边（弹幕窗）：复杂背景（游戏画面）上更易读；关 = Thickness 0（视觉无变化）
    [ObservableProperty] private bool _danmuOutline;
    [ObservableProperty] private string _danmuOutlineColor = "#FF000000";
    [ObservableProperty] private double _danmuOutlineThickness = 1.5;

    // 进场 / 关注 通知窗（独立窗口 / 独立大小 / 独立沉浸）
    [ObservableProperty] private bool _showEntry = true;          // 接收「进入直播间」
    [ObservableProperty] private bool _showFollow = true;         // 接收「关注了主播」
    [ObservableProperty] private bool _showGift = true;           // 接收「礼物」（礼物归本窗，事件流）
    [ObservableProperty] private bool _showSuperChat = true;      // 接收「SC 醒目留言」（B站；归本窗）
    [ObservableProperty] private bool _showNotifyWindow = true;   // 整个进场/关注窗显示/隐藏
    [ObservableProperty] private bool _isNotifyFloating;          // 通知窗沉浸（per-session，不持久化，与主悬浮一致）
    [ObservableProperty] private double _notifyOpacity = 1.0;
    [ObservableProperty] private string _notifyFontFamily = "Microsoft YaHei UI";
    [ObservableProperty] private double _notifyFontSize = 13;

    // 文字描边（通知窗）：独立于弹幕窗描边设置
    [ObservableProperty] private bool _notifyOutline;
    [ObservableProperty] private string _notifyOutlineColor = "#FF000000";
    [ObservableProperty] private double _notifyOutlineThickness = 1.5;

    // 弹幕朗读（独立于显示开关；GPT-SoVITS 本地服务）
    [ObservableProperty] private bool _ttsEnabled;
    [ObservableProperty] private string _ttsEngine = "Edge";             // "Edge"(在线 Azure 神经音，免部署·默认) / "GptSoVits"(音色克隆) / "System"(内置 SAPI)
    [ObservableProperty] private string? _ttsSystemVoice = "";           // 系统引擎音色名（空=系统默认）
    [ObservableProperty] private string _ttsEdgeVoice = "zh-CN-XiaoxiaoNeural"; // Edge 引擎音色
    [ObservableProperty] private string _ttsServerUrl = "http://127.0.0.1:9880";
    [ObservableProperty] private bool _ttsReadDanmu = true;
    [ObservableProperty] private bool _ttsReadSuperChat = true;
    [ObservableProperty] private bool _ttsReadGift = true;
    [ObservableProperty] private bool _ttsReadUserName = true;           // 读「xx 说，」前缀；关掉只读正文
    [ObservableProperty] private string? _ttsRefAudioPath = "";
    [ObservableProperty] private string? _ttsPromptText = "";
    [ObservableProperty] private double _ttsSpeed = 1.0;
    [ObservableProperty] private double _ttsTemperature = 1.0;       // GPT-SoVITS 语气表现力（采样温度）
    [ObservableProperty] private double _ttsVolume = 1.0;
    [ObservableProperty] private int _ttsMaxLength = 80;
    [ObservableProperty] private int _ttsQueueCapacity = 5;
    [ObservableProperty] private string? _ttsBlockedWords = "";

    // 系统内置引擎可选音色（SAPI 枚举，启动时填充；空=系统无可用音色，回落默认）。
    public ObservableCollection<string> SystemVoices { get; } = new();
    // Edge 在线引擎可选音色（Azure 神经音，静态清单见 Core.EdgeTtsClient.SupportedVoices）。
    public ObservableCollection<EdgeVoice> EdgeVoices { get; } = new(EdgeTtsClient.SupportedVoices);

    // 两窗几何（位置 + 大小）：窗口启动时读一次、关闭时写回。非绑定属性，无需通知。
    public double? MainLeft { get; set; }
    public double? MainTop { get; set; }
    public double? MainWidth { get; set; }
    public double? MainHeight { get; set; }
    public double? NotifyLeft { get; set; }
    public double? NotifyTop { get; set; }
    public double? NotifyWidth { get; set; }
    public double? NotifyHeight { get; set; }
    public double? ControlLeft { get; set; }
    public double? ControlTop { get; set; }
    public double? ControlWidth { get; set; }
    public double? ControlHeight { get; set; }

    public DanmuViewModel()
    {
        var s = _settings.Load();
        Cookie = s.Cookie;
        if (s.RecentRooms.Count > 0) RoomIdInput = s.RecentRooms[0];
        DouyinRoomId = s.DouyinRoomId ?? "";
        Platform = Enum.TryParse<Platform>(s.Platform, out var p) ? p : Platform.Bilibili;
        Topmost = s.Topmost;
        IsFloating = s.IsFloating;
        Opacity = s.Opacity;
        FontFamily = string.IsNullOrEmpty(s.FontFamily) ? "Consolas" : s.FontFamily;
        FontSize = s.FontSize;
        MaxMessages = s.MaxMessages;
        ShowUserInfo = s.ShowUserInfo;
        ShowMedal = s.ShowMedal;
        ShowTime = s.ShowTime;
        // 字体默认继承全局 FontFamily，颜色给区分色（用户名浅蓝、弹幕白）
        UserNameFontFamily = string.IsNullOrEmpty(s.UserNameFontFamily) ? FontFamily : s.UserNameFontFamily;
        UserNameColor = string.IsNullOrEmpty(s.UserNameColor) ? "#FF69B7FF" : s.UserNameColor;
        DanmuFontFamily = string.IsNullOrEmpty(s.DanmuFontFamily) ? FontFamily : s.DanmuFontFamily;
        DanmuColor = string.IsNullOrEmpty(s.DanmuColor) ? "#FFFFFFFF" : s.DanmuColor;
        DanmuOutline = s.DanmuOutline;
        DanmuOutlineColor = string.IsNullOrEmpty(s.DanmuOutlineColor) ? "#FF000000" : s.DanmuOutlineColor;
        DanmuOutlineThickness = s.DanmuOutlineThickness;
        // 进场 / 关注 窗
        ShowEntry = s.ShowEntry;
        ShowFollow = s.ShowFollow;
        ShowGift = s.ShowGift;
        ShowSuperChat = s.ShowSuperChat;
        ShowNotifyWindow = s.ShowNotifyWindow;
        IsNotifyFloating = s.IsNotifyFloating;
        NotifyOpacity = s.NotifyOpacity;
        NotifyFontFamily = string.IsNullOrEmpty(s.NotifyFontFamily) ? "Microsoft YaHei UI" : s.NotifyFontFamily;
        NotifyFontSize = s.NotifyFontSize;
        NotifyOutline = s.NotifyOutline;
        NotifyOutlineColor = string.IsNullOrEmpty(s.NotifyOutlineColor) ? "#FF000000" : s.NotifyOutlineColor;
        NotifyOutlineThickness = s.NotifyOutlineThickness;
        // 两窗几何
        MainLeft = s.MainLeft; MainTop = s.MainTop; MainWidth = s.MainWidth; MainHeight = s.MainHeight;
        NotifyLeft = s.NotifyLeft; NotifyTop = s.NotifyTop; NotifyWidth = s.NotifyWidth; NotifyHeight = s.NotifyHeight;
        ControlLeft = s.ControlLeft; ControlTop = s.ControlTop; ControlWidth = s.ControlWidth; ControlHeight = s.ControlHeight;
        // 朗读
        TtsEngine = string.IsNullOrEmpty(s.TtsEngine) ? "Edge" : s.TtsEngine;
        TtsSystemVoice = s.TtsSystemVoice ?? "";
        TtsEdgeVoice = string.IsNullOrEmpty(s.TtsEdgeVoice) ? "zh-CN-XiaoxiaoNeural" : s.TtsEdgeVoice;
        TtsServerUrl = string.IsNullOrEmpty(s.TtsServerUrl) ? "http://127.0.0.1:9880" : s.TtsServerUrl;
        TtsReadDanmu = s.TtsReadDanmu;
        TtsReadSuperChat = s.TtsReadSuperChat;
        TtsReadGift = s.TtsReadGift;
        TtsReadUserName = s.TtsReadUserName;
        TtsRefAudioPath = s.TtsRefAudioPath ?? "";
        TtsPromptText = s.TtsPromptText ?? "";
        TtsSpeed = s.TtsSpeed;
        TtsTemperature = s.TtsTemperature;
        TtsVolume = s.TtsVolume;
        TtsMaxLength = s.TtsMaxLength;
        TtsQueueCapacity = s.TtsQueueCapacity;
        TtsBlockedWords = s.TtsBlockedWords ?? "";
        // 定向回复规则（顺序即匹配优先级，列表顺序原样还原）
        foreach (var r in s.ReplyRules)
            ReplyRules.Add(new ReplyRuleViewModel
            {
                Keyword = r.Keyword,
                Action = r.Action == "sound" ? "sound" : "text",
                Text = r.Text,
                SoundPath = r.SoundPath,
                Enabled = r.Enabled,
            });
        // 枚举系统内置引擎可选音色（一次性；与设置无关）。
        if (SystemVoices.Count == 0)
            foreach (var name in SystemSpeechTtsClient.ListVoiceNames())
                SystemVoices.Add(name);
        TtsEnabled = s.TtsEnabled;
        RefreshUserInfo();
    }

    partial void OnCookieChanged(string? value) => RefreshUserInfo();

    // 悬浮（沉浸置顶）派生属性：进悬浮→强制置顶 + 背景更透；退出自动还原用户原始设置。
    // XAML 的 Window.Topmost 与背景透明度绑这两个，不直接绑 Topmost/Opacity，以保证退出悬浮能复原。
    public bool EffectiveTopmost => Topmost || IsFloating;
    public double EffectiveOpacity => IsFloating ? Math.Min(Opacity, 0.12) : Opacity;

    partial void OnIsFloatingChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveTopmost));
        OnPropertyChanged(nameof(EffectiveOpacity));
    }
    partial void OnTopmostChanged(bool value) => OnPropertyChanged(nameof(EffectiveTopmost));
    partial void OnOpacityChanged(double value) => OnPropertyChanged(nameof(EffectiveOpacity));

    // 进场 / 关注 通知窗：同款「进沉浸自动置顶 + 更透」派生，独立于主弹幕窗。
    public bool EffectiveNotifyTopmost => IsNotifyFloating;
    public double EffectiveNotifyOpacity => IsNotifyFloating ? Math.Min(NotifyOpacity, 0.12) : NotifyOpacity;

    partial void OnIsNotifyFloatingChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveNotifyTopmost));
        OnPropertyChanged(nameof(EffectiveNotifyOpacity));
    }
    partial void OnNotifyOpacityChanged(double value) => OnPropertyChanged(nameof(EffectiveNotifyOpacity));

    // 描边生效粗细：开关关 → 0（OutlineHost.Thickness=0 时影子与文字完全重合，无视觉变化）
    public double DanmuOutlineDepth => DanmuOutline ? DanmuOutlineThickness : 0;
    public double NotifyOutlineDepth => NotifyOutline ? NotifyOutlineThickness : 0;
    partial void OnDanmuOutlineChanged(bool value) => OnPropertyChanged(nameof(DanmuOutlineDepth));
    partial void OnDanmuOutlineThicknessChanged(double value) => OnPropertyChanged(nameof(DanmuOutlineDepth));
    partial void OnNotifyOutlineChanged(bool value) => OnPropertyChanged(nameof(NotifyOutlineDepth));
    partial void OnNotifyOutlineThicknessChanged(double value) => OnPropertyChanged(nameof(NotifyOutlineDepth));

    private void RefreshUserInfo()
    {
        var uid = ParseUid(Cookie);
        UserInfo = uid is null ? "未登录" : $"已登录 UID: {uid}";
    }

    private static long? ParseUid(string? cookie)
    {
        if (string.IsNullOrEmpty(cookie)) return null;
        foreach (var part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Trim() == "DedeUserID" && long.TryParse(part[(eq + 1)..].Trim(), out var uid))
                return uid;
        }
        return null;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        // 按平台取房间号 + client。抖音匿名（无 Cookie），在线人数走 WS 推送（WebcastRoomUserSeqMessage）。
        bool douyin = Platform == Platform.Douyin;
        string room = douyin ? DouyinRoomId.Trim() : RoomIdInput.Trim();
        if (string.IsNullOrWhiteSpace(room)) return;

        _cts = new CancellationTokenSource();
        _pump = new UiBatchPump(Messages, MaxMessages);
        _notifyPump = new UiBatchPump(NotifyMessages, MaxMessages);
        _ = _pump.RunAsync(_cts.Token);
        _ = _notifyPump.RunAsync(_cts.Token);
        Status = "连接中…";
        try
        {
            if (douyin)
            {
                _douyinSigner ??= new DouyinSigner();
                _douyinClient = new DouyinDanmuClient(new HttpClient(), _douyinSigner, m => _log.Error(m));
                _douyinClient.MessageReceived += OnMessageReceived;
                _douyinClient.ConnectionStateChanged += OnStateChanged;
                _douyinClient.StatsUpdated += OnDouyinStats;
                await _douyinClient.ConnectAsync(room, _cts.Token);
            }
            else
            {
                _client = new BilibiliDanmuClient();
                _client.MessageReceived += OnMessageReceived;
                _client.ConnectionStateChanged += OnStateChanged;
                _stats = new StatsService(new HttpClient(), Cookie);
                _stats.Updated += OnStatsUpdated;
                _ = _stats.StartAsync(room, _cts.Token);
                await _client.ConnectAsync(room, Cookie, _cts.Token, m => _log.Error(m));
            }
        }
        catch (Exception e)
        {
            // 连接失败（房间无效 / 缺 node / 网络 / 签名失败）：提示而非崩。client 内部重连循环已由 ct 取消。
            _cts?.Cancel();
            _log.Error($"连接失败：{e.Message}", e);
            Status = $"连接失败：{e.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_douyinClient is not null) { await _douyinClient.DisconnectAsync(); _douyinClient = null; }
        if (_client is not null) { await _client.DisconnectAsync(); _client = null; }
        _cts?.Cancel();
        Status = "已断开";
    }


    private void OnMessageReceived(RichMessage m)
    {
        if (m.Type == MessageType.OnlineCount)
            return; // B站 op3 不可靠（在线由 StatsService 提供）；抖音在线走 StatsUpdated，不进消息流
        // 朗读（独立第三管道，在任何显示路由之前；DropOldest 永不阻塞收弹幕主路径）
        EnqueueForTts(m);
        // 礼物 / SC：与进场/关注同属「事件流」，路由到通知窗（礼物名/SC价格在 Extra，留言在 Text，
        //          由通知窗模板渲染）；各自开关；不再进主弹幕流（主弹幕流只剩纯聊天 Danmu）。
        if (m.Type == MessageType.Gift)
        {
            if (ShowGift) _notifyPump?.Writer.TryWrite(m);
            return;
        }
        if (m.Type == MessageType.SuperChat)
        {
            if (ShowSuperChat) _notifyPump?.Writer.TryWrite(m);
            return;
        }
        // 进场 / 关注：单独路由到通知窗集合；进场/关注可分别开关（按动作 Text 判定，Text 由解析器固定产出）。
        if (m.Type == MessageType.Interact)
        {
            bool allow = m.Text switch
            {
                "关注了主播"   => ShowFollow,
                "分享了直播间" => ShowEntry || ShowFollow, // 罕见，任一开则放行
                _             => ShowEntry,                // "进入直播间" 及其它
            };
            if (!allow) return;
            _notifyPump?.Writer.TryWrite(m);
            return;
        }
        if (!ShowMedal && m.Medal is not null) m = m with { Medal = null };
        _pump?.Writer.TryWrite(m);
    }

    private void OnStatsUpdated(RoomStats s)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnlineCount = NumberFormatter.Format(s.Online);
            WatchedCount = NumberFormatter.Format(s.Watched);
            LikesCount = NumberFormatter.Format(s.Likes);
        });
    }

    // 抖音统计（WS 推送）：在线（raw，本地格式化）/ 累计看过（服务端已格式化串如「1.3万」，直接显示）；
    // 点赞总数抖音不推，保持「-」。
    private void OnDouyinStats(DouyinRoomStats s)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (s.Online is long o) OnlineCount = NumberFormatter.Format(o);
            if (!string.IsNullOrEmpty(s.Watched)) WatchedCount = s.Watched;
        });
    }

    private void OnStateChanged(ConnectionState s)
    {
        Status = s switch
        {
            ConnectionState.Connecting => "连接中…",
            ConnectionState.Connected => "已连接",
            ConnectionState.Reconnecting => "重连中…",
            _ => "已断开",
        };
    }

    public void SaveSettings()
    {
        _settings.Save(new AppSettings
        {
            Cookie = Cookie,
            RecentRooms = string.IsNullOrWhiteSpace(RoomIdInput)
                ? new()
                : new List<string> { RoomIdInput.Trim() },
            Platform = Platform.ToString(),
            DouyinRoomId = DouyinRoomId,
            MaxMessages = MaxMessages,
            Topmost = Topmost,
            IsFloating = IsFloating,
            Opacity = Opacity,
            FontFamily = FontFamily,
            FontSize = FontSize,
            ShowUserInfo = ShowUserInfo,
            ShowMedal = ShowMedal,
            ShowTime = ShowTime,
            UserNameFontFamily = UserNameFontFamily,
            UserNameColor = UserNameColor,
            DanmuFontFamily = DanmuFontFamily,
            DanmuColor = DanmuColor,
            DanmuOutline = DanmuOutline,
            DanmuOutlineColor = DanmuOutlineColor,
            DanmuOutlineThickness = DanmuOutlineThickness,
            ShowEntry = ShowEntry,
            ShowFollow = ShowFollow,
            ShowGift = ShowGift,
            ShowSuperChat = ShowSuperChat,
            ShowNotifyWindow = ShowNotifyWindow,
            IsNotifyFloating = IsNotifyFloating,
            NotifyOpacity = NotifyOpacity,
            NotifyFontFamily = NotifyFontFamily,
            NotifyFontSize = NotifyFontSize,
            NotifyOutline = NotifyOutline,
            NotifyOutlineColor = NotifyOutlineColor,
            NotifyOutlineThickness = NotifyOutlineThickness,
            MainLeft = MainLeft, MainTop = MainTop, MainWidth = MainWidth, MainHeight = MainHeight,
            NotifyLeft = NotifyLeft, NotifyTop = NotifyTop, NotifyWidth = NotifyWidth, NotifyHeight = NotifyHeight,
            ControlLeft = ControlLeft, ControlTop = ControlTop, ControlWidth = ControlWidth, ControlHeight = ControlHeight,
            TtsEnabled = TtsEnabled,
            TtsEngine = TtsEngine,
            TtsSystemVoice = TtsSystemVoice,
            TtsEdgeVoice = TtsEdgeVoice,
            TtsServerUrl = TtsServerUrl,
            TtsReadDanmu = TtsReadDanmu,
            TtsReadSuperChat = TtsReadSuperChat,
            TtsReadGift = TtsReadGift,
            TtsReadUserName = TtsReadUserName,
            TtsRefAudioPath = TtsRefAudioPath,
            TtsPromptText = TtsPromptText,
            TtsSpeed = TtsSpeed,
            TtsTemperature = TtsTemperature,
            TtsVolume = TtsVolume,
            TtsMaxLength = TtsMaxLength,
            TtsQueueCapacity = TtsQueueCapacity,
            TtsBlockedWords = TtsBlockedWords,
            ReplyRules = ReplyRules.Select(r => new ReplyRuleConfig
            {
                Keyword = r.Keyword, Action = r.Action, Text = r.Text, SoundPath = r.SoundPath, Enabled = r.Enabled,
            }).ToList(),
        });
    }

    partial void OnTtsEnabledChanged(bool value)
    {
        if (value) EnsureTtsSpeaker();
        else StopTts();
    }

    partial void OnTtsSpeedChanged(double value) => _ttsSpeaker?.Update(BuildTtsOptions(), TtsVolume);
    partial void OnTtsTemperatureChanged(double value) => _ttsSpeaker?.Update(BuildTtsOptions(), TtsVolume);
    partial void OnTtsVolumeChanged(double value) => _ttsSpeaker?.Update(BuildTtsOptions(), TtsVolume);
    partial void OnTtsRefAudioPathChanged(string? value) => _ttsSpeaker?.Update(BuildTtsOptions(), TtsVolume);
    partial void OnTtsPromptTextChanged(string? value) => _ttsSpeaker?.Update(BuildTtsOptions(), TtsVolume);
    partial void OnTtsBlockedWordsChanged(string? value) => _giftPump?.UpdateBlocked(ParseBlocked(TtsBlockedWords));

    // 引擎 / 音色 / 服务地址 变更：若朗读中则重建底层 client（换合成器），否则下次启用生效。
    partial void OnTtsEngineChanged(string value) => RestartTtsIfRunning();
    partial void OnTtsSystemVoiceChanged(string? value) => RestartTtsIfRunning();
    partial void OnTtsEdgeVoiceChanged(string value) => RestartTtsIfRunning();
    partial void OnTtsServerUrlChanged(string value) => RestartTtsIfRunning();

    private TtsOptions BuildTtsOptions() => new(
        RefAudioPath: TtsRefAudioPath ?? "",
        PromptText: TtsPromptText ?? "",
        TextLang: "zh",
        PromptLang: "zh",
        Speed: TtsSpeed,
        MediaType: "wav",
        Temperature: TtsTemperature);

    private ITtsClient CreateTtsClient() => TtsEngine switch
    {
        "System" => new SystemSpeechTtsClient(TtsSystemVoice),
        "Edge" => new EdgeTtsClient(TtsEdgeVoice),
        _ => new GptSoVitsClient(new HttpClient(), TtsServerUrl),
    };

    private void EnsureTtsSpeaker()
    {
        if (_ttsSpeaker is not null) return;
        _ttsClient = CreateTtsClient();
        _ttsSpeaker = new TtsSpeaker(_ttsClient, TtsQueueCapacity, _log);
        _ttsSpeaker.Update(BuildTtsOptions(), TtsVolume);
        _ttsSpeaker.Start();
        _giftPump = new GiftTtsPump(_ttsSpeaker.Writer, ParseBlocked(TtsBlockedWords), GiftAggregateMs);
    }

    private void RestartTtsIfRunning()
    {
        if (_ttsSpeaker is null) return;
        StopTts();
        EnsureTtsSpeaker();
    }

    private void StopTts()
    {
        _giftPump?.Dispose();   // 先念完礼物累计，再关 speaker
        _giftPump = null;
        _ttsSpeaker?.Dispose();
        _ttsSpeaker = null;
        _ttsClient = null;
    }

    private static IReadOnlyList<string> ParseBlocked(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void EnqueueForTts(RichMessage m)
    {
        if (!TtsEnabled || _ttsSpeaker is null) return;
        // 礼物走聚合泵（连送合并 + 始终带用户名，不受读用户名开关影响）；Danmu/SC 走 TtsTextBuilder。
        if (m.Type == MessageType.Gift)
        {
            if (TtsReadGift) _giftPump?.Add(m.UserName, m.Extra);
            return;
        }
        // 定向回复：Danmu 正文命中关键词 → 不读原弹幕，改念指定文字 / 播指定音频（首条命中即停）。
        // 独立于「读弹幕」开关（可关掉普通弹幕朗读、只留定向回复）；回复原样播报，
        // 不走屏蔽词/字数上限/读用户名（用户显式配置的内容，不该被再过滤）。
        if (m.Type == MessageType.Danmu && ReplyRules.Count > 0)
        {
            ReplyRule? hit;
            lock (_replyLock)
                hit = ReplyRuleMatcher.MatchFirst(ReplyRules.Select(r => r.ToRule()), m.Text);
            if (hit is not null)
            {
                _ttsSpeaker.Writer.TryWrite(hit.Action == ReplyAction.PlaySound
                    ? TtsItem.Sound(hit.SoundPath)
                    : TtsItem.Speech(hit.Text));
                return;
            }
        }
        var toRead = TtsTextBuilder.Build(m,
            new TtsReadFlags(TtsReadDanmu, TtsReadSuperChat, TtsReadGift, TtsReadUserName),
            ParseBlocked(TtsBlockedWords), TtsMaxLength);
        if (toRead is not null)
            _ttsSpeaker.Writer.TryWrite(TtsItem.Speech(toRead));
    }

    // —— 定向回复规则管理（UI 线程增删/移动；与收包线程的匹配共用 _replyLock）——

    [RelayCommand]
    private void AddReplyRule()
    {
        lock (_replyLock) ReplyRules.Add(new ReplyRuleViewModel());
    }

    [RelayCommand]
    private void RemoveReplyRule(ReplyRuleViewModel rule)
    {
        lock (_replyLock) ReplyRules.Remove(rule);
    }

    [RelayCommand]
    private void MoveReplyRuleUp(ReplyRuleViewModel rule)
    {
        lock (_replyLock)
        {
            int i = ReplyRules.IndexOf(rule);
            if (i > 0) ReplyRules.Move(i, i - 1);
        }
    }

    [RelayCommand]
    private void MoveReplyRuleDown(ReplyRuleViewModel rule)
    {
        lock (_replyLock)
        {
            int i = ReplyRules.IndexOf(rule);
            if (i >= 0 && i < ReplyRules.Count - 1) ReplyRules.Move(i, i + 1);
        }
    }

    [RelayCommand]
    private void TestReplyRule(ReplyRuleViewModel rule)
    {
        // 试听：不经弹幕，把该规则的回复直接塞进朗读队列（念文字走当前引擎；播音频直接放文件）
        if (rule.Action == "sound")
        {
            if (string.IsNullOrWhiteSpace(rule.SoundPath)) return;
            EnsureTtsSpeaker();
            _ttsSpeaker!.Writer.TryWrite(TtsItem.Sound(rule.SoundPath));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(rule.Text)) return;
            EnsureTtsSpeaker();
            _ttsSpeaker!.Writer.TryWrite(TtsItem.Speech(rule.Text));
        }
    }

    [RelayCommand]
    private void TestTts()
    {
        EnsureTtsSpeaker();
        _ttsSpeaker!.Writer.TryWrite(TtsItem.Speech("弹幕朗读测试。"));
    }
}
