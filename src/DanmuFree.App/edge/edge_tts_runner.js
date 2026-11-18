// Edge TTS（Read Aloud）合成 runner：由 EdgeNodeTtsClient 以 node 子进程调用。
// 背景（2026-10）：MS 边缘层按 TLS 指纹拦客户端——.NET SChannel 连 voice list 可以、
// 合成路径一律 403；OpenSSL（node）+ 新域名 + 旧版 v1 token 实测出音频。故合成走本脚本。
//
// 协议：wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1
//       ?Ocp-Apim-Subscription-Key=<TrustedClientToken>   ← 老常量 token 换个参数名
//       &Sec-MS-GEC=<v1 token：SHA256("{300s取整的ticks}{token}")大写hex>
//       &Sec-MS-GEC-Version=1-144.0.3719.82&ConnectionId=<32hex>
//   headers: User-Agent(Mac/Chrome144) + origin(chrome-extension)。UA 必须带（实测缺 UA 被拒）。
//   两文本帧 speech.config + ssml；二进制帧剥 2 字节大端头长 + 头，把 MP3 原样写 stdout；
//   文本帧 Path:turn.end 结束 exit 0。任何失败 stderr + exit 1（.NET 侧转异常落 log.txt）。
//
// 用法：node edge_tts_runner.js --voice zh-CN-XiaoxiaoNeural --rate "+0%" [--timeout 30]
//       stdin：要合成的文本（utf8）。stdout：MP3 裸流。
// 依赖：ws（edge/node_modules/ws，纯 JS 零原生依赖，随包分发；node 内置 WebSocket 不能自定义头）。

'use strict'
const { createHash, randomUUID } = require('node:crypto')
const WebSocket = require('ws')

const TRUSTED_TOKEN = '6A5AA1D4EAFF4E9FB37E23D68491D6F4'
const WS_BASE = 'wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1'
const GEC_VERSION = '1-144.0.3719.82'
const UA = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36 Edg/144.0.0.0'
const ORIGIN = 'chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold'

function arg(name, fallback) {
    const i = process.argv.indexOf('--' + name)
    return i > 0 && i + 1 < process.argv.length ? process.argv[i + 1] : fallback
}

// Sec-MS-GEC v1：unix 秒 + Windows 纪元，向下取整 300 秒，×1e7 ticks，拼 token，SHA-256 大写 hex
function secMsGec(unixSeconds) {
    const ticks = BigInt(Math.floor(unixSeconds + 11644473600)) * 10000000n
    const rounded = ticks - (ticks % 3000000000n)
    return createHash('sha256').update(`${rounded}${TRUSTED_TOKEN}`, 'ascii').digest('hex').toUpperCase()
}

function escapeXml(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/'/g, '&apos;').replace(/"/g, '&quot;')
}

const dbg = (m) => { if (process.env.EDGE_TTS_DEBUG) process.stderr.write(`[dbg] ${m}
`) }

function fail(msg) {
    process.stderr.write(String(msg) + '\n')
    process.exit(1)
}

function readStdin() {
    return new Promise((resolve, reject) => {
        const chunks = []
        process.stdin.on('data', (c) => chunks.push(c))
        process.stdin.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')))
        process.stdin.on('error', reject)
    })
}

async function main() {
    const voice = arg('voice', 'zh-CN-XiaoxiaoNeural')
    const rate = arg('rate', '+0%')
    const timeoutMs = parseInt(arg('timeout', '30000'), 10)
    const text = (await readStdin()).trim()
    dbg(`stdin=${JSON.stringify(text)} len=${text.length}`)
    if (!text) fail('empty text')

    // 时钟偏差兜底：token 按 300s 桶校验，本机偏差 ~409s（实测）时会随桶相位在「能连/403」间翻转。
    // 依次试 0/±300/±600 桶，覆盖 ±10 分钟偏差（对齐 Core.EdgeTtsClient 的 403 重试策略）。
    const now = Math.floor(Date.now() / 1000)
    let lastErr = 'unknown'
    for (const offset of [0, -300, 300, -600, 600]) {
        try {
            dbg(`attempt offset=${offset}`)
            await synth(voice, rate, timeoutMs, text, secMsGec(now + offset))
            return // synth 在 turn.end 时已 exit(0)
        } catch (e) {
            lastErr = e && e.message ? e.message : String(e)
            if (!/403/.test(lastErr)) break // 非 403（超时/网络）换桶无意义，直接报
        }
    }
    fail(/403/.test(lastErr)
        ? 'Edge TTS 握手被拒（403）：已自动尝试 ±10 分钟时间桶仍失败——本机时钟偏差可能超 10 分钟（请校准 Windows 时间），或服务端再变更。'
        : lastErr)
}

