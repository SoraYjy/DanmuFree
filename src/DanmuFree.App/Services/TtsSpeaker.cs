using System.IO;
using System.Threading.Channels;
using DanmuFree.Core.Tts;
using NAudio.Wave;

namespace DanmuFree.App.Services;

/// <summary>
/// 朗读消费泵：从 bounded Channel 取文本 → ITtsClient 合成 → NAudio 串行播放。
/// 合成返回 WAV（GPT-SoVITS / SystemSpeech）直读 WaveFileReader；返回 MP3（Edge 在线）用 Mp3FileReader 解码。
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
                WaveStream? reader = null;
                try
                {
                    using var stream = await _client.SynthesizeAsync(text, _opts, ct);
                    var tcs = new TaskCompletionSource();
                    wo = new WaveOutEvent { Volume = _volume };
                    _current = wo;
                    // 嗅探格式：RIFF → WAV（GPT-SoVITS / SystemSpeech）；否则 MP3（Edge 在线引擎）。
                    // Core 的 EdgeTtsClient 只返 MP3（服务不吐 PCM），解码用 NAudio 放 App 层。
                    reader = IsWavStream(stream) ? new WaveFileReader(stream) : new Mp3FileReader(stream);
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

    /// <summary>嗅探音频流头部：RIFF → WAV（WaveFileReader），否则按 MP3（Mp3FileReader）解码。
    /// 读后还原 Position，reader 仍从头读。流需可 seek（各 client 均返回 MemoryStream）。</summary>
    private static bool IsWavStream(Stream s)
    {
        if (!s.CanSeek) return false;
        long pos = s.Position;
        try
        {
            Span<byte> h = stackalloc byte[4];
            int n = s.Read(h);
            return n >= 4 && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F';
        }
        finally { s.Position = pos; }
    }
}
