using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Settings;
using System.Runtime.InteropServices;
using Avalonia.Media;
using EndfieldChargePlus.Animations;

namespace EndfieldChargePlus.Views;

public partial class NotificationIslandWindow : Window
{
    private readonly NotificationPresentation _state = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private WindowsHudHitTest? _hitTest;
    private readonly PaintedIslandRegion _paintedRegion = new();
    private bool _hovering, _disposed, _hiding;
    private CancellationTokenSource? _intro;
    private bool _entering;
    private double _displayScale=1,_duration=6,_ringProgress=-1;
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
            if(_entering)return;
            if (_state.Tick(now, hovered)) { await HideAnimatedAsync(); return; }
            if (IsVisible) TimeLabel.Text = Pinned ? LocalizationManager.TranslateLiteral("已固定 · 再點收起") : hovered ? LocalizationManager.TranslateLiteral("停留中 · 移開後收起") : Countdown(now);
            UpdateCountdown(now);
        };
        LayoutUpdated += (_, _) => UpdateRegion();
        PropertyChanged+=(_,e)=>{if(e.Property==BoundsProperty&&Bounds.Width>32)ApplyCompactLayout(Bounds.Width/_displayScale);};
        Closed += (_, _) => { _disposed = true; _intro?.Cancel();_intro?.Dispose(); IslandTransition.Cancel(this); _timer.Stop(); _hitTest?.Dispose(); };
    }
    public void ShowNotification(NotificationCard card, AppSettings settings)
    {
        if (_disposed) return;
        Current = card;
        AppLabel.Text = string.IsNullOrWhiteSpace(card.App) ? LocalizationManager.TranslateLiteral("Windows 通知") : card.App;
        ToolTip.SetTip(TimeLabel, card.Created.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
        TitleLabel.Text = settings.NotificationHideContent ? LocalizationManager.TranslateLiteral("收到新通知") : card.Title;
        BodyLabel.Text = settings.NotificationHideContent ? LocalizationManager.TranslateLiteral("內容已隱藏；請從 Windows 通知中心查看") : card.Body;
        BodyLabel.MaxLines = 1;BodyScroll.Offset=default;
        _displayScale=Math.Clamp(settings.GlobalScale,.4,1.4);NoticeScale.LayoutTransform=new ScaleTransform(_displayScale,_displayScale);Height=106*_displayScale;
        _duration=double.IsFinite(settings.DisplayDurationSeconds)?Math.Clamp(settings.DisplayDurationSeconds,3,10):6;
        var screen = settings.MonitorIndex >= 0 && settings.MonitorIndex < Screens.All.Count ? Screens.All[settings.MonitorIndex] : Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            Width = Math.Min(576*_displayScale, Math.Max(160, screen.WorkingArea.Width / scale - 32));
            Position = new PixelPoint(screen.WorkingArea.X + (int)Math.Round((screen.WorkingArea.Width - Width * scale) / 2), screen.WorkingArea.Y + (int)Math.Round(8 * scale));
        }
        _hovering = false; _state.Show(DateTimeOffset.UtcNow, settings.DisplayDurationSeconds);
        TimeLabel.Text = Countdown(DateTimeOffset.UtcNow);
        _hiding = false; IslandTransition.PrepareShow(this);
        ShowActivated = false; if (!IsVisible) Show();
        ApplyCompactLayout(Width/_displayScale);
        var handle = this.TryGetPlatformHandle();
        if (handle is not null) _hitTest ??= WindowsHudHitTest.TryAttach(handle.Handle, point =>
        {
            return ContainsScreenPoint(point);
        }, () => Dispatcher.UIThread.Post(BodyClick));
        _hitTest?.Reapply(); UpdateRegion(); _timer.Start(); _ = IslandTransition.FadeInAsync(this);
        NoticeBadge.Data=Geometry.Parse("M12 2 C7.6 2 5 5.5 5 10 V15 L3 18 H21 L19 15 V10 C19 5.5 16.4 2 12 2 Z M9 21 C9 24 15 24 15 21");
        _ringProgress=-1;UpdateCountdown(DateTimeOffset.UtcNow);_ = PlayOriginalIntroAsync(settings);
    }
    private void BodyClick()
    {
        if (!IsVisible) return;
        _state.Click();
        if (!_state.Visible) { _ = HideAnimatedAsync(); return; }
        BodyLabel.MaxLines = 0; TimeLabel.Text = LocalizationManager.TranslateLiteral("已固定 · 再點收起");
        NoticeBadge.Data=Geometry.Parse("M8 2 H16 V5 L15 6 V11 L19 15 V17 H5 V15 L9 11 V6 L8 5 Z M12 17 V23");UpdateCountdown(DateTimeOffset.UtcNow);
        UpdateRegion();
    }
    public void ApplyCompactLayout(double width)
    {
        NoticeRoot.Width=width>=572?576:width;Island.Width=Math.Max(144,NoticeRoot.Width-16);
        TitleLabel.FontSize=width<500?14:17;TimeLabel.MaxWidth=width<400?100:140;
    }
    private void UpdateCountdown(DateTimeOffset now)
    {
        double progress=Pinned||_entering?1:Math.Clamp(_state.RemainingSeconds(now)/_duration,0,1);
        if(Math.Abs(progress-_ringProgress)<.0001)return;
        _ringProgress=progress;CountdownArc.Data=OriginalCapsuleIntro.Ring(progress);
    }
    private async Task PlayOriginalIntroAsync(AppSettings settings)
    {
        _intro?.Cancel();_intro?.Dispose();var source=_intro=new CancellationTokenSource();_entering=true;
        var options=AnimationOptions.FromSettings(settings) with{DurationSeconds=3,SurfaceWidth=Island.Width,SurfaceHeight=60};
        try{await OriginalCapsuleIntro.RunAsync(this,Island,NoticeContent,"Notice",options,source.Token);}
        catch(OperationCanceledException){}
        finally
        {
            if(ReferenceEquals(_intro,source)&&!_disposed)
            {
                OriginalCapsuleIntro.Finish(this,Island,NoticeContent,"Notice");_entering=false;
                if(IsVisible&&!_hiding&&_state.Visible&&!Pinned)_state.Show(DateTimeOffset.UtcNow,_duration);
                UpdateCountdown(DateTimeOffset.UtcNow);UpdateRegion();
            }
        }
    }
    private string Countdown(DateTimeOffset now)=>LocalizationManager.Text($"{Math.Ceiling(_state.RemainingSeconds(now))}秒後收起",$"Dismiss in {Math.Ceiling(_state.RemainingSeconds(now))}s");
    private void UpdateRegion()
        => _paintedRegion.Update(this, Island, _hitTest);
    private bool ContainsScreenPoint(PixelPoint point)
    {
        var p = this.PointToClient(point);
        var origin=Island.TranslatePoint(default,this)??default;
        return IsVisible && IslandGeometry.Contains(p.X-origin.X,p.Y-origin.Y,Island.Bounds.Width*_displayScale,Island.Bounds.Height*_displayScale,Island.Bounds.Height*_displayScale/2);
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
        _intro?.Cancel();_entering=false;IslandTransition.Cancel(this); _state.Hide(); _timer.Stop(); _hovering = false; _hiding = false;
        bool wasVisible = IsVisible;
        if (wasVisible) Hide(); Opacity = 1;
        Current = null; AppLabel.Text = TitleLabel.Text = BodyLabel.Text = TimeLabel.Text = "";
        if (wasVisible) IslandHidden?.Invoke();
    }
    private async Task HideAnimatedAsync()
    {
        if (_hiding || !IsVisible) return;
        _hiding = true; _intro?.Cancel();_timer.Stop();
        await IslandTransition.FadeOutAsync(this);
        if (_hiding) HideIsland();
    }
}
