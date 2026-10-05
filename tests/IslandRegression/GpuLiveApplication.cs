using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

public sealed class GpuLiveApplication : Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        var hud=new HudWindow();desktop.MainWindow=hud;
        Dispatcher.UIThread.Post(async()=>
        {
            try
            {
                using var hub=new VariableHub();var settings=CustomHudSettings.CreateDefault();
                var profile=settings.Profiles.Single(x=>x.BuiltInKey=="system.overview");
                var values=await hub.SnapshotAsync(settings,HudProfileRenderer.GetRequiredVariables(profile));
                var data=HudProfileRenderer.Render(profile,values);
                hud.ApplySettings(new AppSettings{AlwaysVisible=true,GlobalScale=1,ShowClock=true});
                await hud.ShowPersistentAsync(data);await Task.Delay(1000);
                values=await hub.SnapshotAsync(settings,HudProfileRenderer.GetRequiredVariables(profile));
                data=HudProfileRenderer.Render(profile,values);hud.UpdatePersistent(data);await Task.Delay(400);
                foreach(var metric in data.Metrics!)Console.WriteLine($"{metric.Label}: {metric.Value}; {metric.Detail}");
                var content=(Control)hud.Content!;
                using var bitmap=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(hud.Bounds.Width),(int)Math.Ceiling(hud.Bounds.Height)),new Vector(96,96));
                bitmap.Render(content);
                Directory.CreateDirectory("outputs/Gpu-live");bitmap.Save("outputs/Gpu-live/gpu-hud.png");
                var band=hud.FindControl<Grid>("OverviewHost")!;var size=band.Bounds.Size;
                band.Margin=default;band.Measure(size);band.Arrange(new Rect(size));
                using var detail=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width*2),(int)Math.Ceiling(size.Height*2)),new Vector(192,192));
                detail.Render(band);detail.Save("outputs/Gpu-live/gpu-band.png");
                Environment.ExitCode=0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
            finally{desktop.Shutdown(Environment.ExitCode);}
        });
        base.OnFrameworkInitializationCompleted();
    }
}
