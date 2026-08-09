using System.Diagnostics;
using System.IO;
using DanmuFree.Core.Protocol;

namespace DanmuFree.App.Services;

/// <summary>
/// 抖音 X-Bogus 签名器：X-MS-STUB(md5) → node 子进程跑 webmssdk.getSign → X-Bogus。
/// App 层实现（Core 不能引 JS 环境）。sign/sign_runner.js + sign.js + node_modules(jsdom)
/// 随 exe 分发（csproj Content Include sign\**）。webmssdk 反调试只认 jsdom 这种真 DOM 环境，
/// 故必须 node（Jint 跑撞 &gt;32000 层递归），代价是依赖用户机装 node。
/// </summary>
public sealed class DouyinSigner : IDouyinSigner
{
    // 单文件（self-contained single-file）publish 下，AppContext.BaseDirectory 是**解压临时目录**
    // （%TEMP%\.net\DanmuFree\<hash>\），不是 exe 所在目录；node/ 和 sign/ 在 exe 旁。
    // 故用 Environment.ProcessPath 定位真实 exe 目录（开发机 dotnet run 同样成立）。
    private static readonly string AppDir =
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)!;
    private static readonly string SignDir = Path.Combine(AppDir, "sign");
    private static readonly string RunnerPath = Path.Combine(SignDir, "sign_runner.js");
    // 分发自带的 node.exe（node/node.exe）；存在则优先用（用户机无需装 Node），否则回落 PATH 的 node（开发机）。
    private static readonly string BundledNode = Path.Combine(AppDir, "node", "node.exe");
    private static string NodeExe => File.Exists(BundledNode) ? BundledNode : "node";

    /// <summary>启动期检测 node 是否可用（分发自带或 PATH）。无则抖音不可用，B站不受影响。</summary>
    public static bool IsNodeAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo(NodeExe, "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            return p is not null && p.WaitForExit(3000) && p.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<string> SignAsync(string md5, CancellationToken ct)
    {
        if (!File.Exists(RunnerPath))
            throw new InvalidOperationException($"找不到签名脚本：{RunnerPath}（确认 sign/ 已随程序分发）");

        var psi = new ProcessStartInfo(NodeExe, $"\"{RunnerPath}\" {md5}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = SignDir,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("node 启动失败（确认 PATH 有 node）");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        var exited = await Task.Run(() => p.WaitForExit(20_000), ct);
        if (!exited) { try { p.Kill(); } catch { } throw new TimeoutException("node 签名超时（>20s）"); }

        var sig = (await stdoutTask).Trim();
        var err = await stderrTask;
        if (p.ExitCode != 0 || sig.Length == 0)
            throw new InvalidOperationException($"node 未输出签名（exit={p.ExitCode}, stderr: {err.Trim()}）");
        return sig;
    }
}
