using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Platform;
using EndfieldChargePlus.Animations;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Views;

public partial class HudWindow : Window
{
    private CancellationTokenSource? _cts;
    private AppSettings _settings = new();
    private AnimationOptions _animOptions = AnimationOptions.Default;
    private bool _persistent;
    private bool _sessionPinned;
    private WindowsHudHitTest? _hitTest;
    private HudRenderData? _lastRenderData;
    private bool _overviewActive;
    private double SurfaceWidth=>560;
    private double SurfaceHeight=>60;
    private readonly DispatcherTimer _clockTimer;
    private bool _bodyToggleEnabled;

    public event Action? PinToggleRequested;

    // The progress ring is deliberately animated independently from the existing HUD
    // summon/retract animations. Data updates therefore feel continuous without changing
    // any of the original layout or animation timelines.
    private readonly DispatcherTimer _progressTimer;
    private double _displayedProgress;
    private double _progressFrom;
    private double _progressTo;
    private DateTime _progressStartedUtc;
    private bool _progressInitialized;
    private static readonly TimeSpan ProgressTransitionDuration = TimeSpan.FromMilliseconds(320);

    public bool IsHudBusy { get; private set; }

    public HudWindow()
    {
        InitializeComponent();
        LocalizationManager.ApplyStaticText(this);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClockText();

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _progressTimer.Tick += (_, _) => TickProgressAnimation();

        Closed += (_, _) =>
        {
            _progressTimer.Stop();
            _clockTimer.Stop();
            _hitTest?.Dispose();
            _hitTest = null;
        };
        ResetToInitial();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _animOptions = AnimationOptions.FromSettings(settings);
        GlobalScale.RenderTransform = new ScaleTransform(settings.GlobalScale, settings.GlobalScale);
        Opacity = Math.Clamp(settings.HudOpacity, 0.0, 1.0);
        ApplyWindowLayer(_persistent);
        UpdateClockText();

        if (IsVisible)
            PositionHud();
    }

    private void ApplyWindowLayer(bool persistent)
    {
        // 臨時 HUD 保持原行為：始終置頂。
        // 常駐 HUD 可以選擇置頂，或作為普通非置頂視窗留在桌面層。
        Topmost = !persistent || _sessionPinned || _settings.PersistentLayer == PersistentHudLayer.Topmost;
        // Avalonia can update HWND styles after a layer change. Keep the HUD
        // mouse-through in both persistent (topmost/normal) and transient modes.
        if (IsVisible)
        {
            EnsureInputHitTest();
            Dispatcher.UIThread.Post(EnsureInputHitTest, DispatcherPriority.Loaded);
        }
    }

    public async Task ShowPreviewAsync(
        HudRenderData data,
        bool allowPin = true)
    {
        if (IsVisible)
            await HideAnimatedAsync();

        _persistent = false;
        _sessionPinned = false;
        _bodyToggleEnabled = allowPin;
        ApplyWindowLayer(false);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var localCts = _cts;
        var ct = localCts.Token;
        IsHudBusy = true;

        ApplyRenderData(data);
        bool originalOverview=data.Metrics is {Count:4}&&!data.SimpleAnimation&&IslandTransition.AnimationsEnabled;
        if(originalOverview)ResetToInitial();else PreparePreviewInitialState();
        ShowPositioned();

        try
        {
            if(originalOverview)
                await OriginalCapsuleIntro.RunAsync(this,Pill,NumHost,"",_animOptions with{DurationSeconds=3},ct);
            else await Task.WhenAll(
                HudAnimations.PreviewPillIn(SurfaceWidth,SurfaceHeight).RunAsync(Pill, ct),
                HudAnimations.PreviewContentIn().RunAsync(BoltIcon, ct),
                HudAnimations.PreviewContentIn().RunAsync(NumHost, ct),
                HudAnimations.PreviewContentIn().RunAsync(ClockHost, ct));

            SetPersistentFinalState();
            _bodyToggleEnabled = allowPin;
            EnsureInputHitTest();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if(ReferenceEquals(_cts,localCts))IsHudBusy = false;
        }
    }

