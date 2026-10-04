using System;
using System.Runtime.InteropServices;
using Avalonia;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Interop;

/// <summary>
/// Gives only the visible HUD body a real native hit target while the transparent
/// remainder of the 1200x160 host window stays mouse-through. This lets the user
/// click the island itself without adding a separate PIN/LOCK window.
/// </summary>
internal sealed class WindowsHudHitTest : IDisposable
{
    private const int GwlExStyle = -20;
    private const int GwlpWndProc = -4;
    private const uint WsExLayered = 0x00080000u;
    private const uint WsExTransparent = 0x00000020u;

    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmLButtonUp = 0x0202;
    private const int HtClient = 1;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private readonly IntPtr _hwnd;
    private readonly Func<PixelPoint, bool> _isInteractivePoint;
    private readonly Action _onBodyClick;
    private readonly bool _nativeControls;
    private NativeWndProc? _nativeWndProc;
    private IntPtr _oldWndProc;
    private bool _disposed;
    private bool _transitionInputTransparent;
    private bool _failureLogged;
    private (int Left, int Top, int Right, int Bottom, int Diameter)? _region;

    public IntPtr Handle => _hwnd;

    private delegate IntPtr NativeWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private WindowsHudHitTest(
        IntPtr hwnd,
        Func<PixelPoint, bool> isInteractivePoint,
        Action onBodyClick, bool nativeControls = false)
    {
        _hwnd = hwnd;
        _isInteractivePoint = isInteractivePoint;
        _onBodyClick = onBodyClick;
        _nativeControls = nativeControls;
        InstallHook();
        Reapply();
    }

    public static WindowsHudHitTest? TryAttach(
        IntPtr hwnd,
        Func<PixelPoint, bool> isInteractivePoint,
        Action onBodyClick, bool nativeControls = false)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return null;

