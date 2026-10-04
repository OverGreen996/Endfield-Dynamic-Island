using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EndfieldChargePlus.Music;
using EndfieldChargePlus.Views;

public static class MusicLayoutProbe
{
    public static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        int passed=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);passed++;Console.WriteLine("PASS: "+label);}
        foreach(double width in new[]{380d,560d,820d})
        {
            var window=new MusicIslandWindow(new Fixture());
            window.ApplyCompactLayout(width);
            var body=(Control)window.Content!;
            body.Measure(new Size(width,154));body.Arrange(new Rect(0,0,width,154));
            var title=window.FindControl<TextBlock>("SongTitle")!;
            var controls=new[]{"Shuffle","Previous","PlayPause","Next","Repeat","OpenPlaylist","SettingsButton"}.Select(n=>window.FindControl<Button>(n)!).ToArray();
            var positions=controls.Select(c=>(Control:c,Point:c.TranslatePoint(new Point(0,0),body)!.Value)).ToArray();
            Check(positions.Where(p=>p.Control.IsVisible).All(p=>p.Point.X>=8&&p.Point.Y>=8&&p.Point.X+p.Control.Bounds.Width<=width-8+.1&&p.Point.Y+p.Control.Bounds.Height<=154-8+.1),$"all visible capsule buttons within {width} width");
            var transport=positions.Take(5).OrderBy(p=>p.Point.X).ToArray();
            Check(transport.Zip(transport.Skip(1)).All(p=>p.First.Point.X+p.First.Control.Bounds.Width<=p.Second.Point.X+.1),$"transport buttons do not overlap at {width}");
            Check(title.Bounds.Width>0&&title.TextTrimming==Avalonia.Media.TextTrimming.CharacterEllipsis,$"title available and truncates safely at {width}");
            Check(!controls[0].IsEnabled&&!controls[4].IsEnabled,"unavailable shuffle/repeat disabled");
            Check(window.FindControl<Border>("PlayRing")!.Bounds.Width<=window.FindControl<Button>("PlayPause")!.Bounds.Width,"pause circle fits narrow control");
            window.Close();
        }
        Console.WriteLine($"{passed}/{passed} PASS; actual Avalonia layout.");
    }
    private sealed class Fixture:IMusicSession
    {
        public Task<MusicSnapshot> ReadAsync(CancellationToken token)=>Task.FromResult(MusicSnapshot.Empty);
        public Task<bool> SendAsync(MusicCommand command,double position,CancellationToken token)=>Task.FromResult(false);
        public void Dispose(){}
    }
}
