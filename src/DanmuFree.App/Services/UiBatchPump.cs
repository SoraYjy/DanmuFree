using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using DanmuFree.Core.Models;

namespace DanmuFree.App.Services;

/// <summary>
/// Drains a bounded <see cref="Channel{RichMessage}"/> and appends batches to an
/// <see cref="ObservableCollection{RichMessage}"/> on the WPF UI thread. The producer
/// (ViewModel / Core client) writes into <see cref="Writer"/>; this pump aggregates up
/// to 50 messages or ~100ms worth, whichever comes first, then marshals the batch via
/// <see cref="Dispatcher.InvokeAsync"/> to keep UI-thread churn bounded under high load.
/// When the collection exceeds <c>maxMessages</c> the oldest entries are trimmed.
/// </summary>
public sealed class UiBatchPump : IDisposable
{
    private readonly ObservableCollection<RichMessage> _target;
    private readonly int _max;
    private readonly Channel<RichMessage> _channel =
        Channel.CreateBounded<RichMessage>(new BoundedChannelOptions(2000)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    /// <summary>Writer side of the bounded channel; producers use this to enqueue messages.</summary>
    public ChannelWriter<RichMessage> Writer => _channel.Writer;

    /// <summary>Raised on the UI thread for each appended message; used by the auto-scroll hook.</summary>
    public event Action<RichMessage>? BatchAppended;

    public UiBatchPump(ObservableCollection<RichMessage> target, int maxMessages)
    {
        _target = target;
        _max = maxMessages;
    }

    /// <summary>
    /// Pump loop. Reads up to 50 messages or for ~100ms (whichever comes first) per batch,
    /// then dispatches the batch to the UI thread. Returns when <paramref name="ct"/> cancels.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var batch = new List<RichMessage>(64);
        while (!ct.IsCancellationRequested)
        {
            batch.Clear();
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(ct)) break;
                var deadline = DateTimeOffset.UtcNow.AddMilliseconds(100);
                while (batch.Count < 50 && _channel.Reader.TryRead(out var m))
                {
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
