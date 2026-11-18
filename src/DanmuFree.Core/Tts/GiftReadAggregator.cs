namespace DanmuFree.Core.Tts;

/// <summary>
/// 礼物朗读聚合器（纯逻辑，Core 可单测）。
/// 连送同用户+同礼物会在一个窗口内累加，只产出一条「xx 送了 N 个 yy」，
/// 避免 100 连送被读 100 次。换用户/换礼物立即吐出上一组；窗口到期吐当前组。
/// 窗口/定时由上层（App 的 GiftTtsPump）驱动，本类不含时间依赖。
/// 礼物是事件型，**用户名是语义必需**（「谁送了什么」），始终带用户名，不受读用户名开关影响。
/// </summary>
public sealed class GiftReadAggregator
{
    private string? _user;
    private string? _gift;
    private int _count;
    private bool _pending;

    /// <summary>感谢文案模板（空/空白 = 内置默认文案）。占位符 {用户名} {礼物名} {数量}；
    /// 运行中可换（下层泵 UpdateTemplate 直改），下次组队吐出即生效。</summary>
    public string? Template { get; set; }

    /// <summary>加一条礼物。返回需立即出队的「上一组」文本：
    /// 同组（同用户+同礼物）累加 → null；换组 → 上一组文本（若先前有攒）。</summary>
    public string? Add(string user, string? extra)
    {
        if (!TryParse(extra, out var name, out var n)) return null;
        if (_pending && _user == user && _gift == name)
        {
            _count += n;
            return null;
        }
        string? prev = _pending ? FormatCurrent() : null;   // 换组：仅在有攒时先把旧的念出来
        _user = user;
        _gift = name;
        _count = n;
        _pending = true;
        return prev;
    }

    /// <summary>窗口到期 / 收尾：吐出当前累计（无攒则 null）。</summary>
    public string? Flush() => _pending ? FormatCurrent() : null;

    private string FormatCurrent()
    {
        var text = Format(_user!, _gift!, _count, Template);
        _pending = false;
        _user = null;
        _gift = null;
        _count = 0;
        return text;
    }

    /// <summary>礼物朗读文本。template 空 → 内置默认（始终带用户名，1 个省数量、多个带数量）；
    /// 有模板 → 填 {用户名}/{礼物名}/{数量} 占位符（{数量}=实际个数，1 也照填，可预期性优先），
    /// 未认识的占位符原样保留。用户可自定义如「感谢{用户名}送的{礼物名}」。</summary>
    public static string Format(string user, string gift, int count, string? template = null)
    {
        if (string.IsNullOrWhiteSpace(template))
            return count <= 1 ? $"{user} 送了 {gift}" : $"{user} 送了 {count} 个 {gift}";
        // 替换顺序 {数量}→{礼物名}→{用户名}（最可控的最后换）：昵称完全用户可控，含占位符字样时
        // 轮到它那一步已过、原样保留；礼物名半可控，最多被用户名再吃一层。
        return template
            .Replace("{数量}", count.ToString())
            .Replace("{礼物名}", gift)
            .Replace("{用户名}", user);
    }

    /// <summary>解析 Extra「佛跳墙 x2」→（佛跳墙, 2）；「礼物」→（礼物, 1）；空/无效 → false。</summary>
    public static bool TryParse(string? extra, out string name, out int count)
    {
        name = "";
        count = 0;
        if (string.IsNullOrWhiteSpace(extra)) return false;
        var idx = extra.LastIndexOf(" x", StringComparison.Ordinal);
        if (idx > 0 && int.TryParse(extra[(idx + 2)..], out var c) && c >= 1)
        {
            name = extra[..idx];
            count = c;
            return true;
        }
        name = extra;
        count = 1;
        return true;
    }
}
