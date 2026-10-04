using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Settings;
using System.Runtime.InteropServices;

namespace EndfieldChargePlus.Views;

public partial class NotificationIslandWindow : Window
{
    private readonly NotificationPresentation _state = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private WindowsHudHitTest? _hitTest;
    private readonly PaintedIslandRegion _paintedRegion = new();
    private bool _hovering, _disposed, _hiding;
    public NotificationCard? Current { get; private set; }
    public bool Pinned => _state.Pinned;
    public event Action? IslandHidden;
    public NotificationIslandWindow()
    {
        InitializeComponent();
        Island.PointerEntered += (_, _) => _hovering = true;
        Island.PointerExited += (_, _) => _hovering = false;
        _timer.Tick += async (_, _) =>
        {
            var now = DateTimeOffset.UtcNow; bool hovered = IsHovered();
            if (_state.Tick(now, hovered)) { await HideAnimatedAsync(); return; }
            if (IsVisible) TimeLabel.Text = Pinned ? LocalizationManager.TranslateLiteral("已固定 · 再點收起") : hovered ? LocalizationManager.TranslateLiteral("停留中 · 移開後收起") : Countdown(now);
        };
        LayoutUpdated += (_, _) => UpdateRegion();
        Closed += (_, _) => { _disposed = true; IslandTransition.Cancel(this); _timer.Stop(); _hitTest?.Dispose(); };
    }
    public void ShowNotification(NotificationCard card, AppSettings settings)
    {
        if (_disposed) return;
        Current = card;
        AppLabel.Text = string.IsNullOrWhiteSpace(card.App) ? LocalizationManager.TranslateLiteral("Windows 通知") : card.App;
        ToolTip.SetTip(TimeLabel, card.Created.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
        TitleLabel.Text = settings.NotificationHideContent ? LocalizationManager.TranslateLiteral("收到新通知") : card.Title;
        BodyLabel.Text = settings.NotificationHideContent ? LocalizationManager.TranslateLiteral("內容已隱藏；請從 Windows 通知中心查看") : card.Body;
        BodyLabel.MaxLines = 2; Height = 136; BodyScroll.Offset=default;
        var screen = settings.MonitorIndex >= 0 && settings.MonitorIndex < Screens.All.Count ? Screens.All[settings.MonitorIndex] : Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            Width = Math.Min(560, Math.Max(160, screen.WorkingArea.Width / scale - 32));
            Position = new PixelPoint(screen.WorkingArea.X + (int)Math.Round((screen.WorkingArea.Width - Width * scale) / 2), screen.WorkingArea.Y + (int)Math.Round(8 * scale));
        }
        _hovering = false; _state.Show(DateTimeOffset.UtcNow, settings.DisplayDurationSeconds);
        TimeLabel.Text = Countdown(DateTimeOffset.UtcNow);
        _hiding = false; IslandTransition.PrepareShow(this);
        ShowActivated = false; if (!IsVisible) Show();
        var handle = this.TryGetPlatformHandle();
        if (handle is not null) _hitTest ??= WindowsHudHitTest.TryAttach(handle.Handle, point =>
        {
            return ContainsScreenPoint(point);
        }, () => Dispatcher.UIThread.Post(BodyClick));
        _hitTest?.Reapply(); UpdateRegion(); _timer.Start(); _ = IslandTransition.FadeInAsync(this);
    }
    private void BodyClick()
    {
        if (!IsVisible) return;
        _state.Click();
        if (!_state.Visible) { _ = HideAnimatedAsync(); return; }
        BodyLabel.MaxLines = 0; TimeLabel.Text = LocalizationManager.TranslateLiteral("已固定 · 再點收起");
        UpdateRegion();
    }
    private string Countdown(DateTimeOffset now)=>LocalizationManager.Text($"{Math.Ceiling(_state.RemainingSeconds(now))}秒後收起",$"Dismiss in {Math.Ceiling(_state.RemainingSeconds(now))}s");
    private void UpdateRegion()
        => _paintedRegion.Update(this, Island, _hitTest);
    private bool ContainsScreenPoint(PixelPoint point)
    {
        var p = this.PointToClient(point);
        return IsVisible && IslandGeometry.Contains(p.X - 8, p.Y - 8, Bounds.Width - 16, Bounds.Height - 16, 60);
    }
    private bool IsHovered()
    {
        // WM_MOUSEMOVE need not arrive when a window appears underneath a stationary pointer.
        if (OperatingSystem.IsWindows() && GetCursorPos(out var p))
            return ContainsScreenPoint(new PixelPoint(p.X, p.Y)) && GetAncestor(WindowFromPoint(p), 2) == this.TryGetPlatformHandle()?.Handle;
        return _hovering;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    public void HideIsland()
    {
        IslandTransition.Cancel(this); _state.Hide(); _timer.Stop(); _hovering = false; _hiding = false;
        bool wasVisible = IsVisible;
        if (wasVisible) Hide(); Opacity = 1;
        Current = null; AppLabel.Text = TitleLabel.Text = BodyLabel.Text = TimeLabel.Text = "";
        if (wasVisible) IslandHidden?.Invoke();
    }
    private async Task HideAnimatedAsync()
    {
        if (_hiding || !IsVisible) return;
        _hiding = true; _timer.Stop();
        await IslandTransition.FadeOutAsync(this);
        if (_hiding) HideIsland();
    }
}
