using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DanmuFree.App.Services;
using DanmuFree.App.ViewModels;
namespace DanmuFree.App.Views;

public partial class DanmuWindow : Window
{
    private readonly DanmuViewModel _vm;
    private NotifyWindow? _notify;
    public DanmuWindow(DanmuViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.Messages.CollectionChanged += OnCollectionChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        // 还原弹幕窗几何（位置 + 大小），夹到屏内。
        ApplySavedGeometry(vm);
        // 进场/关注通知窗：与主弹幕窗共享同一 VM，独立大小 / 独立沉浸 / 独立显示开关。仅创建一次。
        _notify = new NotifyWindow(_vm);
        Loaded += OnLoaded;
        Closing += OnMainClosing;
    }

    private void ApplySavedGeometry(DanmuViewModel vm)
    {
        if (vm.MainWidth is double w && w >= 120) Width = w;
        if (vm.MainHeight is double h && h >= 100) Height = h;
        if (vm.MainLeft is double l && vm.MainTop is double t && IsOnScreen(l, t, Width, Height))
        { Left = l; Top = t; }
    }

    private void OnMainClosing(object? sender, CancelEventArgs e)
    {
        // 关闭前把两窗几何写回 VM，再存盘（下次启动还原到相同位置）。
        _vm.MainLeft = Left; _vm.MainTop = Top;
        _vm.MainWidth = ActualWidth; _vm.MainHeight = ActualHeight;
        if (_notify is not null && _notify.IsLoaded) // 仅在通知窗被显示过时记录几何，避免存进 0,0
        {
            _vm.NotifyLeft = _notify.Left; _vm.NotifyTop = _notify.Top;
            _vm.NotifyWidth = _notify.ActualWidth; _vm.NotifyHeight = _notify.ActualHeight;
        }
        _vm.SaveSettings();
    }

    private static bool IsOnScreen(double left, double top, double w, double h)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        // 至少 80px 宽、40px 高落在虚拟屏内（多显示器 / 拔显示器后不致于完全飞出）。
        return left + 80 > vl && left < vl + vw - 80
            && top + 40 > vt && top < vt + vh - 40;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_notify is null) return;
        // 还原通知窗几何；没有保存值或飞出屏外时摆在主窗右侧。
        if (_vm.NotifyWidth is double nw && nw >= 120) _notify.Width = nw;
        if (_vm.NotifyHeight is double nh && nh >= 100) _notify.Height = nh;
        if (_vm.NotifyLeft is double nl && _vm.NotifyTop is double nt
            && IsOnScreen(nl, nt, _notify.Width, _notify.Height))
        { _notify.Left = nl; _notify.Top = nt; }
        else
        { _notify.Left = Left + ActualWidth + 12; _notify.Top = Top; }
        ApplyNotifyVisibility();
        // HWND 已就绪，按当前沉浸态同步鼠标穿透。
        WindowClickThrough.SetPassThrough(this, _vm.IsFloating);
    }

    private void ApplyNotifyVisibility()
    {
        if (_vm.ShowNotifyWindow) _notify?.Show();
        else if (_notify?.IsVisible == true) _notify?.Hide(); // 未显示过的窗别 Hide，避免异常 / 存进假几何
    }
    private void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        // 悬浮（沉浸）时 ListBox 不可命中、滚动条隐藏，BringIntoView/ContainerFromIndex 不可靠，
        // 新弹幕进来视图不滚到底会“卡住”。改为直接把内部 ScrollViewer 滚到底：
        // 不依赖命中测试 / 滚动条可见性 / 容器是否已生成。Render 优先级 = 布局完成后执行。
        Dispatcher.BeginInvoke(new Action(ScrollListToBottom), DispatcherPriority.Render);
    }

    private void ScrollListToBottom()
    {
        if (FindScrollViewer(MessageList) is { } sv)
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
    // 隧道事件（XAML 里绑的是 PreviewMouseLeftButtonDown）：先于 ListBox 处理，保证拖动稳定。
    private void OnBackgroundMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_vm.IsFloating) return; // 悬浮模式：背景层已 IsHitTestVisible=False 穿透，此处双保险禁止拖动，避免误触。
        // DragMove 在重入 / 窗口状态不允许（最大化等）时会抛 InvalidOperationException，吞掉以免污染 UI 状态。
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 沉浸(悬浮)：隐藏 resize grip + 开 OS 级鼠标穿透（点击落下层、不抢焦点）；退出复原。
        if (e.PropertyName == nameof(DanmuViewModel.IsFloating))
        {
            ResizeMode = _vm.IsFloating ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            WindowClickThrough.SetPassThrough(this, _vm.IsFloating);
        }
        // 进场/关注窗口的显示开关。
        if (e.PropertyName == nameof(DanmuViewModel.ShowNotifyWindow))
            ApplyNotifyVisibility();
    }
}
