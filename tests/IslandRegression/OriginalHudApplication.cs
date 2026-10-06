using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Music;
using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using SkiaSharp;

public sealed class OriginalHudApplication:Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;}
    private sealed class Fixture:IMusicSession
    {
        internal int Commands;
        public Task<MusicSnapshot> ReadAsync(CancellationToken token)=>Task.FromResult(MusicSnapshot.Empty with{
            Available=true,Title="向更遠的地平線",Artist="Endfield Soundtrack",Playing=true,
            CanPlay=true,CanPause=true,CanPrevious=true,CanNext=true,CanShuffle=true,CanRepeat=true,CanSeek=true,
            Duration=260,Position=84,Shuffle=true,Repeat=1});
        public Task<bool> SendAsync(MusicCommand command,double position,CancellationToken token){Commands++;return Task.FromResult(true);}
        public void Dispose(){}
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        var hud=new HudWindow();var fixture=new Fixture();var music=new MusicIslandWindow(fixture);var notice=new NotificationIslandWindow();desktop.MainWindow=hud;
        Dispatcher.UIThread.Post(async()=>{
            int count=0;void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);count++;Console.WriteLine("PASS: "+message);}
            try
            {
                var folder=Path.Combine(Environment.CurrentDirectory,"artifacts/native-qa/original-style");Directory.CreateDirectory(folder);
                var values=new Dictionary<string,object?>{["cpu.usage"]=42d,["cpu.frequency_ghz"]=4.49d,["gpu.usage"]=68d,["gpu.dedicated_used_bytes"]=6.4*1073741824d,["gpu.vram_bytes"]=12*1073741824d,["memory.usage"]=40d,["memory.used_bytes"]=12.8*1073741824d,["memory.total_bytes"]=32*1073741824d};
                var overview=CustomHudSettings.CreateDefaultProfiles().Single(p=>p.BuiltInKey=="system.overview");
                hud.ApplySettings(new AppSettings{GlobalScale=1});var performanceIntro=hud.ShowPreviewAsync(HudProfileRenderer.Render(overview,values));await Task.Delay(1600);
                Check(hud.FindControl<Border>("Pill")!.Height>60&&!HudProfileRenderer.Render(overview,values).SimpleAnimation,"performance overview defaults to original full ripple animation");
                NativeHudCapture.Save(hud,hud.FindControl<Border>("Pill")!,Path.Combine(folder,"performance-intro-screen.png"));await performanceIntro;await hud.HideAnimatedAsync();
                foreach(var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English})
                foreach(double scale in new[]{.8,1d})
                {
                    LocalizationManager.SetLanguage(language);hud.ApplySettings(new AppSettings{AlwaysVisible=true,GlobalScale=scale});
                    await hud.ShowPersistentAsync(HudProfileRenderer.Render(overview,values),quick:true);await Task.Delay(100);
                    var pill=hud.FindControl<Border>("Pill")!;
                    Check(pill.Width==560&&pill.Height==60&&pill.CornerRadius.TopLeft==30,"original performance geometry: "+language+scale);
                    Check(!hud.FindControl<Border>("OverviewIdentity")!.IsVisible&&!hud.FindControl<StackPanel>("OverviewHeading")!.IsVisible&&!hud.FindControl<Avalonia.Controls.Shapes.Path>("OverviewEdge")!.IsVisible,"no added logo/header/edge: "+language+scale);
                    Check(hud.FindControl<Grid>("Badge")!.IsVisible&&hud.FindControl<Grid>("SquareForm")!.IsVisible,"original small icon and circular badge: "+language+scale);
                    var band=hud.FindControl<Grid>("OverviewHost")!;
                    File.WriteAllText(Path.Combine(folder,"metric-bounds.json"),System.Text.Json.JsonSerializer.Serialize(band.GetVisualDescendants().OfType<TextBlock>().Select(t=>new{t.Text,Bounds=t.Bounds.ToString(),Desired=t.DesiredSize.ToString(),t.Opacity,t.IsVisible,Point=t.TranslatePoint(default,band)?.ToString(),PillPoint=t.TranslatePoint(default,pill)?.ToString(),Foreground=t.Foreground?.ToString(),Layout=t.TextLayout.Height})));
                    Check(band.GetVisualDescendants().OfType<TextBlock>().Where(t=>new[]{"CPU","GPU","RAM","VRAM"}.Contains(t.Text)).Select(t=>t.Text).SequenceEqual(new[]{"CPU","GPU","RAM","VRAM"}),"four explicit metric names: "+language+scale);
                    Save(hud,pill,Path.Combine(folder,$"performance-{language}-{scale}.png")); NativeHudCapture.Save(hud,pill,Path.Combine(folder,$"performance-screen-{language}-{scale}.png")); Check(NativeHudCapture.LabelsPaint(hud,pill,band),"all four labels paint on actual Windows surface: "+language+scale); if(Environment.GetCommandLineArgs().Contains("--hud-probe")){File.WriteAllText(Path.Combine(folder,"screen-rect.json"),System.Text.Json.JsonSerializer.Serialize(new{X=hud.Position.X+(pill.TranslatePoint(default,hud)!.Value.X*hud.RenderScaling),Y=hud.Position.Y+(pill.TranslatePoint(default,hud)!.Value.Y*hud.RenderScaling),Width=pill.Bounds.Width*scale*hud.RenderScaling,Height=pill.Bounds.Height*scale*hud.RenderScaling}));await Task.Delay(10000);desktop.Shutdown(0);return;} await hud.HideAnimatedAsync();
                    music.ShowMusic(new AppSettings{GlobalScale=scale});await Task.Delay(3300);
                    var body=music.FindControl<Border>("Island")!;
                    Check(Math.Abs(body.Width-560)<.1&&Math.Abs(body.Height-60)<.1&&Math.Abs(body.CornerRadius.TopLeft-30)<.1,"music shares original proportions: "+language+scale+" "+body.Width+"x"+body.Height);
                    Check(music.FindControl<Border>("PlayRing")!.Width==46&&music.FindControl<Avalonia.Controls.Shapes.Path>("PlaybackArc")!.IsVisible,"original circular progress badge: "+language+scale);
                    Check(music.FindControl<TextBlock>("SongTitle")!.Text=="向更遠的地平線","language switch preserves song title: "+language+scale);
                    Check(music.FindControl<Button>("Shuffle")!.IsEnabled&&music.FindControl<Button>("Repeat")!.IsEnabled,"shuffle and repeat retained: "+language+scale);
                    Save(music,body,Path.Combine(folder,$"music-{language}-{scale}.png")); NativeHudCapture.Save(music,body,Path.Combine(folder,$"music-screen-{language}-{scale}.png")); music.HideIsland();
                    notice.ShowNotification(NotificationCard.Create("qa/original","ENDFIELD","設計評審會議","今天 10:00–11:00 · 會議室 B",DateTimeOffset.UtcNow),new AppSettings{GlobalScale=scale,DisplayDurationSeconds=10});await Task.Delay(3300);
                    var notification=notice.FindControl<Border>("Island")!;
                    Check(notification.Width==560&&notification.Height==60&&notification.CornerRadius.TopLeft==30,"notification shares original capsule and music height: "+language+scale);
                    Check(!notice.FindControl<Avalonia.Controls.Shapes.Path>("EdgeAccent")!.IsVisible&&notice.FindControl<Avalonia.Controls.Shapes.Path>("CountdownArc")!.Data is not null,"notification uses circular countdown without outer yellow edge: "+language+scale);
                    Check(notice.FindControl<TextBlock>("TitleLabel")!.Text=="設計評審會議","notification content stays in its original language: "+language+scale);
                    NativeHudCapture.Save(notice,notification,Path.Combine(folder,$"notification-screen-{language}-{scale}.png"));notice.HideIsland();
                }
                music.ShowMusic(new AppSettings{GlobalScale=1});await Task.Delay(700);Save(music,music.FindControl<Border>("Island")!,Path.Combine(folder,"music-intro-expand.png"));
                await Task.Delay(900);Save(music,music.FindControl<Border>("Island")!,Path.Combine(folder,"music-intro-ripple.png"));
                Check(music.FindControl<Border>("Island")!.Height>60,"original expanded intro visible");
                music.HideIsland();await Task.Delay(100);music.ShowMusic(new AppSettings{GlobalScale=1});await Task.Delay(3300);
                Check(music.FindControl<Grid>("ContentGrid")!.Opacity==1&&music.FindControl<Grid>("Header")!.IsHitTestVisible,"interrupted intro reopens with usable content");
                music.FindControl<Button>("Next")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);
                Check(fixture.Commands==1,"playback command still reaches session");
                music.HideIsland();
                notice.ShowNotification(NotificationCard.Create("qa/reopen","QA","取消後重開","不應留下空白介面",DateTimeOffset.UtcNow),new AppSettings{GlobalScale=1,DisplayDurationSeconds=3});await Task.Delay(1600);
                Check(notice.FindControl<Border>("Island")!.Height>60,"notification uses original expanded ripple intro");
                notice.HideIsland();notice.ShowNotification(NotificationCard.Create("qa/return","QA","恢復通知","動畫後才開始閱讀倒數",DateTimeOffset.UtcNow),new AppSettings{GlobalScale=1,DisplayDurationSeconds=3});await Task.Delay(3300);
                var presentation=(NotificationPresentation)typeof(NotificationIslandWindow).GetField("_state",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(notice)!;
                Check(notice.FindControl<Grid>("NoticeContent")!.Opacity==1&&presentation.RemainingSeconds(DateTimeOffset.UtcNow)>2,"notification interruption restores content and preserves reading duration");
                Console.WriteLine($"{count}/{count} PASS; original-style native geometry, transitions and synthetic playback; zero model calls.");desktop.Shutdown(0);
            }
            catch(Exception ex){Console.WriteLine(ex);desktop.Shutdown(1);}
            finally{notice.Close();music.Close();hud.Close();}
        });base.OnFrameworkInitializationCompleted();
    }
    private static void Save(Window window,Border body,string path)
    {
        var root=(Control)window.Content!;var offset=new Vector(root.Bounds.X,root.Bounds.Y);var point=body.TranslatePoint(default,root)!.Value+offset;var end=body.TranslatePoint(new Point(body.Bounds.Width,body.Bounds.Height),root)!.Value+offset;
        using var render=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width*2),(int)Math.Ceiling(window.Bounds.Height*2)),new Vector(192,192));render.Render(root);
        using var stream=new MemoryStream();render.Save(stream);stream.Position=0;using var bitmap=SKBitmap.Decode(stream);using var crop=new SKBitmap();
        if(!bitmap.ExtractSubset(crop,new SKRectI(Math.Max(0,(int)Math.Floor(point.X*2)-2),Math.Max(0,(int)Math.Floor(point.Y*2)-2),Math.Min(bitmap.Width,(int)Math.Ceiling(end.X*2)+2),Math.Min(bitmap.Height,(int)Math.Ceiling(end.Y*2)+2))))throw new Exception("Crop failed");
        using var image=SKImage.FromBitmap(crop);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(path);encoded.SaveTo(file);
    }
}
