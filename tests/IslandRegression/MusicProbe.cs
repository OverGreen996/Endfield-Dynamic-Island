using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EndfieldChargePlus.Music;

public static class MusicProbe
{
    private const string SamplePlaylist = "https://www.youtube.com/playlist?list=PLislandFixture123";
    public static async Task Run()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
        var root = Path.Combine(AppContext.BaseDirectory, "music-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "music.json"); var store = new PlaylistStore(path);
        Check(store.Load() == "" && !File.Exists(path), "default playlist does not write or open browser");
        Check(PlaylistStore.Normalize("https://www.youtube.com/watch?v=fixture12345&list=PLislandFixture123") == SamplePlaylist, "user watch+list URL accepted");
        Check(PlaylistStore.Normalize(SamplePlaylist + "&si=abc") == SamplePlaylist, "tracking stripped");
        foreach (string input in new[] { "", "file:///C:/bad", "http://youtube.com/playlist?list=abc", "https://youtube.com.evil.test/playlist?list=abc", "https://user:password@youtube.com/playlist?list=abc", "https://youtube.com:444/playlist?list=abc", "https://youtube.com/watch?v=fixture12345", "https://youtube.com/playlist?list=abc&list=def", "https://youtube.com/playlist?list=abc%27%3Cscript%3E", "https://youtube.com/shorts/abc?list=abc" })
        { bool rejected = false; try { PlaylistStore.Normalize(input); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "invalid URL rejected " + (passed - 2)); }
        store.Save(SamplePlaylist); var first = File.ReadAllText(path);
        store.Save("https://www.youtube.com/playlist?list=PLdifferentTest");
        Check(store.Load().EndsWith("PLdifferentTest") && File.ReadAllText(path + ".previous") == first, "atomic update retains previous playlist");
        var saved = File.ReadAllText(path); try { store.Save("invalid"); } catch (InvalidOperationException) { }
        Check(File.ReadAllText(path) == saved, "invalid save preserves existing setting");
        File.WriteAllText(path, "broken"); bool corrupt = false; try { store.Load(); } catch { corrupt = true; }
        Check(corrupt && File.ReadAllText(path) == "broken", "corrupt setting reported and retained");
        Check(MusicSnapshot.Time(43) == "0:43" && MusicSnapshot.Time(3661) == "1:01:01" && MusicSnapshot.Time(double.NaN) == "--:--" && MusicSnapshot.Time(-1) == "--:--", "time formatting and invalid duration");

        var native = new FakeMusic("native browser");
        var created = new List<FakeMusic>();
        using var session = new YouTubePlaylistSession(native, () => {var player=new FakeMusic("playlist " + (created.Count+1));created.Add(player);return player;});
        Check(session.Mode==MusicMode.Browser,"default mode follows browser only");
        Check((await session.ReadAsync(CancellationToken.None)).Title=="native browser","browser metadata comes from native session");
        Check(await session.SendAsync(MusicCommand.Pause,0,CancellationToken.None)&&native.Sent.Count==1,"browser pause routes only to browser");
        session.OpenBackgroundPlaylist(SamplePlaylist);
        Check(session.Mode==MusicMode.Playlist&&created[0].Url==SamplePlaylist,"explicit playlist mode owns its dedicated player");
        Check(!await session.SendAsync(MusicCommand.Next,0,CancellationToken.None),"old browser UI cannot send a command before playlist metadata arrives");
        Check((await session.ReadAsync(CancellationToken.None)).Title=="playlist 1"&&native.ReadCount==1,"playlist never reads browser metadata");
        Check(await session.SendAsync(MusicCommand.Shuffle,1,CancellationToken.None)&&created[0].Sent.Count==1&&native.Sent.Count==1,"playlist shuffle does not control browser");
        bool invalid=false;try{session.OpenBackgroundPlaylist("invalid");}catch{invalid=true;}
        Check(invalid&&!created[0].Disposed&&session.Mode==MusicMode.Playlist,"invalid URL preserves active playlist");
        created[0].DeferredRead=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRead=session.ReadAsync(CancellationToken.None);
        session.FollowBrowser();
        Check(created[0].Disposed&&created[0].LastReadToken.IsCancellationRequested,"switch cancels old reads and disposes playlist player");
        Check(!await session.SendAsync(MusicCommand.Repeat,2,CancellationToken.None),"old playlist UI cannot command browser before fresh metadata");
        Check((await session.ReadAsync(CancellationToken.None)).Title=="native browser","browser mode clears playlist metadata");
        created[0].DeferredRead!.SetResult(created[0].Snapshot);
        bool staleRejected=false;try{await staleRead;}catch(OperationCanceledException){staleRejected=true;}
        Check(staleRejected,"late playlist response cannot overwrite browser state");
        native.DeferredRead=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldBrowser=session.ReadAsync(CancellationToken.None);
        session.OpenBackgroundPlaylist(SamplePlaylist);
        native.DeferredRead.SetResult(native.Snapshot);
        staleRejected=false;try{await oldBrowser;}catch(OperationCanceledException){staleRejected=true;}
        native.DeferredRead=null;
        Check(staleRejected,"late browser response cannot overwrite playlist state");
        created[1].Ready=false;var unavailable=await session.ReadAsync(CancellationToken.None);
        Check(!unavailable.Available&&unavailable.Title!="native browser"&&!await session.SendAsync(MusicCommand.Play,0,CancellationToken.None),"failed playlist never falls back to unrelated browser");
        created[1].Ready=true;await session.ReadAsync(CancellationToken.None);
        created[1].DeferredSend=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldCommand=session.SendAsync(MusicCommand.Repeat,2,CancellationToken.None);
        session.FollowBrowser();created[1].DeferredSend!.SetResult(true);
        bool cancelled=false;try{await oldCommand;}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled&&created[1].Disposed&&native.Sent.Count==1,"late command acknowledgment is cancelled and never rerouted");
        await session.ReadAsync(CancellationToken.None);
        Check(await session.SendAsync(MusicCommand.Next,0,CancellationToken.None)&&native.Sent.Count==2,"fresh browser controls remain functional after switch");
        session.OpenBackgroundPlaylist(SamplePlaylist);await session.ReadAsync(CancellationToken.None);
        Check(created.Count==3&&created[2].Sent.Count==0&&!created[2].Disposed,"return to playlist creates clean independent state");
        var state=new YouTubePlayerState(true,"t","a","",false,0,200,false,0,"",10);
        Check(YouTubePlayerState.Valid(state)&&!YouTubePlayerState.Valid(state with{duration=-1})&&!YouTubePlayerState.Valid(state with{video="../../bad"})&&!YouTubePlayerState.Valid(state with{title=new string('x',513)}),"background metadata validation retained after embed removal");
        Check(!typeof(YouTubePlaylistSession).Assembly.GetManifestResourceNames().Any(n=>n.Contains("PlaylistPlayer.html")),"removed iframe player is absent from the shipped assembly");
        session.Dispose();
        Check(created[2].Disposed&&native.Disposed,"exit disposes only owned sessions");
        Console.WriteLine($"{passed}/{passed} PASS; two-mode isolation and removed iframe fixtures; no browser or Gemini calls.");
    }
    private sealed class FakeMusic(string title) : IPlaylistMusicSession
    {
        public string? Url; public bool Disposed,Ready=true;
        public int ReadCount;public List<MusicCommand> Sent=new();
        public CancellationToken LastReadToken;
        public TaskCompletionSource<MusicSnapshot>? DeferredRead;
        public TaskCompletionSource<bool>? DeferredSend;
        public MusicSnapshot Snapshot=>new(Ready,title,"artist","source",false,10,200,true,true,true,true,true,null,true,false,true,0);
        public void Open(string url){Url=url;}
        public Task<MusicSnapshot> ReadAsync(CancellationToken token){ReadCount++;LastReadToken=token;return DeferredRead?.Task??Task.FromResult(Snapshot);}
        public Task<bool> SendAsync(MusicCommand command,double position,CancellationToken token){Sent.Add(command);return DeferredSend?.Task??Task.FromResult(true);}
        public void Dispose(){Disposed=true;}
    }
}