        try
        {
            return new WindowsHudHitTest(hwnd, isInteractivePoint, onBodyClick, nativeControls);
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to initialize native HUD hit testing.", ex);
            return null;
        }
    }

    public void Reapply()
    {
        if (_disposed || _hwnd == IntPtr.Zero)
            return;

        try
        {
            // Keep per-pixel transparency support, but do not mark the entire native
            // window WS_EX_TRANSPARENT. WM_NCHITTEST below decides which pixels click
            // through and which pixels belong to the visible island.
            uint before = GetExtendedStyle();
            uint wanted = (before | WsExLayered) & ~WsExTransparent;
            if(_transitionInputTransparent)wanted|=WsExTransparent;
            if (wanted != before)
                SetExtendedStyle(wanted);

            if (!SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
            {
                LogFailure($"HUD SetWindowPos failed. Win32Error={Marshal.GetLastPInvokeError()}.");
                return;
            }

            InstallHook();
            _failureLogged = false;
        }
        catch (Exception ex)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            AppLog.Error("Failed to apply native HUD hit testing.", ex);
        }
    }
    public void SetTransitionInputTransparent(bool value)
    {
        if(_disposed || _transitionInputTransparent==value)return;
        _transitionInputTransparent=value;Reapply();
    }
    public void SetInteractiveRegion(Rect body, double scaling, double radius)
    {
        if (_disposed || body.Width <= 0 || body.Height <= 0 || scaling <= 0) return;
        var region = ((int)Math.Floor(body.Left * scaling), (int)Math.Floor(body.Top * scaling),
            (int)Math.Ceiling(body.Right * scaling), (int)Math.Ceiling(body.Bottom * scaling),
            (int)Math.Round(radius * 2 * scaling));
        if (_region == region) return;
        var handle = CreateRoundRectRgn(region.Item1, region.Item2, region.Item3, region.Item4, region.Item5, region.Item5);
        if (handle == IntPtr.Zero) { LogFailure("Unable to create island input region."); return; }
        // Windows owns the HRGN after success; pixels outside it are outside this
        // HWND, so hit testing reaches windows in other processes as well.
        if (SetWindowRgn(_hwnd, handle, true) == 0)
        { DeleteObject(handle); LogFailure("Unable to set island input region."); return; }
        _region = region;
    }
    public bool SetPaintedRegion((int Left, int Right)[] rows)
    {
        if (_disposed || rows.Length == 0) return false;
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero) return false;
        for (int y = 0; y < rows.Length; y++)
        {
            if (rows[y].Right <= rows[y].Left) continue;
            int start = y;
            while (y + 1 < rows.Length && rows[y + 1] == rows[start]) y++;
            var run = CreateRectRgn(rows[start].Left, start, rows[start].Right, y + 1);
            if (run == IntPtr.Zero) { DeleteObject(region); return false; }
            int combined = CombineRgn(region, region, run, 2 /* RGN_OR */);
            DeleteObject(run);
            if (combined == 0) { DeleteObject(region); return false; }
        }
        // Keep every antialiased pixel, but exclude completely transparent pixels.
        // A rounded GDI region cannot represent the renderer's fractional outline.
        if (SetWindowRgn(_hwnd, region, true) == 0)
        { DeleteObject(region); LogFailure("Unable to apply painted island region."); return false; }
        _region = null;
        return true; // Windows owns the successfully applied HRGN.
    }
    private void InstallHook()
    {
        if (_disposed || _oldWndProc != IntPtr.Zero)
            return;

        _nativeWndProc = NativeWindowProc;
        IntPtr proc = Marshal.GetFunctionPointerForDelegate(_nativeWndProc);
        _oldWndProc = IntPtr.Size == 8
            ? SetWindowLongPtr64(_hwnd, GwlpWndProc, proc)
            : new IntPtr(SetWindowLong32(_hwnd, GwlpWndProc, proc.ToInt32()));

        if (_oldWndProc == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWindowLongPtr(GWLP_WNDPROC) failed. Win32Error={Marshal.GetLastPInvokeError()}.");
    }

    private IntPtr NativeWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmNcHitTest)
        {
            PixelPoint point = ScreenPointFromLParam(lParam);
            return new IntPtr(!_transitionInputTransparent && _isInteractivePoint(point) ? HtClient : HtTransparent);
        }

        if (msg == WmMouseActivate)
            return new IntPtr(_nativeControls && !_transitionInputTransparent ? 1 : MaNoActivate);
        if (msg == WmLButtonUp && !_nativeControls)
        {
            if (GetCursorPos(out NativePoint p))
            {
                var point = new PixelPoint(p.X, p.Y);
                if (_isInteractivePoint(point))
                {
                    try { _onBodyClick(); }
                    catch (Exception ex) { AppLog.Error("HUD body click callback failed.", ex); }
                    return IntPtr.Zero;
                }
            }
        }

        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private static PixelPoint ScreenPointFromLParam(IntPtr lParam)
    {
        long packed = lParam.ToInt64();
        int x = unchecked((short)(packed & 0xFFFF));
        int y = unchecked((short)((packed >> 16) & 0xFFFF));
        return new PixelPoint(x, y);
    }

    private void LogFailure(string message)
    {
        if (_failureLogged)
            return;
        _failureLogged = true;
        AppLog.Warn(message);
    }

    private uint GetExtendedStyle() => IntPtr.Size == 4
        ? unchecked((uint)GetWindowLong32(_hwnd, GwlExStyle))
        : unchecked((uint)GetWindowLongPtr64(_hwnd, GwlExStyle).ToInt64());

    private void SetExtendedStyle(uint style)
    {
        if (IntPtr.Size == 4)
            SetWindowLong32(_hwnd, GwlExStyle, unchecked((int)style));
        else
            SetWindowLongPtr64(_hwnd, GwlExStyle, new IntPtr(unchecked((long)style)));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
        {
            try
            {
                if (IntPtr.Size == 8)
                    SetWindowLongPtr64(_hwnd, GwlpWndProc, _oldWndProc);
                else
                    SetWindowLong32(_hwnd, GwlpWndProc, _oldWndProc.ToInt32());
            }
            catch { }

            _oldWndProc = IntPtr.Zero;
        }

        _nativeWndProc = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", ExactSpelling = true, SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(
        IntPtr lpPrevWndFunc,
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);
}
