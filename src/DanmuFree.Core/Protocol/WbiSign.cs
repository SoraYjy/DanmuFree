using System.Security.Cryptography;

namespace DanmuFree.Core.Protocol;

/// <summary>
/// B站 WBI 签名（<c>w_rid</c>）。2023 年起 B站对部分查询接口强制 WBI 鉴权——缺/错 <c>w_rid</c>
/// 即返回 <c>code:-352</c>。实测（2026-08）：直播 <c>getDanmuInfo</c> 即便带上登录 cookie + 浏览器
/// UA/Referer/Origin 仍 -352，**加上 w_rid 即 code:0**（弹幕 token / host_list 正常返回）。
///
/// 算法（跟踪 SocialSisterYi/bilibili-API-collect，逆向工程，官方无文档）：
/// ① <c>img_key/sub_key</c> 取自 nav 接口 <c>data.wbi_img.{img_url,sub_url}</c> 的文件名（去 <c>.png</c>），
///   全站统一、约每日更替（由调用方现取，本类不缓存）。
/// ② <c>mixin_key</c>：把 <c>img_key+sub_key</c> 按 <see cref="MixinKeyEncTab"/> 重排、取前 32 位。
/// ③ 加 <c>wts</c>（unix 秒）；按 key 升序拼成 query；<c>w_rid = MD5(query + mixin_key)</c> 小写 hex。
///
/// 纯逻辑（无网络、无第三方），<see cref="GetMixinKey"/>/<see cref="Sign"/> 可单测（用文档固定向量回归）。
/// </summary>
public static class WbiSign
{
    /// <summary>固定重排映射表（64 项，全站统一，B站未公开变更过）。取前 32 项生成 mixin_key。</summary>
    public static readonly int[] MixinKeyEncTab =
    {
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
        22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52,
    };

    /// <summary><c>img_key + sub_key</c> → <c>mixin_key</c>（按表重排取前 32 位）。</summary>
    public static string GetMixinKey(string imgKey, string subKey)
    {
        var raw = imgKey + subKey;
        var sb = new System.Text.StringBuilder(32);
        for (int i = 0; i < 32; i++)
            sb.Append(raw[MixinKeyEncTab[i]]);
        return sb.ToString();
    }

    /// <summary>
    /// 对一组参数做 WBI 签名，返回可直接拼到 URL 的
    /// <c>"k=v&amp;...&amp;wts=..&amp;w_rid=.."</c>（参数已按 key 升序、值按 encodeURIComponent 编码、
    /// 过滤 <c>!'()*</c>）。<c>wts</c> 由调用方传入以便单测固定。
    /// </summary>
    public static string Sign(IReadOnlyDictionary<string, string> parameters, string mixinKey, long unixNow)
    {
        var sorted = new SortedDictionary<string, string>();
        foreach (var kv in parameters) sorted[kv.Key] = kv.Value;
        sorted["wts"] = unixNow.ToString();
        var query = new System.Text.StringBuilder();
        foreach (var kv in sorted)
        {
            if (query.Length > 0) query.Append('&');
            query.Append(Encode(kv.Key)).Append('=').Append(Encode(Filter(kv.Value)));
        }
        var wRid = Convert.ToHexString(
            MD5.HashData(System.Text.Encoding.UTF8.GetBytes(query.ToString() + mixinKey))).ToLowerInvariant();
        return query + "&w_rid=" + wRid;
    }

    // encodeURIComponent 兼容：A-Za-z0-9-_.~ 原样，其余按 UTF-8 百分号编码（大写 hex）。
    // 注：getDanmuInfo 的 id/type 都是纯数字，编码对它们是恒等；此实现为通用正确性（中文/特殊字符场景）。
    private static string Encode(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(s))
        {
            if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') ||
                b == '-' || b == '_' || b == '.' || b == '~')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2"));
            }
        }
        return sb.ToString();
    }

    // 过滤 !"!'()*" 字符（WBI 规范：这些字符不参与签名）。
    private static string Filter(string s)
    {
        if (s.IndexOf('!') < 0 && s.IndexOf('\'') < 0 && s.IndexOf('(') < 0 && s.IndexOf(')') < 0 && s.IndexOf('*') < 0)
            return s;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (c is not ('!' or '\'' or '(' or ')' or '*'))
                sb.Append(c);
        return sb.ToString();
    }
}
