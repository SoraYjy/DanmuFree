using System.Text;

namespace DanmuFree.Core.Tts;

/// <summary>
/// TTS 异常 → 面板人话短句（纯函数，Core 单测）。输入是各 client / runner 抛上来的
/// HttpRequestException 消息（App 层已写成人话）+ 底层 socket/HTTP 原文，按关键词归类：
/// 微软服务端故障 / 403 时钟 / 超时 / 文件缺失 / 服务不可达 / 参考音频；未识别 → 消息头截断。
/// 用途：TtsHealthMonitor 心跳失败时在控制窗「朗读」TAB 显示（如 2026-10-26 Edge 全局 500 事故，
/// 用户能一眼看出「不是软件坏了」）。
/// </summary>
public static class TtsErrorClassifier
{
    public static string Classify(Exception? ex)
    {
        if (ex is null) return "";
        // socket / 协议错误常在 InnerException 链里（如 "No connection could be made..."），串起来再归类
        var sb = new StringBuilder(ex.Message);
        for (var cur = ex.InnerException; cur is not null && sb.Length < 300; cur = cur.InnerException)
            sb.Append(' ').Append(cur.Message);
        return Classify(sb.ToString());
    }

    public static string Classify(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "未知错误";
        // 顺序敏感：具体规则在前，兜底截断在最后
        if (Contains(message, "服务端故障", "not you", "bingparachute", "aren't available"))
            return "微软 Edge 朗读服务端故障（官方事故，等恢复即可）";
        if (message.Contains("403"))
            return "Edge 拒绝（403）：本机时钟偏差可能过大，请校准 Windows 时间";
        if (Contains(message, "超时", "timeout"))
            return "连接超时：网络不通或被拦截";
        if (Contains(message, "找不到", "启动失败"))
            return "程序文件缺失（edge/ 或 node），请用完整发行包";
        if (Contains(message, "拒绝连接", "refused", "No connection", "name or servname", "No such host"))
            return "服务不可达：本地服务未启动或地址不对";
        if (message.Contains("参考音频"))
            return "GPT-SoVITS 参考音频无效（须 3~10 秒）";
        var head = message.Length <= 60 ? message : message[..60] + "…";
        return head.ReplaceLineEndings(" ");
    }

    private static bool Contains(string haystack, params string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