function synth(voice, rate, timeoutMs, text, gec) {
    return new Promise((resolve, reject) => {
        const cid = randomUUID().replaceAll('-', '')
        const url = `${WS_BASE}?Ocp-Apim-Subscription-Key=${TRUSTED_TOKEN}` +
            `&Sec-MS-GEC=${gec}&Sec-MS-GEC-Version=${GEC_VERSION}&ConnectionId=${cid}`

        const ws = new WebSocket(url, { headers: { 'User-Agent': UA }, origin: ORIGIN })
        const killer = setTimeout(() => { try { ws.terminate() } catch {} reject(new Error(`timeout after ${timeoutMs}ms`)) }, timeoutMs)
        let done = false
        let pending = 0
        // stdout 是管道 = 异步写；只有「turn.end 已到 + 全部音频块确认刷出」才退出，
        // 否则 exit(0) 会丢未刷出的音频（实测管道下 write('') 回调会抢先触发）
        const maybeExit = () => { if (done && pending === 0) process.exit(0) }

        ws.on('error', (e) => { if (!done) { clearTimeout(killer); reject(e) } })
        ws.on('close', () => { if (!done) { clearTimeout(killer); reject(new Error('ws closed before turn.end')) } })
        // 非 101 握手响应：ws 默认只在 error 里给「Unexpected server response: NNN」，用户看不懂。
        // 分类成人话：403 交回调用方的时钟桶 sweep（消息里必须含 "403"）；其余多为 MS 服务端事故
        // （2026-10-26 实测：故障期新域名回 500/200 + Bing「熊猫伞」错误页 "It's not you, it's us"、
        // 老域名回 "Our services aren't available"——两端点同时故障页、裸连 403 风控仍活，实锤服务端事故）。
        ws.on('unexpected-response', (req, res) => {
            const st = res.statusCode
            if (st === 403) {
                // 立即释放连接再交回 sweep 换桶，否则 403 socket 要等 30s killer 才收（全 403 时最多 5 条并存）
                clearTimeout(killer); try { ws.terminate() } catch { }
                reject(new Error('403: handshake rejected')); return // 含 "403" → sweep 换桶重试
            }
            let body = ''
            res.on('data', (c) => { if (body.length < 400) body += c.toString('utf8') })
            res.on('end', () => {
                if (done) return
                clearTimeout(killer)
                dbg(`handshake rejected status=${st} body=${JSON.stringify(body.slice(0, 120))}`)
                reject(new Error(/not you|bingparachute|aren't available/i.test(body)
                    ? `HTTP ${st}：微软 Edge 朗读服务端故障（返回服务中断错误页），不是本机问题，请稍后再试`
                    : `Edge 朗读握手被拒：HTTP ${st}（200/5xx 多为微软服务端故障——2026-10-26 实测其故障期在 200/500 间抖动；若持续超半天再查本机代理/安全软件）`))
            })
        })
        ws.on('message', (data, isBinary) => {
            if (process.env.EDGE_TTS_DEBUG && isBinary) dbg(`bin frame len=${data.length}`)
            if (isBinary) {
                if (data.length < 2) return
                const headerLen = (data[0] << 8) | data[1]
                if (2 + headerLen > data.length) return
                const header = data.slice(2, 2 + headerLen).toString('utf8')
                if (header.includes('Path:audio') && !header.includes('Path:audio.metadata')) {
                    pending++
                    process.stdout.write(data.slice(2 + headerLen), () => { pending--; maybeExit() })
                }
                return
            }
            dbg(`text frame: ${data.toString('utf8').slice(0, 120)}`)
            if (data.toString('utf8').includes('Path:turn.end')) {
                done = true
                clearTimeout(killer)
                try { ws.close() } catch { }
                maybeExit()
            }
        })

        ws.on('open', () => {
            const ts = new Date().toUTCString()
            ws.send(`X-RequestId:${cid}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n` +
                '{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"false"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}')
            const ssml = `<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='zh-CN'>` +
                `<voice name='${escapeXml(voice)}'><prosody rate='${escapeXml(rate)}' pitch='+0Hz' volume='+0%'>${escapeXml(text)}</prosody></voice></speak>`
            ws.send(`X-RequestId:${cid}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:${ts}\r\nPath:ssml\r\n\r\n${ssml}`)
        })
    })
}

main().catch((e) => fail(e && e.message ? e.message : e))
