using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Music;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Animations;
using SkiaSharp;

namespace EndfieldChargePlus.Views;

public partial class MusicIslandWindow : Window
{
    private readonly IMusicSession _session;
    private readonly PlaylistStore _playlists = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _visibleRequests;
    private MusicSnapshot _snapshot = MusicSnapshot.Empty;
    private WindowsHudHitTest? _hitTest;
    private Bitmap? _bitmap;
    private byte[]? _art;
    private bool _reading, _commanding, _seeking, _disposed;
    private CancellationTokenSource? _intro;
    private double _displayScale=1;
    private double _renderedProgress=-1;
    private readonly PaintedIslandRegion _paintedRegion = new();
    public event Action? IslandHidden;
    public event Action? SettingsRequested;

    public MusicIslandWindow() : this(new YouTubePlaylistSession()) { }
    public MusicIslandWindow(IMusicSession session)
    {
        _session = session; InitializeComponent();
        Transport.Children.Remove(Shuffle);Transport.Children.Remove(PlayPause);
        Transport.Children.Insert(2,Shuffle);Transport.Children.Add(PlayPause);
        Notice.PropertyChanged+=(_,e)=>{if(e.Property==TextBlock.TextProperty)ToolTip.SetTip(Island,Notice.Text);};
        _timer.Tick += async (_, _) => await RefreshAsync();
        Previous.Click += async (_, _) => await CommandAsync(MusicCommand.Previous);
        Next.Click += async (_, _) => await CommandAsync(MusicCommand.Next);
        Shuffle.Click += async (_, _) => await CommandAsync(MusicCommand.Shuffle, _snapshot.Shuffle ? 0 : 1);
        Repeat.Click += async (_, _) => await CommandAsync(MusicCommand.Repeat, (_snapshot.Repeat + 1) % 3);
        PlayPause.Click += async (_, _) => await CommandAsync(_snapshot.Playing ? MusicCommand.Pause : MusicCommand.Play);
        Header.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) HideIsland(); };
        var context=new ContextMenu();
        var start=new MenuItem{Header=LocalizationManager.TranslateLiteral("播放清單模式")};start.Click+=(_,_)=>OpenSavedPlaylist();
        var follow=new MenuItem{Header=LocalizationManager.TranslateLiteral("跟隨瀏覽器模式")};follow.Click+=(_,_)=>FollowBrowser();
        var original=new MenuItem{Header=LocalizationManager.TranslateLiteral("在瀏覽器開啟清單並跟隨")};original.Click+=(_,_)=>{try{_playlists.Open();FollowBrowser();}catch(Exception ex){Notice.Text=ex.Message;}};
        var settingsItem=new MenuItem{Header=LocalizationManager.TranslateLiteral("設定播放清單")};settingsItem.Click+=(_,_)=>SettingsRequested?.Invoke();
        var hide=new MenuItem{Header=LocalizationManager.TranslateLiteral("收起音樂島")};hide.Click+=(_,_)=>HideIsland();
        context.ItemsSource=new object[]{start,follow,settingsItem,original,new Separator(),hide};Island.ContextMenu=context;
        ApplyLanguage();LocalizationManager.LanguageChanged+=ApplyLanguage;
        if(_session is YouTubePlaylistSession routed)routed.ModeChanged+=OnModeChanged;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; HideIsland(); } };
        SettingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        OpenPlaylist.Click += (_, _) => OpenSavedPlaylist();
        Progress.AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(Progress).Properties.IsLeftButtonPressed) _seeking = true; }, RoutingStrategies.Tunnel);
        Progress.AddHandler(PointerReleasedEvent, async (_, _) => { if (!_seeking) return; _seeking = false; await CommandAsync(MusicCommand.Seek, Progress.Value); }, RoutingStrategies.Bubble, handledEventsToo: true);
        Progress.KeyUp += async (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Home or Key.End) await CommandAsync(MusicCommand.Seek, Progress.Value); };
        Progress.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty||e.Property==Slider.MaximumProperty)UpdateProgressFill();};
        LayoutUpdated += (_, _) => {UpdateRegion();UpdateProgressFill();};
        Closed += (_, _) => { _disposed = true;_intro?.Cancel(); _intro?.Dispose();LocalizationManager.LanguageChanged-=ApplyLanguage; _timer.Stop(); _visibleRequests?.Cancel(); _hitTest?.Dispose(); _session.Dispose(); _bitmap?.Dispose(); };
        ApplySnapshot(MusicSnapshot.Empty);
    }
    private void ApplyLanguage()
    {
        // Metadata belongs to the music source; translate controls and generated placeholders only.
        var title=SongTitle.Text;var artist=Artist.Text;
        LocalizationManager.ApplyStaticText(this);if(Island.ContextMenu is {}menu)LocalizationManager.ApplyStaticText(menu);
        if(_snapshot.Available){SongTitle.Text=title;Artist.Text=artist;}
    }
    public void OpenSavedPlaylist()
    {
        try
        {
            if (_session is YouTubePlaylistSession player)
            { player.OpenBackgroundPlaylist(_playlists.Load()); }
            else if(_session is BackgroundYouTubeSession background)background.Open(_playlists.Load());
            else _playlists.Open();
            Notice.Text = LocalizationManager.TranslateLiteral("播放清單模式 · 背景播放器正在載入指定清單");
            _ = RefreshAsync();
        }
        catch (Exception ex) { Notice.Text = LocalizationManager.TranslateLiteral("無法開啟清單：") + ex.Message; }
    }
    public void FollowBrowser()
    {
        if(_session is YouTubePlaylistSession player){player.FollowBrowser();_ = RefreshAsync();}
    }
    private void OnModeChanged()
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(OnModeChanged);return;}
        if(_disposed)return;
        _visibleRequests?.Cancel();_visibleRequests?.Dispose();_visibleRequests=IsVisible?new CancellationTokenSource():null;
        _seeking=false;
        ApplySnapshot(MusicSnapshot.Empty with{Title=LocalizationManager.TranslateLiteral("正在切換音樂來源"),Artist="",Notice=LocalizationManager.TranslateLiteral("等待目前模式的播放資訊")});
    }
    public void ShowMusic(AppSettings settings)
    {
        bool entering=!IsVisible;
        _displayScale=Math.Clamp(settings.GlobalScale,.4,1.4);
        MusicScale.LayoutTransform=new ScaleTransform(_displayScale,_displayScale);
        Height=106*_displayScale;
        var screen = settings.MonitorIndex >= 0 && settings.MonitorIndex < Screens.All.Count ? Screens.All[settings.MonitorIndex] : Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            Width = Math.Min(576*_displayScale, screen.WorkingArea.Width / scale - 32);
            Position = new PixelPoint(screen.WorkingArea.X + (int)Math.Round((screen.WorkingArea.Width - Width * scale) / 2), screen.WorkingArea.Y + (int)Math.Round(8 * scale));
        }
        ShowActivated = false;
        if (!IsVisible)
        {
            _visibleRequests?.Dispose(); _visibleRequests = new CancellationTokenSource(); Show();
        }
        ApplyCompactLayout(Width/_displayScale);
        var handle = this.TryGetPlatformHandle();
        if (handle is not null) _hitTest ??= WindowsHudHitTest.TryAttach(handle.Handle, p => { var local = this.PointToClient(p); var origin=Island.TranslatePoint(default,this)??default; return IsVisible && IslandGeometry.Contains(local.X-origin.X,local.Y-origin.Y,Island.Bounds.Width*_displayScale,Island.Bounds.Height*_displayScale,Island.Bounds.Height*_displayScale/2); }, () => { }, nativeControls: true);
        _hitTest?.Reapply(); UpdateRegion(); _timer.Start(); _ = RefreshAsync();
        if(entering)_=PlayOriginalIntroAsync(settings);
    }
    public void HideIsland()
    {
        if (!IsVisible) return;
        _intro?.Cancel();IslandTransition.Cancel(this); _timer.Stop(); _visibleRequests?.Cancel(); _seeking = false; Hide(); Opacity = 1; IslandHidden?.Invoke();
    }
    private void UpdateProgressFill()
    {
        var fraction=Math.Clamp(Progress.Value/Math.Max(1,Progress.Maximum),0,1);
        ProgressFill.Width=Math.Max(0,Progress.Bounds.Width)*fraction;
        if(Progress.Maximum<=1){PlaybackArc.IsVisible=false;return;}
        PlaybackArc.IsVisible=true;
        if(Math.Abs(_renderedProgress-fraction)<.00001)return;
        _renderedProgress=fraction;
        PlaybackArc.Data=OriginalCapsuleIntro.Ring(fraction);
    }
    private void UpdateRegion()
        => _paintedRegion.Update(this, Island, _hitTest);
    public void ApplyCompactLayout(double width)
    {
        bool compact=width<500;SettingsButton.IsVisible=false;
        // Windows rounds client sizes to physical pixels. Keep canonical art dimensions when
        // only transparent padding is affected by that rounding.
        MusicRoot.Width=width>=572?576:width;Island.Width=MusicRoot.Width-16;
        ContentGrid.Margin=new Thickness(19,7,5,7);
        OpenPlaylist.Width=OpenPlaylist.Height=32;
        Header.Margin=compact?new Thickness(10,0,8,0):new Thickness(14,0,18,0);
        SongTitle.FontSize=compact?14:17;Artist.FontSize=10;
        Transport.Spacing=compact?2:8;
        foreach(var button in new[]{Shuffle,Previous,Next,Repeat}){button.Width=compact?24:26;button.Height=46;}
        PlayPause.Width=PlayPause.Height=PlayRing.Width=PlayRing.Height=46;
        Notice.IsVisible=false;
    }

    private async Task PlayOriginalIntroAsync(AppSettings settings)
    {
        _intro?.Cancel();_intro?.Dispose();var source=_intro=new CancellationTokenSource();
        var o=AnimationOptions.FromSettings(settings) with{DurationSeconds=3,SurfaceWidth=Island.Width,SurfaceHeight=60};
        Island.Height=60;Island.CornerRadius=new CornerRadius(30);Island.Opacity=1;
        ContentGrid.Opacity=0;OpenPlaylist.IsHitTestVisible=Header.IsHitTestVisible=Transport.IsHitTestVisible=false;
        try
        {
            await OriginalCapsuleIntro.RunAsync(this,Island,ContentGrid,"Music",o,source.Token);
        }
        catch(OperationCanceledException){}
        finally
        {
            if(ReferenceEquals(_intro,source)){
                OriginalCapsuleIntro.Finish(this,Island,ContentGrid,"Music");
                ContentGrid.Opacity=1;OpenPlaylist.IsHitTestVisible=Header.IsHitTestVisible=Transport.IsHitTestVisible=true;UpdateRegion();
            }
        }
    }
    private async Task RefreshAsync()
    {
        if (_reading || _commanding || _disposed || !IsVisible) return;
        _reading = true; var generation = _visibleRequests;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(generation!.Token); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try { var snapshot = await _session.ReadAsync(timeout.Token); if (!_disposed && IsVisible && ReferenceEquals(generation, _visibleRequests)) ApplySnapshot(snapshot); }
        catch (OperationCanceledException) { }
        catch { if (!_disposed && IsVisible) { ApplySnapshot(MusicSnapshot.Empty); Notice.Text = LocalizationManager.TranslateLiteral("Windows 媒體資訊暫時無法讀取；可從瀏覽器控制播放"); } }
        finally { _reading = false; }
    }
    private async Task CommandAsync(MusicCommand command, double position = 0)
    {
        if (_commanding || !IsVisible || _visibleRequests is null) return;
        _commanding = true; var generation=_visibleRequests;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_visibleRequests.Token); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try { var ok=await _session.SendAsync(command, position, timeout.Token);if(ReferenceEquals(generation,_visibleRequests))Notice.Text = ok ? LocalizationManager.TranslateLiteral("已送出播放控制") : LocalizationManager.TranslateLiteral("此模式未支援此控制；請從播放器操作"); }
        catch (OperationCanceledException) { }
        catch { if(ReferenceEquals(generation,_visibleRequests))Notice.Text = LocalizationManager.TranslateLiteral("播放控制失敗；請從播放器重試"); }
        finally { _commanding = false; await RefreshAsync(); }
    }
    private void ApplySnapshot(MusicSnapshot s)
    {
        _snapshot = s; SongTitle.Text = s.Available?s.Title:LocalizationManager.TranslateLiteral(s.Title); Artist.Text = (_session is YouTubePlaylistSession active ? (active.Mode==MusicMode.Playlist ? LocalizationManager.Text("清單 · ","Playlist · ") : LocalizationManager.Text("網頁 · ","Browser · ")) : "") + (!s.Available&&!string.IsNullOrWhiteSpace(s.Notice)?LocalizationManager.TranslateLiteral(s.Notice):s.Artist);
        ToolTip.SetTip(SongTitle, s.Title); ToolTip.SetTip(Artist, s.Artist);
        Previous.IsEnabled = s.CanPrevious; Next.IsEnabled = s.CanNext;
        Shuffle.IsEnabled = s.CanShuffle; Repeat.IsEnabled = s.CanRepeat;
        Shuffle.Foreground = Brush.Parse(s.Shuffle ? "#C6CA4C" : "#8D8B8C");
        Repeat.Foreground = Brush.Parse(s.Repeat != 0 ? "#C6CA4C" : "#8D8B8C");
        RepeatSingle.IsVisible=s.Repeat==2;
        ToolTip.SetTip(Shuffle, s.CanShuffle ? (s.Shuffle ? LocalizationManager.TranslateLiteral("隨機播放：開") : LocalizationManager.TranslateLiteral("隨機播放：關")) : LocalizationManager.TranslateLiteral("此瀏覽器未提供隨機控制；請在 YouTube 原頁切換"));
        ToolTip.SetTip(Repeat, s.CanRepeat ? LocalizationManager.Text("重播：","Repeat: ") + (LocalizationManager.IsEnglish?new[]{"Off","Playlist","Track"}:new[]{"關","整份清單","單曲"})[Math.Clamp(s.Repeat,0,2)] : LocalizationManager.TranslateLiteral("此瀏覽器未提供重播控制；請在 YouTube 原頁切換"));
        if (s.Notice is not null) Notice.Text = s.Notice;
        PlayPause.IsEnabled = s.Playing ? s.CanPause : s.CanPlay;
        PlayGlyph.Data = Geometry.Parse(s.Playing ? "M 2,0 L 7,0 L 7,18 L 2,18 Z M 12,0 L 17,0 L 17,18 L 12,18 Z" : "M 2,0 L 18,9 L 2,18 Z");
        Progress.IsEnabled = s.CanSeek; Progress.Maximum = Math.Max(1, s.Duration);
        if (!_seeking) Progress.Value = s.Position;
        Elapsed.Text = s.Duration > 0 ? MusicSnapshot.Time(s.Position) : "--:--";
        Remaining.Text = s.Duration > 0 ? "−"+MusicSnapshot.Time(Math.Max(0,s.Duration-s.Position)) : "--:--";
        if (!ReferenceEquals(_art, s.Artwork))
        {
            Cover.Source = null; _bitmap?.Dispose(); _bitmap = null; _art = s.Artwork;
            if (_art is not null) try { using var stream = new MemoryStream(_art); _bitmap = Bitmap.DecodeToWidth(stream, 128); Cover.Source = _bitmap; } catch { }
        }
    }
}
