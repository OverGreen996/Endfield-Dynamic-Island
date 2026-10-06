using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EndfieldChargePlus.Music;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using SkiaSharp;

public sealed class MusicEdgeApplication : Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;}
    private sealed class Fixture : IMusicSession
    {
        public int Commands;
        public Task<MusicSnapshot> ReadAsync(CancellationToken token)=>Task.FromResult(MusicSnapshot.Empty with {Title="音樂島邊緣驗收",Artist="介面測試 · 不播放音訊",Available=true,CanPlay=true,CanPause=true,CanNext=true,CanPrevious=true,CanSeek=true,Duration=240,Position=84});
        public Task<bool> SendAsync(MusicCommand command,double position,CancellationToken token){Commands++;return Task.FromResult(true);}
        public void Dispose(){}
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        var fixture=new Fixture();var island=new MusicIslandWindow(fixture){ShowInTaskbar=true,Title="音樂邊緣 v16 驗收"};
        int corners=0;var status=new TextBlock{Margin=new Thickness(20,170,20,10),Foreground=Brushes.White};
        var grid=new Grid();grid.Children.Add(status);
        var show=new Button{Content="重顯音樂島",Margin=new Thickness(20,230,20,20),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top};grid.Children.Add(show);
        var corner=new Button{Content="+",Width=24,Height=20,Padding=new Thickness(0),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top};
        corner.Click+=(_,_)=>corners++;grid.Children.Add(corner);
        var underlay=new Window{Title="音樂邊緣 v16 底層",Width=820,Height=350,SystemDecorations=SystemDecorations.None,Background=Brush.Parse("#526774"),Content=grid};
        desktop.MainWindow=underlay;underlay.Position=new PixelPoint(220,250);underlay.Show();underlay.Activate();
        island.ShowMusic(new AppSettings());island.Position=underlay.Position;
        show.Click+=(_,_)=>{island.ShowMusic(new AppSettings());island.Position=underlay.Position;};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=(_,_)=>{status.Text=$"透明角落點擊：{corners}　播放控制：{fixture.Commands}";File.WriteAllText(Path.Combine(Environment.CurrentDirectory,"outputs","Island-Music-v16-UI-State.json"),System.Text.Json.JsonSerializer.Serialize(new{corners,commands=fixture.Commands,visible=island.IsVisible,scaling=island.RenderScaling}));};timer.Start();
        underlay.Closed+=(_,_)=>{timer.Stop();island.Close();desktop.Shutdown();};
        if(desktop.Args?.Contains("--music-edge-test")==true)
        Dispatcher.UIThread.Post(async()=>{
            try{await Task.Delay(3300);Run(island);desktop.Shutdown(0);}
            catch(Exception ex){Console.WriteLine(ex);desktop.Shutdown(1);}
        });
        base.OnFrameworkInitializationCompleted();
    }
    private static void Run(MusicIslandWindow island)
    {
        int passed=0;void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);passed++;Console.WriteLine("PASS: "+label);}
        var content=(Control)island.Content!;
        var hit=typeof(MusicIslandWindow).GetField("_hitTest",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(island)!;
        var set=hit.GetType().GetMethod("SetPaintedRegion")!;
        foreach(double width in new[]{380d,820d})foreach(double scale in new[]{1d,1.25,1.5,2})
        {
            island.Width=width;island.ApplyCompactLayout(width);content.Measure(new Size(width,154));content.Arrange(new Rect(0,0,width,154));
            string path=Path.Combine(Environment.CurrentDirectory,"outputs",$"Island-Music-v16-Render-{width}-{scale}.png");
            using(var image=new RenderTargetBitmap(new PixelSize((int)(width*scale),(int)(154*scale)),new Vector(96*scale,96*scale))){image.Render(content);image.Save(path);}
            using var pixels=SKBitmap.Decode(path);
            var rows=new (int Left,int Right)[pixels.Height];
            for(int y=0;y<pixels.Height;y++){int l=0,r=pixels.Width;while(l<r&&pixels.GetPixel(l,y).Alpha==0)l++;while(r>l&&pixels.GetPixel(r-1,y).Alpha==0)r--;rows[y]=(l,r);}
            set.Invoke(hit,new object[]{rows});
            var region=CreateRectRgn(0,0,0,0);GetWindowRgn(island.TryGetPlatformHandle()!.Handle,region);GetRgnBox(region,out var box);Console.WriteLine($"Region box {box.L},{box.T},{box.R},{box.B}");
            int cropped=0,partial=0,transparentBlocked=0;var missing=new List<string>();
            try{for(int y=0;y<pixels.Height;y++)for(int x=0;x<pixels.Width;x++){var color=pixels.GetPixel(x,y);if(color.Alpha>0&&!PtInRegion(region,x,y)){cropped++;if(missing.Count<12)missing.Add($"{x},{y}:{color}");}if(color.Alpha==0&&PtInRegion(region,x,y))transparentBlocked++;if(color.Alpha>0&&color.Alpha<255&&y<128*scale)partial++;}}
            finally{DeleteObject(region);}
            if(cropped>0)Console.WriteLine("Cropped pixels: "+string.Join("; ",missing));
            Check(cropped==0,$"native mask preserves every painted pixel: width={width}, scale={scale}, cropped={cropped}");
            Check(transparentBlocked==0,$"native mask excludes every fully transparent pixel: width={width}, scale={scale}");
            Check(partial>100,$"fractional alpha preserved at scale={scale}, width={width}");
            Check(island.FindControl<Avalonia.Controls.Shapes.Path>("PlaybackArc")!.Data is not null&&!island.FindControl<Avalonia.Controls.Shapes.Path>("EdgeAccent")!.IsVisible,$"original circular progress replaces corner decoration: width={width}, scale={scale}");
        }
        Console.WriteLine($"{passed}/{passed} PASS; native HRGN versus rendered pixels; simulated render scales, not changed Windows DPI.");
    }
    [DllImport("gdi32.dll")]private static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")]private static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr region);
    [DllImport("user32.dll")]private static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
    [StructLayout(LayoutKind.Sequential)]private struct Box{public int L,T,R,B;}
    [DllImport("gdi32.dll")]private static extern int GetRgnBox(IntPtr region,out Box rect);
}
