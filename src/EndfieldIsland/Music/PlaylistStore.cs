using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Music;

public sealed class PlaylistStore
{
    // Public builds never include a personal playlist. Existing saved playlists remain intact.
    public const string DefaultPlaylist = "";
    private readonly string _path;
    public PlaylistStore(string? path = null) => _path = path ?? Path.Combine(SettingsManager.SettingsDirectory, "music.json");

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 2048 ||
            !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
            uri.Host.ToLowerInvariant() is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com") ||
            uri.AbsolutePath is not ("/playlist" or "/watch"))
            throw new InvalidOperationException("請輸入含有 list= 的 YouTube 播放清單網址。");
        var values = uri.Query.TrimStart('?').Split('&').Select(v => v.Split('=', 2))
            .Where(v => v.Length == 2 && v[0] == "list").Select(v => Uri.UnescapeDataString(v[1])).ToArray();
        if (values.Length != 1 || !Regex.IsMatch(values[0], "^[A-Za-z0-9_-]{3,150}$"))
            throw new InvalidOperationException("播放清單 ID 不完整，請重新複製 YouTube 清單網址。");
        return "https://www.youtube.com/playlist?list=" + values[0];
    }
    public string Load()
    {
        if (!File.Exists(_path)) return DefaultPlaylist;
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        return Normalize(doc.RootElement.GetProperty("playlist_url").GetString());
    }
    public void Save(string input)
    {
        var url = Normalize(input); // Validate before touching the previous setting.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new { playlist_url = url }));
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".previous", true);
            else File.Move(temp, _path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Open() => Process.Start(new ProcessStartInfo(Normalize(Load())) { UseShellExecute = true });
}
