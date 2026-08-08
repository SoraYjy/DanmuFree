using System.Threading.Channels;
using DanmuFree.Core.Tts;
using NAudio.Wave;

namespace DanmuFree.App.Services;

/// <summary>
/// 朗读消费泵：从 bounded Channel 取文本 → GPT-SoVITS 合成 → NAudio 串行播放。
/// 串行不打断（播完一条再下一条）；Channel DropOldest 在洪水时丢最旧、紧跟最新。
/// 单条合成/播放失败：catch 跳过，继续队列，绝不抛回收弹幕主路径。
/// </summary>
public sealed class TtsSpeaker : IDisposable
{
    private readonly ITtsClient _client;
    private readonly Channel<string> _channel;
    private readonly FileLogger? _log;
    private TtsOptions _opts;
    private float _volume = 1f;
    private WaveOutEvent? _current;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public ChannelWriter<string> Writer => _channel.Writer;

    public TtsSpeaker(ITtsClient client, int capacity, FileLogger? log = null)
    {
        _client = client;
        _log = log;
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _opts = new TtsOptions();
    }

    public void Update(TtsOptions opts, double volume)
    {
        _opts = opts;
        _volume = (float)Math.Clamp(volume, 0, 1);
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _current?.Stop();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct))
            {
                if (!_channel.Reader.TryRead(out var text)) continue;
                WaveOutEvent? wo = null;
                WaveFileReader? reader = null;
                try
                {
                    using var stream = await _client.SynthesizeAsync(text, _opts, ct);
                    var tcs = new TaskCompletionSource();
                    wo = new WaveOutEvent { Volume = _volume };
                    _current = wo;
                    reader = new WaveFileReader(stream);
                    wo.PlaybackStopped += (_, _) => tcs.TrySetResult();
                    wo.Init(reader);
                    wo.Play();
                    using var reg = ct.Register(() => wo.Stop());
                    await tcs.Task;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log?.Error("TTS 合成/播放失败，已跳过", ex);
                }
                finally
                {
                    wo?.Dispose();
                    reader?.Dispose();
                    _current = null;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        Stop();
        _channel.Writer.TryComplete();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); }
        catch { } // 防止聚合异常卡死 Dispose
        _cts?.Dispose();
        _cts = null;
    }
}
