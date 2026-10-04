using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EndfieldChargePlus.Views;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Notifications;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Reflection;

public sealed class AllEdgeApplication:Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        var main=new Window{Width=200,Height=100,Title="共用邊緣自動验收"};desktop.MainWindow=main;main.Show();
        Dispatcher.UIThread.Post(async()=>
        {
            int passed=0;void Check(bool value,string text){if(!value)throw new Exception("FAIL: "+text);passed++;Console.WriteLine("PASS: "+text);}
            string root=Path.Combine(Path.GetTempPath(),"IslandEdges-"+Guid.NewGuid().ToString("N"));
            var ai=new AssistantIslandWindow(new PersonalAssistantStore(Path.Combine(root,"p.dpapi")),new AssistantSession(Path.Combine(root,"s.dpapi")));
            var notice=new NotificationIslandWindow();
            try
            {
                foreach(var window in new Window[]{ai,notice})foreach(double width in new[]{420d,560d})
                {
                    if(window==ai){ai.ShowInput(new AppSettings());ai.ApplyViewportLayout(width-16,360);}else{notice.ShowNotification(NotificationCard.Create("qa/edge","Edge QA","通知邊緣","合成資料",DateTimeOffset.Now),new AppSettings{DisplayDurationSeconds=10});notice.Width=width;}
                    await Task.Delay(250);
                    string method=window==ai?"UpdateInputRegion":"UpdateRegion";window.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    var content=(Control)window.Content!;double scale=window.RenderScaling;
                    using var image=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width*scale),(int)Math.Ceiling(window.Bounds.Height*scale)),new Vector(96*scale,96*scale));image.Render(content);
                    using var stream=new MemoryStream();image.Save(stream);stream.Position=0;using var pixels=SKBitmap.Decode(stream);
                    var region=CreateRectRgn(0,0,0,0);Check(GetWindowRgn(window.TryGetPlatformHandle()!.Handle,region)!=0,"native shared painted-region applied: "+window.GetType().Name+width);
                    int lost=0,zeroBlocked=0,aa=0;
                    try{for(int y=0;y<pixels.Height;y++)for(int x=0;x<pixels.Width;x++){var color=pixels.GetPixel(x,y);bool inside=PtInRegion(region,x,y);if(color.Alpha>0&&!inside)lost++;if(color.Alpha==0&&inside)zeroBlocked++;if(color.Alpha is >0 and <255)aa++;}}finally{DeleteObject(region);}
                    Check(lost==0,"all painted AA edge pixels retained: "+window.GetType().Name+width+" lost="+lost);
                    Check(zeroBlocked==0,"all zero-alpha pixels excluded: "+window.GetType().Name+width+" blocked="+zeroBlocked);
                    Check(aa>50,"real render includes smooth fractional alpha: "+window.GetType().Name+width);
                    if(window==notice){bool arc=true;for(int degrees=5;degrees<=85;degrees+=5){double angle=degrees*Math.PI/180;int count=0;for(double r=55;r<60;r+=.25){int x=(int)((window.Bounds.Width-8-60+Math.Cos(angle)*r)*scale),y=(int)((window.Bounds.Height-8-60+Math.Sin(angle)*r)*scale);var c=pixels.GetPixel(x,y);if(c.Red>160&&c.Green>160&&c.Blue<100)count++;}if(count<8||count>19)arc=false;}Check(arc,"notification yellow quarter-ring fits curved edge: "+width);}
                    if(window==ai)ai.HideIsland();else notice.HideIsland();
                }
                Console.WriteLine($"{passed}/{passed} PASS; production shared masks on real native windows at current Windows DPI.");
                desktop.Shutdown(0);
            }
            catch(Exception ex){Console.WriteLine(ex);desktop.Shutdown(1);}
            finally{ai.Close();notice.Close();}
        });
        base.OnFrameworkInitializationCompleted();
    }
    [DllImport("gdi32.dll")]private static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")]private static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr region);
    [DllImport("user32.dll")]private static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
}
