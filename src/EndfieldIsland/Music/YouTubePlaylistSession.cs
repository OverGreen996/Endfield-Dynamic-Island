namespace EndfieldChargePlus.Music;

/// <summary>Explicit source ownership. Playlist failures never route to unrelated browser media.</summary>
public sealed class YouTubePlaylistSession : IMusicSession
{
    private readonly IMusicSession _native;
    private readonly Func<IPlaylistMusicSession> _createPlaylist;
    private readonly object _gate = new();
    private IPlaylistMusicSession? _background;
    private CancellationTokenSource _modeRequests = new();
    private MusicMode _mode = MusicMode.Browser;
    private long _generation, _readyGeneration = -1;
    private bool _disposed;
    public MusicMode Mode { get { lock (_gate) return _mode; } }
    public event Action? ModeChanged;
    public YouTubePlaylistSession(IMusicSession? native = null, Func<IPlaylistMusicSession>? createPlaylist = null)
    { _native = native ?? new WindowsMusicSession(); _createPlaylist = createPlaylist ?? (() => new BackgroundYouTubeSession()); }
    private long Transition(MusicMode mode)
    {
        CancellationTokenSource previous; IPlaylistMusicSession? background; long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _modeRequests; _modeRequests = new();
            background = _background; _background = null;
            _mode = mode; generation = ++_generation; _readyGeneration = -1;
        }
        previous.Cancel(); previous.Dispose(); background?.Dispose();
        ModeChanged?.Invoke();
        return generation;
    }
    public void FollowBrowser() => Transition(MusicMode.Browser);
    public void OpenBackgroundPlaylist(string url)
    {
        url = PlaylistStore.Normalize(url);
        var generation = Transition(MusicMode.Playlist);
        var background = _createPlaylist();
        try
        {
            background.Open(url);
            lock (_gate)
            {
                if (_disposed || generation != _generation) { background.Dispose(); return; }
                _background = background;
            }
        }
        catch { background.Dispose(); throw; }
    }
    private (IMusicSession? Source, long Generation, CancellationToken ModeToken) Capture()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (_mode == MusicMode.Playlist ? _background : _native, _generation, _modeRequests.Token);
        }
    }
    public async Task<MusicSnapshot> ReadAsync(CancellationToken token)
    {
        var active = Capture();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, active.ModeToken);
        var snapshot = active.Source is null
            ? MusicSnapshot.Empty with { Title = "播放清單尚未連線", Artist = "點封面重新啟動清單" }
            : await active.Source.ReadAsync(request.Token);
        request.Token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_disposed || active.Generation != _generation) throw new OperationCanceledException("音樂模式已切換");
            _readyGeneration = snapshot.Available ? _generation : -1;
            var label = _mode == MusicMode.Playlist ? "播放清單" : "跟隨瀏覽器";
            return snapshot with { Notice = label + " · " + (snapshot.Notice ?? snapshot.Source) };
        }
    }
    public async Task<bool> SendAsync(MusicCommand command, double position, CancellationToken token)
    {
        var active = Capture();
        lock (_gate) if (active.Source is null || active.Generation != _readyGeneration || active.Generation != _generation) return false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, active.ModeToken);
        request.Token.ThrowIfCancellationRequested();
        var ok = await active.Source!.SendAsync(command, position, request.Token);
        request.Token.ThrowIfCancellationRequested();
        lock (_gate) return !_disposed && active.Generation == _generation && ok;
    }
    public void Dispose()
    {
        CancellationTokenSource requests; IPlaylistMusicSession? background;
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            requests = _modeRequests; background = _background; _background = null; ++_generation;
        }
        requests.Cancel(); requests.Dispose(); background?.Dispose(); _native.Dispose();
    }
}