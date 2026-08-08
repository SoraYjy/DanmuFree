using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
}
