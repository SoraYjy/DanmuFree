using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DanmuFree.App.Services;
using DanmuFree.App.ViewModels;

namespace DanmuFree.App.Views;

public partial class NotifyWindow : Window
{
    private readonly DanmuViewModel _vm;

    public NotifyWindow(DanmuViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.NotifyMessages.CollectionChanged += OnCollectionChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += OnLoaded;
        // 仅 app 退出时真正关闭；否则 Alt+F4 一律隐藏（由 ShowNotifyWindow 控制），
        // 避免 Owner-less 窗口被销毁后无法再 Show。
        Closing += OnClosing;
        // 悬浮态抗 Win+D / 显示桌面：被最小化时立刻恢复（仅悬浮态生效）。详见 DanmuWindow 同款注释。
        StateChanged += (_, _) => { if (_vm.IsNotifyFloating && WindowState == WindowState.Minimized) WindowState = WindowState.Normal; };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // HWND 就绪后按当前沉浸态同步鼠标穿透。
        WindowClickThrough.SetPassThrough(this, _vm.IsNotifyFloating);
    }

    private System.Windows.Controls.ScrollViewer? _scroll;   // 缓存列表内部 ScrollViewer（只找一次）
    private bool _scrollQueued;      // 已有滚动在队列 → 后续 Add 合并进那一次（高弹幕量防 UI 刷爆）

    private void OnCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add) return;
        // 每条 Add 各排一次滚动会在高频事件（进场/礼物刷屏）刷爆 UI 线程，合并成每帧一次（详见 DanmuWindow 同款）。
        // 沉浸下命中测试关闭、滚动条透明，依赖 ScrollToBottom。
        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { _scrollQueued = false; ScrollListToBottom(); }), DispatcherPriority.Render);
    }

    private void ScrollListToBottom()
    {
        var sv = _scroll ??= FindScrollViewer(NotifyList);
        if (sv is null) return;
        sv.UpdateLayout();
        sv.ScrollToBottom();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }

    // 隧道事件（XAML 绑 PreviewMouseLeftButtonDown）：先于 ListBox 处理，保证拖动稳定。
    private void OnBackgroundMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_vm.IsNotifyFloating) return; // 沉浸：穿透 + 双保险禁止拖动，避免误触。
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 沉浸(悬浮)：隐藏 resize grip + 开 OS 级鼠标穿透；退出复原。
        if (e.PropertyName == nameof(DanmuViewModel.IsNotifyFloating))
        {
            ResizeMode = _vm.IsNotifyFloating ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            WindowClickThrough.SetPassThrough(this, _vm.IsNotifyFloating);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var main = Application.Current?.MainWindow;
        if (main is null || !main.IsLoaded) return; // app 退出中 → 允许真正关闭
        e.Cancel = true;                            // 否则只隐藏
        _vm.ShowNotifyWindow = false;
    }
}
