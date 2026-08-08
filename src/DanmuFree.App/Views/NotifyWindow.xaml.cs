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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // HWND 就绪后按当前沉浸态同步鼠标穿透。
        WindowClickThrough.SetPassThrough(this, _vm.IsNotifyFloating);
    }

    private void OnCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add) return;
        // 沉浸下命中测试关闭、滚动条透明，依赖 ScrollToBottom（与主弹幕窗同款）。
        Dispatcher.BeginInvoke(new Action(ScrollListToBottom), DispatcherPriority.Render);
    }

    private void ScrollListToBottom()
    {
        if (FindScrollViewer(NotifyList) is { } sv)
        {
            sv.UpdateLayout();
            sv.ScrollToBottom();
        }
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
