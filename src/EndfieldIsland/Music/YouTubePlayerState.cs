using System.Text.RegularExpressions;
namespace EndfieldChargePlus.Music;
public sealed record YouTubePlayerState(bool ready, string title, string artist, string video, bool playing,
    double position, double duration, bool shuffle, int repeat, string error, int count)
{
    public static bool Valid(YouTubePlayerState s) => s.title is { Length: <= 512 } && s.artist is { Length: <= 512 }
        && s.error is { Length: <= 200 } && s.video is not null && Regex.IsMatch(s.video, "^([A-Za-z0-9_-]{11})?$")
        && double.IsFinite(s.position) && double.IsFinite(s.duration) && s.position >= 0 && s.duration is >= 0 and <= 604800
        && s.repeat is >= 0 and <= 2 && s.count is >= 0 and <= 10000;
}
