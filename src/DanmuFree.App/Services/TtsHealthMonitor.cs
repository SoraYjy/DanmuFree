using DanmuFree.Core.Tts;

namespace DanmuFree.App.Services;

/// <summary>
/// TTS 健康心跳（控制窗「朗读」TAB 状态行的数据源）：朗读开启期间每 60s 用当前引擎合成一个
/// 探针短句；真实弹幕合成的成败经 <see cref="TtsSpeaker.ResultReported"/> 同样计入（立即刷新，
/// 不等下一个 tick）。动机：2026-10-26 Edge 全局 500 事故——用户只看到「没声音」，
/// 要在面板一眼看出「微软服务端故障，不是本机问题」。
/// 探针独立调 client（不进朗读队列、不播声）；speaker 忙时跳过本 tick 避免抢占引擎；
/// client 用独立实例（三个实现均为单次调用自包含：Edge 每次起新 node 进程 / GPT 用自带
/// HttpClient / System 独立 SAPI 实例，双实例并存安全）。
/// </summary>
public sealed class TtsHealthMonitor : IDisposable
{
    private const string ProbeText = "你好"; // 探针文本越短越好（Edge 一次合成 ~1s、数 KB 流量）
    private readonly Func<ITtsClient> _clientFactory;
    private readonly Func<TtsOptions> _opts;
    private readonly Func<bool> _canProbe;
    private readonly FileLogger? _log;
    private readonly System.Threading.Timer _timer;
    private ITtsClient? _client;
    private int _probing;

    public TtsHealthMonitor(Func<ITtsClient> clientFactory, Func<TtsOptions> opts, Func<bool> canProbe, FileLogger? log)
    {
        _clientFactory = clientFactory;
        _opts = opts;
        _canProbe = canProbe;
        _log = log;
        // 首探 2s（开启朗读后尽快给结论），之后 60s 一探
        _timer = new System.Threading.Timer(async _ => await ProbeAsync(), null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));
    }

    private readonly object _gate = new();
    // 三态：Initial（首探结果未出，状态行留空——避免「检测中」被误读成故障，也不会在
    // 朗读开关关着时误导成「正常」）→ Ok（绿）/ Fail（红+持续时长）
    private enum Phase { Initial, Ok, Fail }
    private Phase _phase = Phase.Initial;
    private string _cause = "";          // Fail 态的原因（分类器人话）
    private DateTime _since = DateTime.Now;

    /// <summary>当前状态快照（VM 1s tick 拉取；故障态附持续时长，让用户知道挂了多久）。
    /// Initial 态返回空文本。文案不带 emoji/符号——控制窗字体缺字形会渲染成方框
    /// （实测 ✅⚠️ 变乱码），状态用颜色区分即可。</summary>
    public (string Text, bool Warn) Snapshot()
    {
        lock (_gate)
        {
            switch (_phase)
            {
                case Phase.Initial: return ("", false);
                case Phase.Ok: return ($"朗读引擎正常（{DateTime.Now:HH:mm} 检查）", false);
                default:
                    var dur = DateTime.Now - _since;
                    var durText = dur.TotalMinutes >= 1 ? $"，已持续 {(int)dur.TotalMinutes} 分钟" : "";
                    return ($"{_cause}{durText}", true);
            }
        }
    }

    /// <summary>真实合成结果（speaker 回调；成功清故障态，失败立即亮红）。</summary>
    public void NoteRealResult(bool ok, Exception? ex)
    {
        if (ok) SetOk();
        else SetFail(TtsErrorClassifier.Classify(ex));
    }

    private async Task ProbeAsync()
    {
        if (!_canProbe()) return;
        if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0) return; // 上一探未归（卡 20s 超时内）不叠
        try
        {
            _client ??= _clientFactory();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var s = await _client.SynthesizeAsync(ProbeText, _opts(), cts.Token);
            SetOk();
        }
        catch (OperationCanceledException)
        {
            SetFail("连接超时：网络不通或被拦截");
        }
        catch (Exception ex)
        {
            _log?.Error("TTS 健康探针失败", ex);
            SetFail(TtsErrorClassifier.Classify(ex));
        }
        finally
        {
            Interlocked.Exchange(ref _probing, 0);
        }
    }

    private void SetOk()
    {
        lock (_gate)
        {
            if (_phase != Phase.Ok) { _phase = Phase.Ok; _since = DateTime.Now; }
        }
    }

    private void SetFail(string cause)
    {
        lock (_gate)
        {
            // 防御：空原因会把状态洗成「正常」（Classify(null) 返回 ""）
            if (string.IsNullOrEmpty(cause)) cause = "未知错误";
            if (_phase == Phase.Fail && _cause == cause) return; // 同因连续失败：保留最初 _since（持续时长累计）
            _phase = Phase.Fail;
            _cause = cause;
            _since = DateTime.Now;
        }
    }

    public void Dispose() => _timer.Dispose();
}
