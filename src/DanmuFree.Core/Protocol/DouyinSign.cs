using System.Security.Cryptography;
using System.Text;

namespace DanmuFree.Core.Protocol;

/// <summary>
/// 抖音 WS signature（X-Bogus）素材：纯 C#，可在 Core 单测。
/// 算法（saermart）：
///   MD5(按 13 字段顺序拼的 "k=v,k=v,...") → X-MS-STUB；X-MS-STUB 再喂 webmssdk.getSign → X-Bogus。
/// getSign 依赖 webmssdk 反调试 JS（Jint 撞 &gt;32000 层递归跑不动），由 App 层 DouyinSigner 调 node 子进程实现
/// （见 <see cref="IDouyinSigner"/>）；本类只算 md5，与 JS 环境解耦。
/// </summary>
public static class DouyinSign
{
    /// <summary>签名参数的固定拼接顺序（必须严格按此序，逗号分隔）。</summary>
    public static readonly string[] ParamOrder =
    {
        "live_id", "aid", "version_code", "webcast_sdk_version",
        "room_id", "sub_room_id", "sub_channel_id", "did_rule",
        "user_unique_id", "device_platform", "device_type", "ac", "identity",
    };

    const string WssHost = "wss://webcast3-ws-web-lf.douyin.com/webcast/im/push/v2/";
    // 域名必须带 -ws-web-；老的 webcast3-normal-lf 已 NXDOMAIN。

    /// <summary>构造不含 signature 的 WSS 连接 URL（签名在其后追加）。</summary>
    public static string BuildConnectUrl(string roomId, string userUniqueId)
    {
        var internalExt = Uri.EscapeDataString(
            $"internal_src:dim|wss_push_room_id:{roomId}|wss_push_did:{userUniqueId}" +
            "|first_req_ms:0|fetch_time:0|seq:1|wss_info:0-0-0-0|wrds_v:0");
        return WssHost +
               "?app_name=douyin_web&version_code=180800&webcast_sdk_version=1.3.0&update_version_code=1.3.0" +
               "&compress=gzip&live_id=1&aid=6383&did_rule=3&device_platform=web&identity=audience" +
               $"&room_id={roomId}&user_unique_id={userUniqueId}" +
               "&cursor=d-1_u-1&host=https://live.douyin.com&im_path=/webcast/im/fetch/" +
               "&need_persist_msg_count=15&support_wrds=1" +
               $"&internal_ext={internalExt}";
    }

    /// <summary>从 URL query 取 ParamOrder 各字段（URL 解码），按序拼成 "k=v,k=v,..."。</summary>
    public static string BuildParamString(string url)
    {
        int q = url.IndexOf('?');
        var query = q >= 0 ? url[(q + 1)..] : "";
        var map = new Dictionary<string, string>();
        foreach (var kv in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq >= 0) map[kv[..eq]] = Uri.UnescapeDataString(kv[(eq + 1)..]);
        }
        return string.Join(",", ParamOrder.Select(k => $"{k}={map.GetValueOrDefault(k, "")}"));
    }

    /// <summary>X-MS-STUB = MD5(paramString) 小写十六进制。此值喂 webmssdk.getSign 得 X-Bogus。</summary>
    public static string ComputeXBogusStub(string paramString)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(paramString))).ToLowerInvariant();

    /// <summary>把签名追加到连接 URL。</summary>
    public static string AppendSignature(string urlWithoutSignature, string signature)
        => urlWithoutSignature + "&signature=" + Uri.EscapeDataString(signature);
}

/// <summary>X-MS-STUB(md5) → X-Bogus 的抽象。Core 不能引 App，故 node 子进程实现在 App 层。</summary>
public interface IDouyinSigner
{
    /// <summary>输入 <see cref="DouyinSign.ComputeXBogusStub"/> 的 md5，返回 X-Bogus。</summary>
    Task<string> SignAsync(string md5, CancellationToken ct);
}
