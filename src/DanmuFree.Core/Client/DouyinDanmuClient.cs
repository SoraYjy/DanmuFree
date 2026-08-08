using System.Net.WebSockets;
using System.Text;
using DanmuFree.Core.Models;
using DanmuFree.Core.Protocol;

namespace DanmuFree.Core.Client;

/// <summary>
/// 抖音直播弹幕 WebSocket 客户端，端到端编排：房间解析（真实 room_id + ttwid）、签名（X-Bogus，
/// 经 <see cref="IDouyinSigner"/> 调 node）、wss 握手（ttwid cookie）、10s 心跳、收帧解码
/// （PushFrame→Response→messages）、need_ack 回 ack、messages→<see cref="RichMessage"/>、指数退避重连
/// （2s→4s→…封顶 30s，每次重连重新签名）。公开面与 <see cref="BilibiliDanmuClient"/> 一致，
/// 供 DanmuViewModel 按平台切换。在线人数由 WebcastRoomUserSeqMessage 推送，无需轮询。
/// </summary>
/// <remarks>集成层，正确性靠真机冒烟验证（与 B站 client 同），不单测。</remarks>
public sealed class DouyinDanmuClient
{
    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly IDouyinSigner _signer;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _userUniqueId = "";

    public event Action<RichMessage>? MessageReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;
    // 抖音统计（在线 / 累计看过）走独立事件：WS 推送 RoomUserSeq / RoomStats，不进 RichMessage 流。
    // 注：抖音只推增量点赞(LikeMessage)，无累计点赞总数，故无 Likes。
    public event Action<DouyinRoomStats>? StatsUpdated;

    public DouyinDanmuClient(HttpClient http, IDouyinSigner signer)
    {
        _http = http;
        _signer = signer;
    }

    public async Task ConnectAsync(string webRid, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _userUniqueId = RandomDigits(19); // 匿名设备 id，每次连接新生成
        var resolver = new DouyinRoomResolver(_http);
        var info = await resolver.ResolveAsync(webRid, _cts.Token);
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

    private async Task ConnectInternal(DouyinRoomInfo info, CancellationToken ct)
    {
        int attempt = 0;
        // URL 与 X-MS-STUB(md5) 在一次连接内固定；每次重连只重新调 node 取新 X-Bogus。
        var url = DouyinSign.BuildConnectUrl(info.RoomId, _userUniqueId);
        var stub = DouyinSign.ComputeXBogusStub(DouyinSign.BuildParamString(url));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                _ws.Options.SetRequestHeader("User-Agent", UA);
                _ws.Options.SetRequestHeader("Origin", "https://live.douyin.com");
                if (!string.IsNullOrEmpty(info.Ttwid))
                    _ws.Options.SetRequestHeader("Cookie", $"ttwid={info.Ttwid}");

                var sig = await _signer.SignAsync(stub, ct); // 重连需重新签名
                var fullUrl = DouyinSign.AppendSignature(url, sig);
                await _ws.ConnectAsync(new Uri(fullUrl), ct);

                SetState(ConnectionState.Connected);
                attempt = 0;
                _loop = Task.Run(() => HeartbeatLoop(ct));
                await ReceiveLoop(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* 记日志 */ }
            attempt++;
            SetState(ConnectionState.Reconnecting);
            await Task.Delay(Math.Min(30000, 1000 * (1 << Math.Min(attempt, 5))), ct);
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        var ping = DouyinProto.BuildHeartbeat(); // 空 PushFrame{ f8=gzip(空) }
        while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
        {
            try { await _ws.SendAsync(ping, WebSocketMessageType.Binary, true, ct); }
            catch { return; }
            await Task.Delay(10_000, ct);
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[128 * 1024];
        while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
        {
            WebSocketReceiveResult r;
            using var ms = new MemoryStream();
            do
            {
                r = await _ws.ReceiveAsync(buf, ct);
                ms.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);
            if (r.MessageType == WebSocketMessageType.Close) return;

            var pf = DouyinProto.ReadPushFrame(ms.ToArray());
            if (pf?.Payload is not byte[] resp) continue;

            var (msgs, needAck, internalExt) = DouyinProto.ReadResponse(resp);
            if (needAck && internalExt.Length > 0)
            {
                try { await _ws.SendAsync(DouyinProto.BuildAck(pf.LogId, internalExt), WebSocketMessageType.Binary, true, ct); }
                catch { }
            }

            foreach (var m in msgs)
            {
                // 统计类单独走 StatsUpdated（在线 / 看过）；其余映射为 RichMessage。
                switch (m.Method)
                {
                    case "WebcastRoomUserSeqMessage" when m.Payload is not null:
                        var (current, cumulative) = DouyinProto.ReadRoomUserSeq(m.Payload);
                        StatsUpdated?.Invoke(new DouyinRoomStats(current, cumulative));
                        break;
                    case "WebcastRoomStatsMessage" when m.Payload is not null:
                        StatsUpdated?.Invoke(new DouyinRoomStats(DouyinProto.ReadRoomStatsOnline(m.Payload), null));
                        break;
                    default:
                        if (DouyinMapper.ToRichMessage(m) is { } rm) MessageReceived?.Invoke(rm);
                        break;
                }
            }
        }
    }

    private void SetState(ConnectionState s) => ConnectionStateChanged?.Invoke(s);

    static string RandomDigits(int n)
    {
        var rng = new Random();
        var sb = new StringBuilder(n);
        sb.Append((char)('1' + rng.Next(9))); // 首位非 0，更像真实 uid
        for (int i = 1; i < n; i++) sb.Append((char)('0' + rng.Next(10)));
        return sb.ToString();
    }
}
