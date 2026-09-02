using System.Net.WebSockets;
using System.Text.Json;
using DanmuFree.Core.Models;
using DanmuFree.Core.Protocol;

namespace DanmuFree.Core.Client;

/// <summary>
/// Orchestrates a B站 live danmu websocket end-to-end: room resolution, wss connect,
/// auth handshake (protover 3 / op 7), 30s heartbeat (op 2), a receive loop that
/// accumulates partial frames into a <see cref="MemoryStream"/> and decodes them via
/// <see cref="PacketDecoder"/>, and exponential-backoff reconnection (2s → 4s → 8s →
/// 16s → 32s, capped at 30s) on any failure. Parsed messages are surfaced through
/// <see cref="MessageReceived"/>; lifecycle transitions through
/// <see cref="ConnectionStateChanged"/>.
/// </summary>
/// <remarks>
/// This is the integration layer. Correctness is verified by the Task 13 live-room
/// smoke test rather than by unit tests, per the project plan.
/// </remarks>
public sealed class BilibiliDanmuClient
{
    private readonly PacketDecoder _decoder = new();
    private readonly MessageParser _parser = new();
    private Action<string>? _log;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<RichMessage>? MessageReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;

    public BilibiliDanmuClient() { }

    public async Task ConnectAsync(string roomId, string? cookie, CancellationToken ct, Action<string>? log = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _log = log;
        var resolver = new RoomResolver(new HttpClient(), cookie, log);
        var info = await resolver.ResolveAsync(roomId, _cts.Token);
        SetState(ConnectionState.Connecting);
        await ConnectInternal(info, _cts.Token);
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_ws is not null && _ws.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        try { await (_loop ?? Task.CompletedTask); } catch { }
        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
        SetState(ConnectionState.Disconnected);
    }

    private async Task ConnectInternal(RoomInfo info, CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(info.WssUrl), ct);

                await SendAuth(info);
                SetState(ConnectionState.Connected);
                attempt = 0;

                _loop = Task.Run(() => HeartbeatLoop(ct));
                await ReceiveLoop(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { _log?.Invoke($"B站 连接异常，第 {attempt + 1} 次重连：{e.Message}"); }
            // 指数退避重连
            attempt++;
            SetState(ConnectionState.Reconnecting);
            await Task.Delay(Math.Min(30000, 1000 * (1 << Math.Min(attempt, 5))), ct);
        }
    }

    private async Task SendAuth(RoomInfo info)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = info.Uid,
            roomid = info.RoomId,
            protover = 3,
            platform = "web",
            type = 2,
            key = info.Token,
        });
        var frame = FrameCodec.Encode(op: 7, version: 1, body);
        await _ws!.SendAsync(frame, WebSocketMessageType.Binary, true, _cts!.Token);
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        var ping = FrameCodec.Encode(op: 2, version: 1, Array.Empty<byte>());
        while (!ct.IsCancellationRequested)
        {
            try { await _ws!.SendAsync(ping, WebSocketMessageType.Binary, true, ct); }
            catch { return; }
            await Task.Delay(30_000, ct);
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var acc = new MemoryStream();
        while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
        {
            var result = await _ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
            acc.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var packets = _decoder.Decode(acc.GetBuffer().AsMemory(0, (int)acc.Length), out var rem);
            foreach (var p in packets)
            {
                // ParseAll：一帧可产出多条（SEND_GIFT_V2 的 gift_list 重复字段）。
                foreach (var msg in _parser.ParseAll(p))
                    MessageReceived?.Invoke(msg);
            }
            acc.SetLength(0);
            if (!rem.IsEmpty) acc.Write(rem.Span); // 保留半包
        }
    }

    private void SetState(ConnectionState s) => ConnectionStateChanged?.Invoke(s);
}
