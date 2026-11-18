using System.IO;
using System.Windows;
using System.Windows.Threading;
using DanmuFree.App.Services;
using DanmuFree.App.ViewModels;
using DanmuFree.App.Views;

namespace DanmuFree.App;

public partial class App : Application
{
    private readonly FileLogger _log = new();

    // 退出中标志：Application.Shutdown() 会逐窗触发 Closing，顺序=创建序（通知窗在主窗构造函数内
    // 创建、先完成注册）——通知窗的 Closing 会先于主窗的 SaveSettings 执行。若此时通知窗走
    // 「用户手动隐藏」分支把 ShowNotifyWindow 改成 false，主窗随后存盘就把 false 持久化了
    // （症状：每次重启「显示进场/关注窗口」都被取消勾选）。所有退出入口先把此标志置 true，
    // 通知窗的 Closing 见标志即不动 VM 状态。仅进程退出语义，ControlWindow 被单独关闭不置位。
    public static bool IsExiting;
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, a) => { _log.Error("UI 未捕获异常", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => _log.Error("AppDomain 异常", a.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, a) => { _log.Error("未观察 Task 异常", a.Exception); a.SetObserved(); };
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var vm = new DanmuViewModel();
        var main = new DanmuWindow(vm);
        MainWindow = main;
        main.Show();
        // 控制面板常驻：随主窗启动、独立任务栏项、可最小化；× 退出程序。
        var control = new ControlWindow(vm);
        control.Show();
        base.OnStartup(e);

        // 抖音签名依赖本机 node；后台探测，缺失仅记日志（不弹窗、不影响 B站）。
        System.Threading.Tasks.Task.Run(() =>
        {
            if (!DouyinSigner.IsNodeAvailable())
                _log.Error("未检测到 node，抖音弹幕不可用（B站不受影响）。请安装 Node.js 并加入 PATH。");
        });
    }
}
