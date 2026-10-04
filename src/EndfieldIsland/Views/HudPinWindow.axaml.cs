using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace EndfieldChargePlus.Views;

public partial class HudPinWindow : Window
{
    public event Action? PinClicked;

    private delegate IntPtr NativeWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private NativeWndProc? _nativeWndProc;
    private IntPtr _oldWndProc;
    private IntPtr _hookedHwnd;

    public HudPinWindow()
    {
        InitializeComponent();
        if (!OperatingSystem.IsWindows())
            PinButton.Click += OnPinClicked;

        Closed += (_, _) => RemoveNativeClickHook();
    }

    private void OnPinClicked(object? sender, RoutedEventArgs e) => PinClicked?.Invoke();

    public void ApplyState(bool pinned, bool enabled, bool topmost, double opacity)
    {
        Topmost = topmost;
        Opacity = Math.Clamp(opacity, 0.25, 1.0);
        PinButton.IsEnabled = enabled;
        PinButton.Content = pinned ? "LOCK" : "PIN";

        var accent = new SolidColorBrush(Color.Parse("#FFC6CA4C"));
        var normalBorder = new SolidColorBrush(Color.Parse("#7071766B"));
        var pinnedBackground = new SolidColorBrush(Color.Parse("#F03A3A2B"));
        var normalBackground = new SolidColorBrush(Color.Parse("#F02A292B"));
        var normalForeground = new SolidColorBrush(Color.Parse("#FFD8D9C7"));

        PinShell.BorderBrush = pinned ? accent : normalBorder;
        PinShell.Background = pinned ? pinnedBackground : normalBackground;
        PinButton.Foreground = pinned ? accent : normalForeground;
    }
    public void ShowWithoutActivation()
    {
        if (!IsVisible)
            Show();

        MakeNativeWindowInteractive();
        EnsureNativeClickHook();
        Dispatcher.UIThread.Post(() =>
        {
            MakeNativeWindowInteractive();
            EnsureNativeClickHook();
        }, DispatcherPriority.Loaded);
    }

    private void MakeNativeWindowInteractive()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = this.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
            return;

        IntPtr hwnd = handle.Handle;
        long style = IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, GwlExStyle).ToInt64()
            : GetWindowLong32(hwnd, GwlExStyle);
        long wanted = style & ~WsExTransparent;

        if (wanted != style)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(wanted));
            else
                SetWindowLong32(hwnd, GwlExStyle, unchecked((int)wanted));

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
    }

    private void EnsureNativeClickHook()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = this.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
            return;

        IntPtr hwnd = handle.Handle;
        if (_hookedHwnd == hwnd && _oldWndProc != IntPtr.Zero)
            return;

        RemoveNativeClickHook();

        _nativeWndProc = NativeWindowProc;
        IntPtr proc = Marshal.GetFunctionPointerForDelegate(_nativeWndProc);
        _oldWndProc = IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, GwlpWndProc, proc)
            : new IntPtr(SetWindowLong32(hwnd, GwlpWndProc, proc.ToInt32()));
        _hookedHwnd = hwnd;
    }

    private void RemoveNativeClickHook()
    {
        if (_hookedHwnd == IntPtr.Zero || _oldWndProc == IntPtr.Zero)
            return;

        try
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(_hookedHwnd, GwlpWndProc, _oldWndProc);
            else
                SetWindowLong32(_hookedHwnd, GwlpWndProc, _oldWndProc.ToInt32());
        }
        catch { }

        _hookedHwnd = IntPtr.Zero;
        _oldWndProc = IntPtr.Zero;
        _nativeWndProc = null;
    }

    private IntPtr NativeWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmLButtonUp && PinButton.IsEnabled)
        {
            Dispatcher.UIThread.Post(() => PinClicked?.Invoke());
            return IntPtr.Zero;
        }

        return _oldWndProc != IntPtr.Zero
            ? CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam)
            : DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void HideControl()
    {
        if (IsVisible)
            Hide();
    }

    private const int GwlExStyle = -20;
    private const int GwlpWndProc = -4;
    private const uint WmLButtonUp = 0x0202;
    private const long WsExTransparent = 0x00000020L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", ExactSpelling = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}