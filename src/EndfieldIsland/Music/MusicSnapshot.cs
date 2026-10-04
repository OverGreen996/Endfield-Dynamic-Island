namespace EndfieldChargePlus.Music;

public sealed record MusicSnapshot(bool Available, string Title, string Artist, string Source,
    bool Playing, double Position, double Duration, bool CanPlay, bool CanPause,
    bool CanPrevious, bool CanNext, bool CanSeek, byte[]? Artwork = null,
    bool CanShuffle = false, bool Shuffle = false, bool CanRepeat = false, int Repeat = 0, string? Notice = null)
{
    public static MusicSnapshot Empty => new(false, "尚未偵測到音樂", "請先在瀏覽器播放 YouTube 清單", "", false, 0, 0, false, false, false, false, false);
    public static string Time(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "--:--";
        var t = TimeSpan.FromSeconds(Math.Min(seconds, 86400 * 7));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
public enum MusicCommand { Play, Pause, Previous, Next, Seek, Shuffle, Repeat }
public enum MusicMode { Browser, Playlist }
public interface IPlaylistMusicSession : IMusicSession { void Open(string url); }
public interface IMusicSession : IDisposable
{
    Task<MusicSnapshot> ReadAsync(CancellationToken token);
    Task<bool> SendAsync(MusicCommand command, double position, CancellationToken token);
}
