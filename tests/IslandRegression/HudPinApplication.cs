using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

public sealed class HudPinApplication : Application
{
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    private static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        var hud=new HudWindow();desktop.MainWindow=hud;
        Dispatcher.UIThread.Post(async()=>{
            int passed=0;var failed=new List<string>();
            void Check(bool ok,string message){Console.WriteLine((ok?"PASS: ":"FAIL: ")+message);if(ok)passed++;else failed.Add(message);}
            using var runtime=new CustomHudRuntime(hud);
            var settings=new AppSettings{GlobalScale=1,DisplayDurationSeconds=3};
            hud.ApplySettings(settings);runtime.ApplySettings(settings);
            var profile=CustomHudSettings.CreateDefaultProfiles().Single(p=>p.BuiltInKey=="system.overview");
            PixelPoint Center(){var pill=hud.FindControl<Border>("Pill")!;return pill.PointToScreen(new Point(pill.Bounds.Width/2,pill.Bounds.Height/2));}
            int Hit(PixelPoint p){long xy=((long)(ushort)p.Y<<16)|(ushort)p.X;return SendMessage(hud.TryGetPlatformHandle()!.Handle,0x84,IntPtr.Zero,new IntPtr(xy)).ToInt32();}
            hud.PinToggleRequested+=()=>Console.WriteLine("Native pin callback received");
            void Click()
            {
                var p=Center();
                var client=new NativePoint{X=p.X,Y=p.Y};
                ScreenToClient(hud.TryGetPlatformHandle()!.Handle,ref client);
                long xy=((long)(ushort)client.Y<<16)|(ushort)client.X;
                SendMessage(hud.TryGetPlatformHandle()!.Handle,0x202,IntPtr.Zero,new IntPtr(xy));
            }
            bool Pinned()=> (bool)typeof(CustomHudRuntime).GetField("_userPinned",Fields)!.GetValue(runtime)!;
            try{
                var intro=runtime.PreviewAsync(profile);await Task.Delay(1100);
                Check(Hit(Center())==1,"visible performance intro accepts native hit testing");Click();
                Check(Pinned(),"click during intro pins overview");await intro;
                Check((string)typeof(CustomHudRuntime).GetField("_persistentProfileId",Fields)!.GetValue(runtime)! == profile.Id,"early pin retains the requested overview, not the default scheme");
                var tick=typeof(CustomHudRuntime).GetMethod("TickAsync",Fields)!;
                await Task.Delay(3300);await (Task)tick.Invoke(runtime,null)!;
                Check(Pinned()&&hud.IsVisible,"early pin survives intro and auto-hide deadline");
                Check((string)typeof(CustomHudRuntime).GetField("_persistentProfileId",Fields)!.GetValue(runtime)! == profile.Id,"refresh preserves pinned preview without switching to the saved default scheme");
                Click();await Task.Delay(450);Check(!Pinned()&&!hud.IsVisible,"second click hides pinned overview");
                await runtime.PreviewAsync(profile);
                Check(Hit(Center())==1,"completed preview accepts native hit testing");Click();
                Check(Pinned(),"click after intro pins overview");
                await Task.Delay(3300);await (Task)tick.Invoke(runtime,null)!;
                Check(Pinned()&&hud.IsVisible,"pinned overview remains visible after timeout");
                Click();await Task.Delay(450);Check(!Pinned()&&!hud.IsVisible,"second click releases and hides overview");
                await runtime.PreviewAsync(profile);
                var outside=hud.PointToScreen(new Point(2,2));
                Check(Hit(outside)==-1,"transparent window margin remains click-through");
                await Task.Delay(3300);await (Task)tick.Invoke(runtime,null)!;
                Check(!Pinned()&&!hud.IsVisible,"unclicked preview still closes after timeout");
                Check(!runtime.EffectiveSettings.AlwaysVisible,"temporary pin does not change the saved always-visible setting");
                Console.WriteLine($"{passed}/{passed+failed.Count} native HUD pin PASS");desktop.Shutdown(failed.Count==0?0:1);
            }catch(Exception e){Console.WriteLine(e);desktop.Shutdown(1);}finally{hud.Close();}
        });
    }
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd,ref NativePoint point);
}
