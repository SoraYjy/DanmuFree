using System.Threading;
using System.Threading.Channels;
using DanmuFree.Core.Tts;

namespace DanmuFree.App.Services;

/// <summary>
/// 礼物朗读 debounce 泵：连送同用户+同礼物在一个窗口内累加，停顿后才念「xx 送了 N 个 yy」一次，
/// 不再被连送刷屏读 100 次。换用户/换礼物立即把上一组念出来。
/// 核心聚合逻辑在 Core 的 <see cref="GiftReadAggregator"/>（纯逻辑、可单测）；本类负责
/// 定时器（<see cref="System.Threading.Timer"/>，线程池回调，与收包线程并发）+ 屏蔽词 + 出队。
/// </summary>
public sealed class GiftTtsPump : IDisposable
{
    private readonly ChannelWriter<string> _writer;
    private readonly GiftReadAggregator _agg = new();
    private readonly Timer _timer;
    private readonly int _windowMs;
    private IReadOnlyList<string> _blocked;
    private readonly object _lock = new();
    private bool _disposed;

    public GiftTtsPump(ChannelWriter<string> writer, IReadOnlyList<string> blocked, int windowMs)
    {
        _writer = writer;
        _blocked = blocked;
        _windowMs = windowMs;
        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>收到一条礼物。同组累加并重置窗口；换组立即念上一组。</summary>
    public void Add(string user, string? extra)
    {
        lock (_lock)
        {
            if (_disposed) return;
            var immediate = _agg.Add(user, extra);
            if (immediate is not null) Emit(immediate);
            // debounce：每收到一条就重置窗口，停顿 windowMs 后才念当前累计。
            _timer.Change(_windowMs, Timeout.Infinite);
        }
    }

    public void UpdateBlocked(IReadOnlyList<string> blocked)
    {
        lock (_lock) _blocked = blocked;
    }

    private void OnTick(object? state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            Emit(_agg.Flush());
        }
    }

    private void Emit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var w in _blocked)
            if (!string.IsNullOrEmpty(w) && text.Contains(w)) return;  // 命中屏蔽词：不读
        _writer.TryWrite(text);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            Emit(_agg.Flush());   // 停朗读前念完最后攒的一组，避免吞掉
            _disposed = true;
        }
        _timer.Dispose();
    }
}
