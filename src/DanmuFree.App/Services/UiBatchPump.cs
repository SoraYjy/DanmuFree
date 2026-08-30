using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using DanmuFree.Core.Models;

namespace DanmuFree.App.Services;

/// <summary>
/// Drains a bounded <see cref="Channel{RichMessage}"/> and appends batches to an
/// <see cref="ObservableCollection{RichMessage}"/> on the WPF UI thread. The producer
/// (ViewModel / Core client) writes into <see cref="Writer"/>; this pump aggregates up
/// to 256 messages or ~100ms worth, whichever comes first, then marshals the batch via
/// <see cref="Dispatcher.InvokeAsync"/> to keep UI-thread churn bounded under high load.
/// When the collection exceeds <c>maxMessages</c> the oldest entries are trimmed.
/// </summary>
public sealed class UiBatchPump : IDisposable
{
    private readonly ObservableCollection<RichMessage> _target;
    private readonly int _max;
    private long _written;   // 入队累计（收包线程递增）
    private long _read;      // 出队累计（泵循环递增）
    private readonly Channel<RichMessage> _channel =
        Channel.CreateBounded<RichMessage>(new BoundedChannelOptions(2000)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public UiBatchPump(ObservableCollection<RichMessage> target, int maxMessages)
    {
        _target = target;
        _max = maxMessages;
    }

    /// <summary>入队一条（.NET 8 的 BoundedChannel 无 ItemDropped 回调，靠计数差算丢弃）。</summary>
    public bool TryWrite(RichMessage m)
    {
        Interlocked.Increment(ref _written);
        return _channel.Writer.TryWrite(m);
    }

    /// <summary>被 DropOldest 丢弃的累计条数 = 入队 − 已读 − 仍在管道。
    /// 诊断用：&gt;0 = UI 侧确实丢过；恒 0 = 没显示是服务端就没推，与本端无关。</summary>
    public long DroppedCount
    {
        get
        {
            long inflight = _channel.Reader.CanCount ? _channel.Reader.Count : 0;
            long d = Interlocked.Read(ref _written) - Interlocked.Read(ref _read) - inflight;
            return d > 0 ? d : 0;
        }
    }

    /// <summary>Raised on the UI thread for each appended message; used by the auto-scroll hook.</summary>
    public event Action<RichMessage>? BatchAppended;

    /// <summary>
    /// Pump loop. Reads up to 256 messages or for ~100ms (whichever comes first) per batch,
    /// then dispatches the batch to the UI thread. Returns when <paramref name="ct"/> cancels.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var batch = new List<RichMessage>(256);
        while (!ct.IsCancellationRequested)
        {
            batch.Clear();
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(ct)) break;
                var deadline = DateTimeOffset.UtcNow.AddMilliseconds(100);
                while (batch.Count < 256 && _channel.Reader.TryRead(out var m))
                {
                    Interlocked.Increment(ref _read);
                    batch.Add(m);
                    if (DateTimeOffset.UtcNow >= deadline) break;
                }
            }
            catch (OperationCanceledException) { break; }

            if (batch.Count == 0) continue;
            var snapshot = batch.ToArray();
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) break;
            await dispatcher.InvokeAsync(() => Apply(snapshot), DispatcherPriority.Background);
        }
    }

    private void Apply(RichMessage[] msgs)
    {
        foreach (var m in msgs)
        {
            _target.Add(m);
            BatchAppended?.Invoke(m);
            while (_target.Count > _max) _target.RemoveAt(0);
        }
    }

    public void Dispose() => _channel.Writer.TryComplete();
}
