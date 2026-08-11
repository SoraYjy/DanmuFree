using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace DanmuFree.Core.Tts;

/// <summary>
/// Edge「大声朗读」在线 TTS 客户端：调 Edge 浏览器 Read Aloud 用的 Azure 神经音色，
/// 免 key、免注册、免部署（朋友「不会装 GPT-SoVITS」的对症解法）。实现 Core 的
/// <see cref="ITtsClient"/>，返回 WAV 流复用 TtsSpeaker 的 NAudio 串行播放。
///
/// 协议（reverse-engineered，官方无文档；跟踪 rany2/edge-tts）：
/// ① WSS <c>wss://speech.platform.bing.com/.../readaloud/edge/v1?TrustedClientToken=...&amp;Sec-MS-GEC=&lt;token&gt;&amp;ConnectionId=&lt;guid&gt;</c>
/// ② 每次合成 = 新建一条 WS：发 speech.config（指定输出格式）+ ssml（含音色/语速/文本）两个文本帧，
///    收二进制帧（前 2 字节大端=头长度，其后是头，再后是音频数据）攒 PCM，收到文本帧 Path:turn.end 结束。
/// ③ DRM token <see cref="BuildSecMsGec"/>：服务端 2025 年起强制校验，缺失/错误 → 403。
/// 输出格式用 raw-24khz-16bit-mono-pcm（纯 PCM，无 RIFF 头），最后由 <see cref="BuildWav"/> 套 44 字节头。
///
/// 注意：非官方端点、ToS 灰色（MS 可再改 token 算法 / 版本号，2025 年断过、上游几天内修好）；
/// 失败经 TtsSpeaker catch → FileLogger 落盘。无网络时弹幕本就收不到，故「需联网」不是负担。
/// </summary>
public sealed class EdgeTtsClient : ITtsClient
{
    private const string WssHost = "speech.platform.bing.com";
    private const string WssPath = "/consumer/speech/synthesize/readaloud/edge/v1";
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    // Chromium 版本号常量（token 校验用，跟随官方变化时改这一处即可；2026-08 取自 rany2/edge-tts）。
    private const string SecMsGecVersion = "1-143.0.3650.75";
    // 探针实测（2026-08）：免费 Edge 端点主动拒绝 raw-/riff-/audio-...pcm 等 PCM 格式串（"Unsupported
    // output format"），只接受 MP3/Opus 串。但请求 audio-...mp3 时服务实际返回 **24kHz/16bit/mono
    // PCM WAV（RIFF）**——直接喂 WaveFileReader 即可，无需解码。若服务行为变了，IsRiff 失效时会回落到 BuildWav。
    private const string OutputFormat = "audio-24khz-48kbitrate-mono-mp3";
    private const string EdgeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";
    private const string EdgeOrigin = "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold";

    /// <summary>默认音色（女声「晓晓」，Azure 神经音，中文朗读万金油）。</summary>
    public const string DefaultVoice = "zh-CN-XiaoxiaoNeural";

    /// <summary>UI 下拉用的中文音色清单（Edge 端点实测可用的 zh-CN 主线 + 方言）。</summary>
    public static readonly string[] SupportedVoices =
    {
        "zh-CN-XiaoxiaoNeural",   // 晓晓 女
        "zh-CN-XiaoyiNeural",     // 晓伊 女
        "zh-CN-YunxiNeural",      // 云希 男
        "zh-CN-YunjianNeural",    // 云健 男
        "zh-CN-YunyangNeural",    // 云扬 男
        "zh-CN-YunxiaNeural",     // 云夏 男（童声感）
        "zh-CN-liaoning-XiaobeiNeural", // 东北话
        "zh-CN-shaanxi-XiaoniNeural",   // 陕西话
    };

    private readonly string _voice;

    public EdgeTtsClient(string? voice = null) => _voice = string.IsNullOrWhiteSpace(voice) ? DefaultVoice : voice!;

    public async Task<Stream> SynthesizeAsync(string text, TtsOptions opts, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var gec = BuildSecMsGec(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var url = new StringBuilder()
            .Append("wss://").Append(WssHost).Append(WssPath).Append('?')
            .Append("TrustedClientToken=").Append(TrustedClientToken)
            .Append("&Sec-MS-GEC=").Append(gec)
            .Append("&Sec-MS-GEC-Version=").Append(Uri.EscapeDataString(SecMsGecVersion))
            .Append("&ConnectionId=").Append(connectionId);

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("User-Agent", EdgeUserAgent);
        ws.Options.SetRequestHeader("Origin", EdgeOrigin);

        // 整体超时：防止服务端静默挂死堵塞朗读泵（串行播放，一条卡死=后续全不念）。
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            await ws.ConnectAsync(new Uri(url.ToString()), timeoutCts.Token);

            var ts = TimestampNow();
            // ① speech.config：指定输出格式（实测请求 mp3 串、服务返回 PCM WAV，详见 OutputFormat 注释）
            //   注意 JSON 用普通拼接、不要用 $ 插值：插值串里 }} 会转义成单个 }，brace 计数极易错（曾因此 400）
            await SendTextAsync(ws, timeoutCts.Token,
                $"X-RequestId:{connectionId}\r\n" +
                "Content-Type:application/json; charset=utf-8\r\n" +
                $"X-Timestamp:{ts}\r\n" +
                "Path:speech.config\r\n" +
                "\r\n" +
                "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":" +
                "{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\",\"sessionEndEventsEnabled\":\"false\"}," +
                "\"outputFormat\":\"" + OutputFormat + "\"}}}}");

            // ② ssml：音色 + 语速 + 文本
            var ssml = BuildSsml(_voice, text, SpeedToRate(opts.Speed));
            await SendTextAsync(ws, timeoutCts.Token,
                $"X-RequestId:{connectionId}\r\n" +
                "Content-Type:application/ssml+xml\r\n" +
                $"X-Timestamp:{ts}\r\n" +
                "Path:ssml\r\n" +
                "\r\n" +
                ssml);

            // ③ 收帧：文本帧找 Path:turn.end，二进制帧按 2 字节头长度抠音频
            using var audio = new MemoryStream();
            var recv = new byte[8192];
            var msg = new MemoryStream();
            var sawTurnEnd = false;
            while (!sawTurnEnd)
            {
                msg.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(recv, timeoutCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new HttpRequestException($"Edge TTS 连接被关闭（{ws.CloseStatus} {ws.CloseStatusDescription}）");
                    msg.Write(recv, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var body = Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length);
                    if (body.Contains("Path:turn.end", StringComparison.Ordinal)) sawTurnEnd = true;
                }
                else // Binary
                {
                    var chunk = ExtractAudio(msg.GetBuffer(), (int)msg.Length);
                    if (chunk is not null) audio.Write(chunk, 0, chunk.Length);
                }
            }

            if (audio.Length == 0)
                throw new HttpRequestException("Edge TTS 未返回音频（音色名错误或 token 被服务拒绝，详见日志）。");

            // 实测服务返回 RIFF/WAV（以 RIFF 开头）→ 原样返回；若将来变成纯 PCM，回落 BuildWav 套头。
            var pcm = audio.ToArray();
            var wav = IsRiff(pcm) ? pcm : BuildWav(pcm, sampleRate: 24000, bits: 16, channels: 1);
            return new MemoryStream(wav, writable: false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new HttpRequestException("Edge TTS 超时（20s 无 turn.end，可能网络不通或端点变更）。");
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
                catch { /* 关闭失败忽略，ws 即将 Dispose */ }
            }
        }
    }

