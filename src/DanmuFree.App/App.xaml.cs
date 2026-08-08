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
