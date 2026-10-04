using System.IO;
using Windows.Media.Control;
using Windows.Media;

namespace EndfieldChargePlus.Music;

/// <summary>Native browser media integration; no scraping, audio extraction or API keys.</summary>
public sealed class WindowsMusicSession : IMusicSession
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _artKey;
    private byte[]? _art;
    private bool _disposed;
    private static bool Browser(GlobalSystemMediaTransportControlsSession session) =>
        !session.SourceAppUserModelId.Contains("webview", StringComparison.OrdinalIgnoreCase) && !session.SourceAppUserModelId.Contains("MusicPlayerHost", StringComparison.OrdinalIgnoreCase) &&
        new[] { "chrome", "msedge", "firefox", "MicrosoftEdge" }.Any(v => session.SourceAppUserModelId.Contains(v, StringComparison.OrdinalIgnoreCase));

    public async Task<MusicSnapshot> ReadAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token);
            var current = _manager.GetCurrentSession();
            _session = current is not null && Browser(current) ? current : _manager.GetSessions()
                .Where(Browser).OrderByDescending(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing).FirstOrDefault();
            if (_session is null) { _artKey = null; _art = null; return MusicSnapshot.Empty; }
            var properties = await _session.TryGetMediaPropertiesAsync().AsTask(token);
            var playback = _session.GetPlaybackInfo(); var controls = playback.Controls;
            var timeline = _session.GetTimelineProperties();
            double duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
            double position = Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing && timeline.LastUpdatedTime.Year > 2000)
                position += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds) * (playback.PlaybackRate ?? 1);
            position = duration > 0 ? Math.Clamp(position, 0, duration) : 0;
            var artKey = _session.SourceAppUserModelId + "\n" + properties.Title + "\n" + properties.Artist + "\n" + properties.AlbumTitle;
            if (_artKey != artKey)
            {
                _artKey = artKey; _art = null;
                if (properties.Thumbnail is { } thumbnail)
                {
                    try
                    {
                        using var raw = await thumbnail.OpenReadAsync().AsTask(token);
                        if (raw.Size is > 0 and <= 2_000_000)
                        {
                            using var input = raw.AsStreamForRead();
                            var bytes = new byte[(int)raw.Size]; await input.ReadExactlyAsync(bytes, token); _art = bytes;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* Missing or unreadable cover does not block playback. */ }
                }
            }
            return new(true, string.IsNullOrWhiteSpace(properties.Title) ? "曲名未提供" : properties.Title,
                string.IsNullOrWhiteSpace(properties.Artist) ? "歌手／頻道未提供" : properties.Artist,
                _session.SourceAppUserModelId, playing, position, duration,
                controls.IsPlayEnabled, controls.IsPauseEnabled, controls.IsPreviousEnabled, controls.IsNextEnabled,
                controls.IsPlaybackPositionEnabled && duration > 0, _art,
                controls.IsShuffleEnabled, playback.IsShuffleActive ?? false,
                controls.IsRepeatEnabled, playback.AutoRepeatMode switch { MediaPlaybackAutoRepeatMode.List => 1, MediaPlaybackAutoRepeatMode.Track => 2, _ => 0 });
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> SendAsync(MusicCommand command, double position, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_disposed || _session is null) return false;
            var c = _session.GetPlaybackInfo().Controls;
            return command switch
            {
                MusicCommand.Play when c.IsPlayEnabled => await _session.TryPlayAsync().AsTask(token),
                MusicCommand.Pause when c.IsPauseEnabled => await _session.TryPauseAsync().AsTask(token),
                MusicCommand.Previous when c.IsPreviousEnabled => await _session.TrySkipPreviousAsync().AsTask(token),
                MusicCommand.Next when c.IsNextEnabled => await _session.TrySkipNextAsync().AsTask(token),
                MusicCommand.Seek when c.IsPlaybackPositionEnabled && double.IsFinite(position) => await SeekAsync(position, token),
                MusicCommand.Shuffle when c.IsShuffleEnabled && position is 0 or 1 => await _session.TryChangeShuffleActiveAsync(position == 1).AsTask(token),
                MusicCommand.Repeat when c.IsRepeatEnabled && position is 0 or 1 or 2 => await _session.TryChangeAutoRepeatModeAsync(position switch { 1 => MediaPlaybackAutoRepeatMode.List, 2 => MediaPlaybackAutoRepeatMode.Track, _ => MediaPlaybackAutoRepeatMode.None }).AsTask(token),
                _ => false
            };
        }
        finally { _gate.Release(); }
    }
    private async Task<bool> SeekAsync(double seconds, CancellationToken token)
    {
        var t = _session!.GetTimelineProperties();
        var target = t.StartTime + TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, (t.EndTime - t.StartTime).TotalSeconds)));
        return await _session.TryChangePlaybackPositionAsync(target.Ticks).AsTask(token);
    }
    public void Dispose() { _disposed = true; _session = null; _manager = null; _art = null; }
}
