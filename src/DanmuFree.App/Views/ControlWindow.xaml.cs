using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DanmuFree.App.ViewModels;
using Microsoft.Win32;
namespace DanmuFree.App.Views;

public partial class ControlWindow : Window
{
    public ControlWindow(DanmuViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // 还原几何（位置 + 大小），夹到屏内；可调大小 + 持久化（窗口被拖动/缩放后下次还原）。
        ApplySavedGeometry(vm);
        Closing += OnControlClosing;
    }

    private void ApplySavedGeometry(DanmuViewModel vm)
    {
        if (vm.ControlWidth is double w && w >= MinWidth) Width = w;
        if (vm.ControlHeight is double h && h >= MinHeight) Height = h;
        if (vm.ControlLeft is double l && vm.ControlTop is double t && IsOnScreen(l, t, Width, Height))
        { Left = l; Top = t; }
    }

    private void OnControlClosing(object? sender, CancelEventArgs e)
    {
        // 写回几何再存盘（DanmuWindow.Closing 也会 SaveSettings，幂等）。
        if (DataContext is DanmuViewModel vm)
        {
            vm.ControlLeft = Left; vm.ControlTop = Top;
            vm.ControlWidth = ActualWidth; vm.ControlHeight = ActualHeight;
            vm.SaveSettings();
        }
    }

    private static bool IsOnScreen(double left, double top, double w, double h)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        return left + 80 > vl && left < vl + vw - 80
            && top + 40 > vt && top < vt + vh - 40;
    }
    // 标题栏拖动；点关闭按钮（ButtonBase）不触发拖动。
    private void OnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is DependencyObject d && AncestorOf<ButtonBase>(d)) return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
    private static bool AncestorOf<T>(DependencyObject d) where T : class
    {
        while (d is not null)
        {
            if (d is T) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    private void OnQrLoginClick(object sender, RoutedEventArgs e)
    {
        var dlg = new LoginDialog { Owner = this };
        dlg.ShowDialog();
        if (dlg.Cookie is not null && DataContext is DanmuViewModel vm)
        {
            vm.Cookie = dlg.Cookie;
            vm.SaveSettings();
        }
    }
    private void OnPickTtsRefAudio(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|所有文件|*.*" };
        if (dlg.ShowDialog() == true && DataContext is ViewModels.DanmuViewModel vm)
            vm.TtsRefAudioPath = dlg.FileName;
    }

    // 定向回复规则行「…」：选音频文件，写回该行规则（sender 的 DataContext 即行模型）。
    private void OnPickReplySound(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.ReplyRuleViewModel rule) return;
        var dlg = new OpenFileDialog { Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|所有文件|*.*" };
        if (dlg.ShowDialog() == true) rule.SoundPath = dlg.FileName;
    }

    // ── 描边色：色块按钮点开 Win32 颜色盘（System.Windows.Forms.ColorDialog，FullOpen=自带光谱）──

    private void OnPickDanmuOutlineColor(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.DanmuViewModel vm)
            vm.DanmuOutlineColor = PickColor(vm.DanmuOutlineColor);
    }

    private void OnPickNotifyOutlineColor(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.DanmuViewModel vm)
            vm.NotifyOutlineColor = PickColor(vm.NotifyOutlineColor);
    }

    /// <summary>打开颜色盘选色；取消/失败返回原值。</summary>
    private string PickColor(string currentText)
    {
        var current = Colors.Black;
        try { current = (Color)ColorConverter.ConvertFromString(currentText); }
        catch (FormatException) { }
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B),
        };
        return dlg.ShowDialog(new WpfWindowHandle(this)) == System.Windows.Forms.DialogResult.OK
            ? $"#{dlg.Color.A:X2}{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}"
            : currentText;
    }

    // 把 WPF 窗口句柄包给 WinForms 对话框作 owner（保持在主窗前）。
    private sealed class WpfWindowHandle(Window window) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle => new WindowInteropHelper(window).Handle;
    }
}
