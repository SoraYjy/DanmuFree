using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DanmuFree.App.Services;

/// <summary>
/// 切换 WPF 窗口的 Win32 <c>WS_EX_TRANSPARENT</c>：开启后整个窗口对鼠标「穿透」——
/// 点击直接落到下层窗口（例如全屏 / 无边框游戏），本窗既不抢焦点也不接收任何输入。
/// </summary>
/// <remarks>
/// 仅靠 WPF 的 <c>IsHitTestVisible=False</c> 不够：<c>AllowsTransparency=True</c> 的分层透明窗
/// 仍会在 OS 层占用其屏幕区域，点击照样激活本窗 HWND（表现为游戏里点中弹幕窗 → 游戏失焦）。
/// <c>WS_EX_TRANSPARENT</c> 在 OS 层彻底放行（WPF 透明窗本就是 layered，满足其生效前提）。
/// 用法：进沉浸(悬浮)时 <c>SetPassThrough(window,true)</c>，退出时 <c>false</c>。
/// </remarks>
public static class WindowClickThrough
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    public static void SetPassThrough(Window window, bool passThrough)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return; // 窗口尚未创建 HWND（忽略；Loaded 后再同步）

        int ex = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        int value = passThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        if (value == ex) return;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)value);
    }

    // 指针宽度自适应：x64 走 *Ptr，x86 走 SetWindowLong。GWL_EXSTYLE 是 32 位值，两种都安全。
    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : (IntPtr)GetWindowLong32(hwnd, index);

    private static void SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, index, value);
        else SetWindowLong32(hwnd, index, value.ToInt32());
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
}