    private static async Task SendTextAsync(ClientWebSocket ws, CancellationToken ct, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    /// <summary>从一条已组装好的二进制消息里抠出音频载荷（去掉「2 字节大端头长度 + 头文本」）。
    /// 一条 WS 二进制消息 = 一个段：Path:audio 段抠音频；Path:audio.metadata（也含 "Path:audio" 子串）跳过。</summary>
    private static byte[]? ExtractAudio(byte[] msg, int length)
    {
        if (length < 2) return null;
        var headerLen = (msg[0] << 8) | msg[1];
        var payloadStart = 2 + headerLen;
        if (payloadStart > length) return null;
        var header = Encoding.UTF8.GetString(msg, 2, headerLen);
        if (!header.Contains("Path:audio", StringComparison.Ordinal) ||
            header.Contains("Path:audio.metadata", StringComparison.Ordinal))
            return null;
        var payloadLen = length - payloadStart;
        var audio = new byte[payloadLen];
        Buffer.BlockCopy(msg, payloadStart, audio, 0, payloadLen);
        return audio;
    }

    // ── 纯逻辑（可单测，无需网络） ──────────────────────────────────────────

    /// <summary>当前时间戳（Edge 约定的 JS 风格字符串，服务端不严格校验内容）。</summary>
    private static string TimestampNow() =>
        DateTimeOffset.UtcNow.ToString("ddd MMM d yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
            CultureInfo.InvariantCulture);

    /// <summary>
    /// 计算 DRM token Sec-MS-GEC（2025 年起服务强制校验，缺失/错误 → 403）。
    /// 算法（跟踪 rany2/edge-tts drm）：unix 秒 +30 偏移 + Windows 纪元 11644473600，
    /// 向下取整到 300 秒桶，×1e7 转成 100ns ticks，拼 token 常量，SHA-256 大写 hex。
    /// </summary>
    public static string BuildSecMsGec(long unixSeconds, int clockSkew = 30)
    {
        long total = unixSeconds + clockSkew + 11644473600L;
        total -= total % 300;          // 向下取整到最近的 300s
        long ticks = total * 10_000_000L;
        var seed = $"{ticks}{TrustedClientToken}";
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(seed));
        return Convert.ToHexString(hash); // 大写 hex
    }

    /// <summary>构造 SSML（音色 + 语速百分比 + 文本，XML 转义防注入）。结构跟踪 rany2/edge-tts。</summary>
    public static string BuildSsml(string voice, string text, string rate)
    {
        var v = EscapeXml(voice);
        var escaped = EscapeXml(text);
        return
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='zh-CN'>" +
            $"<voice name='{v}'>" +
            $"<prosody rate='{EscapeXml(rate)}' pitch='+0Hz' volume='+0%'>" +
            $"{escaped}" +
            "</prosody></voice></speak>";
    }

    /// <summary>语速（1.0=正常）映射成 Edge prosody rate 百分比：1.0→"+0%"，2.0→"+100%"，0.5→"-50%"。</summary>
    public static string SpeedToRate(double speed)
    {
        var pct = (int)Math.Round((speed - 1.0) * 100);
        return pct >= 0 ? $"+{pct}%" : $"{pct}%";
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("'", "&apos;").Replace("\"", "&quot;");

    private static bool IsRiff(byte[] b) => b.Length >= 4 &&
        b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F';

    /// <summary>给纯 PCM 套 44 字节标准 WAV 头（PCM/16bit/mono/24kHz）。</summary>
    public static byte[] BuildWav(byte[] pcm, int sampleRate = 24000, int bits = 16, int channels = 1)
    {
        int blockAlign = channels * bits / 8;
        int byteRate = sampleRate * blockAlign;
        var wav = new byte[44 + pcm.Length];
        using var w = new BinaryWriter(new MemoryStream(wav));
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                 // fmt 块大小（PCM 固定 16）
        w.Write((short)1);           // 音频格式 = PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bits);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
        return wav;
    }
}