    public void PromotePreviewToPersistent()
    {
        if (!IsVisible)
            return;

        _persistent = true;
        _sessionPinned = true;
        _cts?.Cancel();
        _bodyToggleEnabled = !_settings.AlwaysVisible;
        ApplyWindowLayer(true);
        SetPersistentFinalState();
        EnsureInputHitTest();
    }

    public bool IsPointInsideInteractiveHud(PixelPoint screenPoint)
    {
        if (!IsVisible)
            return false;

        return IsPointInsideVisibleHud(screenPoint);
    }

    public async Task ShowCustomAsync(
        HudRenderData data,
        Func<CancellationToken, Task<HudRenderData>>? liveRefresh = null)
    {
        if (IsVisible)
            await HideAnimatedAsync();

        _persistent = false;
        _sessionPinned = false;
        _bodyToggleEnabled = false;
        ApplyWindowLayer(false);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var localCts = _cts;
        var ct = localCts.Token;
        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsHudBusy = true;

        ApplyRenderData(data);
        ResetToInitial();
        ShowPositioned();

        Task? refreshTask = liveRefresh is null
            ? null
            : RefreshLiveDataAsync(liveRefresh, refreshCts.Token, persistent: false);

        try
        {
            if (data.SimpleAnimation)
            {
                SetSimpleCState();
                await Task.WhenAll(
                    HudAnimations.SimplePillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(NumHost, ct),
                    HudAnimations.SimpleScaleOut(_animOptions).RunAsync(ScaleHost, ct));
            }
            else
            {
                await Task.WhenAll(
                    HudAnimations.PillCorner(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillWidth(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.ScaleOut(_animOptions).RunAsync(ScaleHost, ct),
                    HudAnimations.BoltIcon(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.RippleHost(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.CircleForm(_animOptions).RunAsync(CircleForm, ct),
                    HudAnimations.SquareForm(_animOptions).RunAsync(SquareForm, ct),
                    HudAnimations.TitleHost(_animOptions).RunAsync(TitleHost, ct),
                    HudAnimations.NumHost(_animOptions).RunAsync(NumHost, ct),
                    HudAnimations.Ripple(_animOptions, 1.5, 0.50).RunAsync(RippleInner, ct),
                    HudAnimations.Ripple(_animOptions, 2.0, 0.50).RunAsync(RippleMid, ct),
                    HudAnimations.Ripple(_animOptions, 2.5, 0.60).RunAsync(RippleOuter, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleInnerHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleMidHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleOuterHost, ct));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            refreshCts.Cancel();
            if (refreshTask is not null)
            {
                try { await refreshTask; }
                catch (OperationCanceledException) { }
            }

            if (ReferenceEquals(_cts, localCts))
            {
                if (!ct.IsCancellationRequested && IsVisible)
                    Hide();
                _clockTimer.Stop();
                ClockHost.Opacity = 0;
                _bodyToggleEnabled = false;
                IsHudBusy = false;
            }
        }
    }

    private async Task RefreshLiveDataAsync(
        Func<CancellationToken, Task<HudRenderData>> refresh,
        CancellationToken ct,
        bool persistent)
    {
        long lastSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(50, ct).ConfigureAwait(false);
            long second = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
            if (second == lastSecond) continue;
            lastSecond = second;

            HudRenderData data;
            try
            {
                data = await refresh(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                continue;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested && IsVisible && _persistent == persistent)
                    ApplyRenderData(data);
            });
        }
    }

    public async Task ShowPersistentAsync(
        HudRenderData data,
        Func<CancellationToken, Task<HudRenderData>>? liveRefresh = null,
        bool sessionPinned = false,
        bool quick = false)
    {
        _persistent = true;
        _sessionPinned = sessionPinned;
        _bodyToggleEnabled = sessionPinned && !_settings.AlwaysVisible;
        ApplyWindowLayer(true);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var localCts = _cts;
        var ct = localCts.Token;
        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsHudBusy = true;

        ApplyRenderData(data);
        ResetToInitial();
        ShowPositioned();

        // Full animation reaches its final numeric state before the animation timeline itself
        // has finished. Keep sampling during that tail so clocks/CPU/GPU values do not appear
        // frozen for several seconds after the visible intro has completed.
        Task? refreshTask = liveRefresh is null
            ? null
            : RefreshLiveDataAsync(liveRefresh, refreshCts.Token, persistent: true);

        try
        {
            // 複用原動畫，只去掉最後的 ScaleOut，因此最終停留在原動畫 C 狀態。
            if (quick)
            {
                PreparePreviewInitialState();
                await Task.WhenAll(
                    HudAnimations.PreviewPillIn(SurfaceWidth,SurfaceHeight).RunAsync(Pill,ct),
                    HudAnimations.PreviewContentIn().RunAsync(BoltIcon,ct),
                    HudAnimations.PreviewContentIn().RunAsync(NumHost,ct),
                    HudAnimations.PreviewContentIn().RunAsync(ClockHost,ct));
            }
            else if (data.SimpleAnimation)
            {
                SetSimpleCState();
                await Task.WhenAll(
                    HudAnimations.SimplePillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(NumHost, ct));
            }
            else
            {
                await Task.WhenAll(
                    HudAnimations.PillCorner(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillWidth(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.BoltIcon(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.RippleHost(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.CircleForm(_animOptions).RunAsync(CircleForm, ct),
                    HudAnimations.SquareForm(_animOptions).RunAsync(SquareForm, ct),
                    HudAnimations.TitleHost(_animOptions).RunAsync(TitleHost, ct),
                    HudAnimations.NumHost(_animOptions).RunAsync(NumHost, ct),
                    HudAnimations.Ripple(_animOptions, 1.5, 0.50).RunAsync(RippleInner, ct),
                    HudAnimations.Ripple(_animOptions, 2.0, 0.50).RunAsync(RippleMid, ct),
                    HudAnimations.Ripple(_animOptions, 2.5, 0.60).RunAsync(RippleOuter, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleInnerHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleMidHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleOuterHost, ct));
            }

            SetPersistentFinalState();
            _bodyToggleEnabled = sessionPinned && !_settings.AlwaysVisible;
            EnsureInputHitTest();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            refreshCts.Cancel();
            if (refreshTask is not null)
            {
                try { await refreshTask; }
                catch (OperationCanceledException) { }
            }

            if (ReferenceEquals(_cts, localCts))
                IsHudBusy = false;
        }
    }

    public void UpdatePersistent(HudRenderData data)
    {
        if (!_persistent) return;

        ApplyRenderData(data);
        ApplyWindowLayer(true);
        SetPersistentFinalState();

        if (!IsVisible)
            ShowPositioned();
    }

    public void UpdateVisible(HudRenderData data)
    {
        if(IsVisible)ApplyRenderData(data);
    }

    public void HidePersistent() => _ = HidePersistentAsync();

    public async Task HidePersistentAsync() => await HideAnimatedAsync();

    public async Task HideAnimatedAsync()
    {
        _cts?.Cancel();
        _persistent = false;
        _sessionPinned = false;
        _bodyToggleEnabled = false;
        EnsureInputHitTest();

        if (!IsVisible)
        {
            IsHudBusy = false;
            return;
        }

        IsHudBusy = true;
        try
        {
            // A settings/profile switch always exits from the final C state, so the transition
            // is deterministic: old HUD retracts completely before the next HUD is shown.
            SetPersistentFinalState();
            await HudAnimations.CloseFromC().RunAsync(ScaleHost);
        }
        catch { }
        finally
        {
            if (IsVisible) Hide();
            _clockTimer.Stop();
            ClockHost.Opacity = 0;
            IsHudBusy = false;
        }
    }

    private void ApplyRenderData(HudRenderData data)
    {
        var previous = _lastRenderData;
        bool overview=data.Metrics is {Count:4};
        _overviewActive=overview;
        _animOptions=_animOptions with{SurfaceWidth=SurfaceWidth,SurfaceHeight=SurfaceHeight};
        NumHost.Width=ClockHost.Width=SurfaceWidth;NumHost.Height=overview?SurfaceHeight:double.NaN;ClockHost.Height=SurfaceHeight;
        OverviewIdentity.IsVisible=OverviewHeading.IsVisible=OverviewEdge.IsVisible=false;
        Pill.BorderThickness=new Thickness(0);
        Pill.BorderBrush=null;
        OverviewHost.IsVisible=overview;StandardPrimary.IsVisible=StandardRight.IsVisible=!overview;
        Badge.IsVisible=CircleForm.IsVisible=SquareForm.IsVisible=true;
        OverviewClockDivider.IsVisible=false;
        if(overview)UpdateOverview(data.Metrics!);
        else
        {
            ClockText.Margin=new Thickness(0,5,70,0);DateText.Margin=new Thickness(0,0,70,5); ClockText.Width=DateText.Width=double.NaN;ClockText.TextAlignment=DateText.TextAlignment=TextAlignment.Left;
            ClockText.FontSize=10;DateText.FontSize=8;ClockText.Opacity=.82;DateText.Opacity=.62;
            DateText.VerticalAlignment=Avalonia.Layout.VerticalAlignment.Bottom;
        }

        if (previous?.Tagline != data.Tagline) TagLineText.Text = overview?"ENDFIELD / TELEMETRY":data.Tagline;
        if (previous?.Title != data.Title) TitleText.Text = overview?"PERFORMANCE":data.Title;
        if (previous?.PrimaryText != data.PrimaryText) PrimaryText.Text = data.PrimaryText;
        if (previous?.SecondaryText != data.SecondaryText) SecondaryText.Text = data.SecondaryText;
        if (previous?.RightText != data.RightText) RightText.Text = data.RightText;
        if (previous?.RightSuffix != data.RightSuffix) RightSuffixText.Text = data.RightSuffix;

        if (previous?.LeftIcon != data.LeftIcon || previous?.Metrics is null != (data.Metrics is null))
        {
            var geometry = overview?Geometry.Parse("M13 2 L4 13 L12 13 L18 2 Z M13 11 L20 11 L13 22 L4 22 Z"):IconCatalog.GetGeometry(data.LeftIcon);
            CircleGlyph.Data = geometry;
            SquareGlyph.Data = geometry;
        }

        bool useOriginalLaptop = string.Equals(data.RightIcon, "battery", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(data.RightIcon, "laptop", StringComparison.OrdinalIgnoreCase);

        if (previous?.RightIcon != data.RightIcon || previous?.Metrics is null != (data.Metrics is null))
        {
            BadgeLaptop.IsVisible = useOriginalLaptop;
            BadgeElectrode.IsVisible = useOriginalLaptop;
            BadgeGlyph.IsVisible = !useOriginalLaptop;
            if (!useOriginalLaptop)
                BadgeGlyph.Data = overview ? Geometry.Parse("M5 5 H19 V19 H5 Z M9 9 H15 V15 H9 Z M8 1 V5 M12 1 V5 M16 1 V5 M8 19 V23 M12 19 V23 M16 19 V23 M1 8 H5 M1 12 H5 M1 16 H5 M19 8 H23 M19 12 H23 M19 16 H23") : IconCatalog.GetGeometry(data.RightIcon);
        }

        if (previous?.AccentColor != data.AccentColor || previous?.Metrics is null != (data.Metrics is null))
        {
            var accent = TryColor(data.AccentColor, Color.Parse("#C6CA4C"));
            var accentBrush = new SolidColorBrush(accent);
            BadgeArc.Stroke = accentBrush;
            LaptopScreen.BorderBrush = accentBrush;
            LaptopBase.Background = accentBrush;
            BadgeElectrode.Background = accentBrush;
            BadgeGlyph.Fill = overview ? null : accentBrush; BadgeGlyph.Stroke = overview ? accentBrush : null; BadgeGlyph.StrokeThickness = overview ? 1.5 : 0;
        }

        double badgeProgress=overview?(data.Metrics![1].Usage??0)/100:data.Progress;
        double previousBadgeProgress=previous?.Metrics is {Count:4} oldMetrics?(oldMetrics[1].Usage??0)/100:previous?.Progress??-1;
        if (previous is null || Math.Abs(previousBadgeProgress-badgeProgress)>0.0005)
            SetProgressTarget(badgeProgress, animate: previous is not null && IsVisible);

        _lastRenderData = data;
    }

    private readonly List<(TextBlock Label,TextBlock Value,TextBlock Unit,TextBlock Detail,Border Track,Border Fill,ScaleTransform Scale)> _overviewCells=new();
    private static readonly IBrush OverviewText=Brushes.White;
    private static readonly IBrush OverviewMuted=Brush.Parse("#8D8B8C");
    private static readonly IBrush OverviewAccent=Brush.Parse("#C6CA4C");
    private static readonly IBrush OverviewWarning=Brush.Parse("#EDB65E");
    private static readonly IBrush OverviewCritical=Brush.Parse("#FF715F");
    private static readonly string[] OverviewLabelsEnglish={"CPU","GPU","RAM","VRAM"};
    private void UpdateOverview(IReadOnlyList<HudMetric> metrics)
    {
        if(_overviewCells.Count==0)
        {
            for(int index=0;index<4;index++)
            {
                // Fixed columns and tabular figures keep telemetry changes from moving their neighbours.
                var panel=new Grid{RowDefinitions=new RowDefinitions("14,21,11"),Margin=new Thickness(0,0,10,0)};
                var label=new TextBlock{FontSize=10,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,FontFamily=new FontFamily("Inter, Segoe UI"),FontWeight=FontWeight.Normal,Foreground=Brush.Parse("#A29FA1")};
                var numberRow=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=3};
                var value=new TextBlock{FontSize=22,FontWeight=FontWeight.Medium,Foreground=OverviewText,FontFamily=new FontFamily("Inter, Segoe UI"),VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center};
                var unit=new TextBlock{FontSize=11,FontFamily=new FontFamily("Inter, Segoe UI"),Foreground=Brushes.White,Opacity=.55,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,4)};
                numberRow.Children.Add(value);numberRow.Children.Add(unit);
                var detail=new TextBlock{FontSize=9,FontFamily=new FontFamily("Inter, Segoe UI"),Foreground=Brushes.White,Opacity=.55,TextTrimming=TextTrimming.CharacterEllipsis};
                var scale=new ScaleTransform(0,1);
                var fill=new Border{Background=OverviewAccent,RenderTransform=scale,RenderTransformOrigin=new RelativePoint(0,0,RelativeUnit.Relative)};
                var track=new Border{Height=2,IsVisible=false,Background=Brush.Parse("#4D514B"),CornerRadius=new CornerRadius(1),ClipToBounds=true,Child=fill};
                Grid.SetRow(numberRow,1);Grid.SetRow(detail,2);
                panel.Children.Add(label);panel.Children.Add(numberRow);panel.Children.Add(detail);
                var cell=new Grid();cell.Children.Add(panel);
                Grid.SetColumn(cell,index);OverviewHost.Children.Add(cell);_overviewCells.Add((label,value,unit,detail,track,fill,scale));
            }
        }
        for(int i=0;i<4;i++)
        {
            var cell=_overviewCells[i];var metric=metrics[i<2?i:i==2?3:2];
            cell.Label.Text=OverviewLabelsEnglish[i];
            string reading=i==2?metric.Detail.Split('/')[0]+(metric.Detail.EndsWith(" GB",StringComparison.Ordinal)?" GB":""):metric.Value;
            cell.Detail.Text=i==2?metric.Detail.Contains('/')?"/ "+metric.Detail.Split('/')[1]:metric.Detail:i==3?metric.Detail:metric.Detail;
            bool percent=reading.EndsWith('%');bool gigabytes=reading.EndsWith(" GB",StringComparison.Ordinal);
            cell.Value.Text=percent?reading[..^1]:gigabytes?reading[..^3]:reading;
            cell.Unit.Text=percent?"%":gigabytes?"GB":"";
            cell.Value.FontSize=22;
            cell.Scale.ScaleX=Math.Clamp((metric.Usage??0)/100,0,1);cell.Track.Opacity=metric.Usage is null?.25:1;
            cell.Fill.Background=metric.Usage>=90?OverviewCritical:metric.Usage>=75?OverviewWarning:OverviewAccent;
        }
        // Clock remains in its dedicated band rather than crossing the metric labels.
        OverviewHost.Margin=new Thickness(64,0,_settings.ShowClock?136:88,0); ClockText.Margin=new Thickness(0,17,58,0);DateText.Margin=new Thickness(0,0,58,15); ClockText.Width=66;DateText.Width=66; ClockText.TextAlignment=TextAlignment.Center;DateText.TextAlignment=TextAlignment.Center;
        DateText.VerticalAlignment=Avalonia.Layout.VerticalAlignment.Bottom;
        ClockText.FontSize=8;DateText.FontSize=7;ClockText.Opacity=.55;DateText.Opacity=.4;
    }

    private void SetProgressTarget(double value, bool animate)
    {
        double target = Math.Clamp(value, 0d, 1d);

        if (!_progressInitialized || !animate)
        {
            _progressTimer.Stop();
            _progressInitialized = true;
            _displayedProgress = target;
            _progressFrom = target;
            _progressTo = target;
            BadgeArc.Data = BuildRingGeometry(target, 46d, 4.5d);
            return;
        }

        if (Math.Abs(_displayedProgress - target) < 0.0005d)
        {
            _progressTimer.Stop();
            _displayedProgress = target;
            _progressFrom = target;
            _progressTo = target;
            BadgeArc.Data = BuildRingGeometry(target, 46d, 4.5d);
            return;
        }

        // If another sample arrives while the ring is already moving, continue from the
        // currently displayed value instead of snapping back to the previous target.
        _progressFrom = _displayedProgress;
        _progressTo = target;
        _progressStartedUtc = DateTime.UtcNow;
        _progressTimer.Start();
    }

    private void TickProgressAnimation()
    {
        if (!_progressInitialized)
        {
            _progressTimer.Stop();
            return;
        }

        double durationMs = Math.Max(1d, ProgressTransitionDuration.TotalMilliseconds);
        double t = Math.Clamp((DateTime.UtcNow - _progressStartedUtc).TotalMilliseconds / durationMs, 0d, 1d);
        // Cubic ease-out: quick response at the start, gentle settle at the target.
        double eased = 1d - Math.Pow(1d - t, 3d);
        _displayedProgress = _progressFrom + (_progressTo - _progressFrom) * eased;
        BadgeArc.Data = BuildRingGeometry(_displayedProgress, 46d, 4.5d);

        if (t >= 1d)
        {
            _displayedProgress = _progressTo;
            BadgeArc.Data = BuildRingGeometry(_displayedProgress, 46d, 4.5d);
            _progressTimer.Stop();
        }
    }

    private static Color TryColor(string? value, Color fallback)
    {
        try { return Color.Parse(string.IsNullOrWhiteSpace(value) ? "#C6CA4C" : value); }
        catch { return fallback; }
    }

    private async Task DismissAsync()
    {
        if (_persistent) return;
        await HideAnimatedAsync();
    }

    private static Geometry BuildRingGeometry(double fraction, double diameter, double thickness)
    {
        double radius = (diameter - thickness) / 2d;
        var center = new Point(diameter / 2d, diameter / 2d);
        double sweep = 360d * Math.Clamp(fraction, 0d, 1d);
        if (sweep < 0.5d) sweep = 0.5d;
        if (sweep > 359.5d) sweep = 359.5d;
        const double startAngle = -90d;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweep);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments = new PathSegments
        {
            new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                RotationAngle = 0d,
                IsLargeArc = sweep > 180d,
                SweepDirection = SweepDirection.Clockwise,
            },
        };
        return new PathGeometry { Figures = new PathFigures { figure } };
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        double rad = degrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    private void ResetToInitial()
    {
        Root.Opacity = 1;
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);
        Pill.Width = SurfaceWidth;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 0;
        Pill.RenderTransform = new ScaleTransform(0.6d, 0.6d);

        RippleHost.RenderTransform = new TranslateTransform(0d, 0d);
        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(0.4d, 0.4d), new TranslateTransform(0d, 0d) },
        };
        BoltIcon.Opacity = 0;

        RippleInnerHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleMidHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleOuterHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleInner.RenderTransform = new ScaleTransform(0d, 0d);
        RippleInner.Opacity = 0;
        RippleMid.RenderTransform = new ScaleTransform(0d, 0d);
        RippleMid.Opacity = 0;
        RippleOuter.RenderTransform = new ScaleTransform(0d, 0d);
        RippleOuter.Opacity = 0;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 0;
        TitleHost.RenderTransform = new TranslateTransform(0d, 0d);
        TitleHost.Opacity = 0;
        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 0;
        ClockHost.Opacity = 0;
    }

    private void PreparePreviewInitialState()
    {
        Root.Opacity = 1;
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);
        Pill.Width = SurfaceWidth;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 0;
        Pill.RenderTransform = new ScaleTransform(0.78d, 0.94d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 0;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;
        RippleHost.Height = 60;
        RippleHost.RenderTransform = new TranslateTransform(-245d, 0d);
        RippleInner.Opacity = 0;
        RippleMid.Opacity = 0;
        RippleOuter.Opacity = 0;
        TitleHost.Opacity = 0;
        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 0;
        ClockHost.Opacity = 0;
        UpdateClockText();
    }

    private void SetSimpleCState()
    {
        Pill.Width = SurfaceWidth;
        Pill.Height = SurfaceHeight;
        Pill.CornerRadius = new CornerRadius(SurfaceHeight/2);
        Pill.Opacity = 0;
        Pill.RenderTransform = new ScaleTransform(0.6d, 0.6d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 0;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;
        TitleHost.Opacity = 0;
        RippleHost.Height = 60;
        RippleHost.RenderTransform = new TranslateTransform(0d, 0d);
        RippleInnerHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleMidHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleOuterHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleInner.Opacity = 0;
        RippleMid.Opacity = 0;
        RippleOuter.Opacity = 0;
        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 0;
    }

    private void SetPersistentFinalState()
    {
        Root.Opacity = 1;
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);

        Pill.Width = SurfaceWidth;
        Pill.Height = SurfaceHeight;
        Pill.CornerRadius = new CornerRadius(SurfaceHeight/2);
        Pill.Opacity = 1;
        Pill.RenderTransform = new ScaleTransform(1d, 1d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 1;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;

        RippleHost.Height = 60;
        RippleHost.RenderTransform = new TranslateTransform(-245d, 0d);
        RippleInner.Opacity = 0;
        RippleMid.Opacity = 0;
        RippleOuter.Opacity = 0;
        TitleHost.Opacity = 0;
        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 1;
        UpdateClockText();
        ClockHost.Opacity = _settings.ShowClock ? 1d : 0d;
    }

    private void UpdateClockText()
    {
        ClockHost.IsVisible = _settings.ShowClock;
        if (!_settings.ShowClock)
        {
            ClockHost.Opacity = 0;
            return;
        }

        var culture = LocalizationManager.IsEnglish
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("zh-TW");
        var now = DateTime.Now;
        ClockText.Text = _settings.Use24HourClock
            ? now.ToString("HH:mm", culture)
            : now.ToString("hh:mm tt", culture);
        DateText.Text = now.ToString("yyyy/MM/dd", culture);
        DateText.IsVisible = _settings.ShowDate;
    }

    private void PositionHud()
    {
        var screen = ResolveScreen(_settings.MonitorIndex);
        if (screen is null) return;

        var area = screen.WorkingArea;
        double scaling = screen.Scaling > 0 ? screen.Scaling : 1d;

        // 視窗本身比 560px HUD 大，是為了保留原動畫波紋空間。
        // 定位時按“可見 HUD”而不是透明視窗外框計算，所以座標更符合使用者直覺。
        double windowWidthPx = Width * scaling;
        double windowHeightPx = Height * scaling;
        double hudWidthPx = SurfaceWidth * _settings.GlobalScale * scaling;
        double hudHeightPx = SurfaceHeight * _settings.GlobalScale * scaling;
        double paddingX = Math.Max(0d, (windowWidthPx - hudWidthPx) / 2d);
        double paddingY = Math.Max(0d, (windowHeightPx - hudHeightPx) / 2d);
        const double margin = 16d;

        double hudLeft;
        double hudTop;

        if (_settings.PositionMode == HudPositionMode.CustomCoordinates)
        {
            hudLeft = area.X + _settings.HudCustomX;
            hudTop = area.Y + _settings.HudCustomY;
        }
        else
        {
            hudLeft = _settings.HudPosition switch
            {
                HudPosition.TopLeft or HudPosition.CenterLeft or HudPosition.BottomLeft => area.X + margin,
                HudPosition.TopRight or HudPosition.CenterRight or HudPosition.BottomRight => area.X + area.Width - hudWidthPx - margin,
                _ => area.X + (area.Width - hudWidthPx) / 2d,
            };

            hudTop = _settings.HudPosition switch
            {
                HudPosition.TopLeft or HudPosition.TopCenter or HudPosition.TopRight => area.Y + margin,
                HudPosition.BottomLeft or HudPosition.BottomCenter or HudPosition.BottomRight => area.Y + area.Height - hudHeightPx - margin,
                _ => area.Y + (area.Height - hudHeightPx) / 2d,
            };

            hudLeft += _settings.HudOffsetX;
            hudTop += _settings.HudOffsetY;
        }

        int windowX = (int)Math.Round(hudLeft - paddingX);
        int windowY = (int)Math.Round(hudTop - paddingY);
        Position = new PixelPoint(windowX, windowY);
    }

    private Avalonia.Platform.Screen? ResolveScreen(int monitorIndex)
    {
        var screens = Screens.All;
        var primary = Screens.Primary;
        if (monitorIndex < 0)
            return primary ?? screens.FirstOrDefault();
        if (monitorIndex < screens.Count)
            return screens[monitorIndex];
        return primary ?? screens.FirstOrDefault();
    }

    private void ShowPositioned()
    {
        PositionHud();
        UpdateClockText();
        if (_settings.ShowClock)
            _clockTimer.Start();
        else
            _clockTimer.Stop();

        if (!IsVisible)
            Show();

        EnsureInputHitTest();
        PositionHud();
        Dispatcher.UIThread.Post(() =>
        {
            PositionHud();
            EnsureInputHitTest();
        }, DispatcherPriority.Loaded);
    }

    public bool IsPointInTopCenterHotZone(PixelPoint screenPoint, int width = 240, int height = 5)
    {
        var screen = ResolveScreen(_settings.MonitorIndex);
        if (screen is null) return false;

        var bounds = screen.Bounds;
        int centerX = bounds.X + bounds.Width / 2;
        int halfWidth = Math.Max(40, width / 2);
        int hotHeight = Math.Max(2, height);

        return screenPoint.X >= centerX - halfWidth
            && screenPoint.X <= centerX + halfWidth
            && screenPoint.Y >= bounds.Y
            && screenPoint.Y <= bounds.Y + hotHeight;
    }

    private void EnsureInputHitTest()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Avalonia may refresh native HWND extended styles after Show(), Topmost changes
        // or other window-state transitions. Re-apply click-through every time the HUD
        // is presented instead of assuming a one-time style write remains forever.
        var handle = this.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;

        if (_hitTest is not null && _hitTest.Handle != handle.Handle)
        {
            _hitTest.Dispose();
            _hitTest = null;
        }

        if (_hitTest is null)
            _hitTest = WindowsHudHitTest.TryAttach(
                handle.Handle,
                IsPointInsideClickableHud,
                () =>
                {
                    if (_bodyToggleEnabled)
                        PinToggleRequested?.Invoke();
                });

        _hitTest?.Reapply();
    }

    private bool IsPointInsideClickableHud(PixelPoint screenPoint)
        => _bodyToggleEnabled && IsPointInsideVisibleHud(screenPoint);

    private bool IsPointInsideVisibleHud(PixelPoint screenPoint)
    {
        var screen = ResolveScreen(_settings.MonitorIndex);
        if (screen is null) return true;

        double scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
        double windowWidthPx = Width * scaling;
        double windowHeightPx = Height * scaling;
        double hudWidthPx = SurfaceWidth * _settings.GlobalScale * scaling;
        double currentPillHeight = double.IsNaN(Pill.Height) ? 60d : Math.Max(60d, Pill.Height);
        double hudHeightPx = currentPillHeight * _settings.GlobalScale * scaling;
        double left = Position.X + (windowWidthPx - hudWidthPx) / 2d;
        double top = Position.Y + (windowHeightPx - hudHeightPx) / 2d;
        if (_overviewActive)
        {
            double x = screenPoint.X - left, y = screenPoint.Y - top;
            if (x < 0 || y < 0 || x > hudWidthPx || y > hudHeightPx)
                return false;
            double radius = hudHeightPx / 2d;
            double centerX = Math.Clamp(x, radius, hudWidthPx - radius);
            return Math.Pow(x - centerX, 2) + Math.Pow(y - radius, 2) <= radius * radius;
        }
        const double tolerance = 4d;

        return screenPoint.X >= left - tolerance
            && screenPoint.X <= left + hudWidthPx + tolerance
            && screenPoint.Y >= top - tolerance
            && screenPoint.Y <= top + hudHeightPx + tolerance;
    }
}
