using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

// Manual production-window QA. Only explicit Send invokes the configured local Hub.
// Session and personal memory use isolated temporary paths, never the user's files.
public sealed class PastedImageLiveApplication : Application
{
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string root=Path.Combine(Path.GetTempPath(),"IslandImageLive-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window=new AssistantIslandWindow(new PersonalAssistantStore(Path.Combine(root,"personal.dpapi")),new AssistantSession(Path.Combine(root,"session.dpapi")))
                {Title="AI 貼圖隔離驗收",ShowInTaskbar=true};
            desktop.MainWindow=window;window.ShowInput(new AppSettings());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
