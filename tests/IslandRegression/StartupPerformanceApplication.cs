using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Music;
using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

public sealed class StartupPerformanceApplication:Application
{
    private sealed class HeldMusic:IMusicSession
    {
        public Task<MusicSnapshot> ReadAsync(CancellationToken token)=>Task.Delay(3000,token).ContinueWith(_=>MusicSnapshot.Empty,token);
        public Task<bool> SendAsync(MusicCommand command,double position,CancellationToken token)=>Task.FromResult(false);
        public void Dispose(){}
    }
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://EndfieldChargePlus")){Source=new Uri("avares://EndfieldChargePlus/Styles/IndustrialTheme.axaml")});
    }
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var desktop=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async()=>
        {
            int passed=0,calls=0;void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);passed++;Console.WriteLine("PASS: "+label);}
            var held=new TaskCompletionSource<Dictionary<string,object?>>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var data=new HudDataService((_,_,_)=>{Interlocked.Increment(ref calls);return held.Task;});
            var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud,data);
            var config=CustomHudSettings.CreateDefault() with{AutoCycle=false,ActiveProfileId="system.overview"};
            var profile=config.Profiles.First(p=>p.BuiltInKey=="system.overview");config=config with{ActiveProfileId=profile.Id};
            var settings=new AppSettings{CustomHud=config,ShowClock=true,AlwaysVisible=false};hud.ApplySettings(settings);runtime.ApplySettings(settings);
            var root=Path.Combine(Path.GetTempPath(),"IslandPerformance-"+Guid.NewGuid().ToString("N"));
            AssistantIslandWindow? ai=null;MusicIslandWindow? music=null;NotificationIslandWindow? notification=null;
            try
            {
                for(int i=0;i<3;i++)
                {
                    var timer=Stopwatch.StartNew();var presentation=runtime.PreviewActiveAsync();
                    Check(hud.IsVisible&&!held.Task.IsCompleted,$"HUD {i} becomes visible before held hardware finishes");
                    await presentation;Console.WriteLine($"HUD summon {i}: {timer.ElapsedMilliseconds} ms");
                    Check(timer.ElapsedMilliseconds<900,"HUD animation completes without waiting for hardware");
                    timer.Restart();await runtime.BeginAssistantAsync();Console.WriteLine($"mode switch {i}: {timer.ElapsedMilliseconds} ms");
                    Check(timer.ElapsedMilliseconds<700&&!hud.IsVisible,"mode switch does not wait for sampling or 5-second busy loop");runtime.EndAssistant();runtime.Stop();
                }
                held.SetResult(new(){{"cpu.usage",37d},{"gpu.usage",21d},{"memory.usage",42d}});await Task.Delay(60);
                Check(!hud.IsVisible,"late sample cannot resurrect an interrupted HUD");
                Check(calls==1,"multiple native summons share a single hardware read");
                runtime.ApplySettings(settings with{AlwaysVisible=true});var start=Stopwatch.StartNew();runtime.Start();await Task.Delay(400);runtime.Stop();
                Check(hud.IsVisible&&!hud.IsHudBusy,"AlwaysVisible uses compact summon rather than a multi-second introduction");await runtime.BeginAssistantAsync();
                var timer2=Stopwatch.StartNew();ai=new AssistantIslandWindow(new PersonalAssistantStore(Path.Combine(root,"p.dpapi")),new AssistantSession(Path.Combine(root,"s.dpapi")));ai.ShowInput(settings);
                Console.WriteLine($"AI construct/show: {timer2.ElapsedMilliseconds} ms");Check(ai.IsVisible&&timer2.ElapsedMilliseconds<1200,"AI surface opens independently of hardware sampling");ai.HideIsland();
                timer2.Restart();music=new MusicIslandWindow(new HeldMusic());music.ShowMusic(settings);Console.WriteLine($"Music construct/show: {timer2.ElapsedMilliseconds} ms");
                Check(music.IsVisible&&timer2.ElapsedMilliseconds<1200,"music surface opens before slow media metadata");music.HideIsland();
                timer2.Restart();notification=new NotificationIslandWindow();notification.ShowNotification(NotificationCard.Create("qa","fixture","title","body",DateTimeOffset.UtcNow),settings);
                Console.WriteLine($"Notification construct/show: {timer2.ElapsedMilliseconds} ms");Check(notification.IsVisible&&timer2.ElapsedMilliseconds<1200,"notification surface opens without hardware sampling");
                notification.HideIsland();
                await Task.Delay(800);int beforeWarm=calls;
                runtime.Start();await Task.Delay(5400);runtime.Stop();
                Check(calls>=beforeWarm+2&&!hud.IsVisible,"hardware sampling continues at idle cadence while assistant is active without showing HUD");
                int afterStop=calls;await Task.Delay(5200);
                Check(calls==afterStop,"stopping runtime stops periodic background sampling");
                Console.WriteLine($"{passed}/{passed} PASS; native windows, synthetic private data, zero model calls.");
            }
            catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}
            finally{held.TrySetResult(new());runtime.Stop();ai?.Close();music?.Close();notification?.Close();hud.Close();desktop.Shutdown(Environment.ExitCode);}
        });
    }
}
