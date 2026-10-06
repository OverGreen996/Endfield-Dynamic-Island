using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;
public sealed class IndustrialUiApplication:Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://EndfieldChargePlus")){Source=new Uri("avares://EndfieldChargePlus/Styles/IndustrialTheme.axaml")});RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        LocalizationManager.SetLanguage(AppLanguage.TraditionalChinese);
        var hud=new HudWindow{ShowInTaskbar=true,Title="v20 效能總覽驗收"};var runtime=new CustomHudRuntime(hud);
        var settings=new SettingsWindow(new AppSettings(),hud,runtime,saveSettings:_=>Console.WriteLine("Fixture preferences retained in isolated process only."),applyStartup:_=>Console.WriteLine("Fixture startup registration skipped.")){Title="v20 主介面驗收"};desktop.MainWindow=settings;settings.Show();
        string folder=Path.Combine(Environment.CurrentDirectory,"outputs","Island-v20-Design");Directory.CreateDirectory(folder);
        if(desktop.Args?.Contains("--industrial-ui")==true)
        {
            var temp=Path.Combine(Path.GetTempPath(),"IslandManual-"+Guid.NewGuid().ToString("N"));
            var session=new AssistantSession(Path.Combine(temp,"s.dpapi"));
            for(int i=0;i<15;i++)session.Append("驗收訊息 "+i,new AssistantReply("這是隔離測試對話，AI 保留青藍色外圈，你的頭像使用黃色外圈。這一段長文字用來確認換行和右側捲軸不會壓到頭像。","local",null,false,null,null,null));
            var ai=new AssistantIslandWindow(new PersonalAssistantStore(Path.Combine(temp,"p.dpapi")),session){Title="v20 AI 介面驗收"};
            var controls=new StackPanel{Spacing=8,Margin=new Thickness(16)};
            var open=new Button{Content="開啟 AI 驗收"};open.Click+=(_,_)=>{hud.Hide();ai.ShowInput(new AppSettings());};settings.AssistantRequested+=()=>{hud.Hide();ai.ShowInput(new AppSettings());};
            var overview=new Button{Content="開啟效能總覽"};overview.Click+=async(_,_)=>
            {
                ai.HideIsland();var profile=CustomHudSettings.CreateDefaultProfiles().Single(p=>p.BuiltInKey=="system.overview");
                using var hub=new VariableHub();var config=CustomHudSettings.CreateDefault();var values=await hub.SnapshotAsync(config,HudProfileRenderer.GetRequiredVariables(profile));
                hud.ApplySettings(new AppSettings{AlwaysVisible=true,GlobalScale=1,ShowClock=true,ShowDate=true});await hud.ShowPersistentAsync(HudProfileRenderer.Render(profile,values));
            };
            controls.Children.Add(open);controls.Children.Add(overview);
            var panel=new Window{Title="v20 驗收控制",Width=300,Height=180,Position=new PixelPoint(550,700),Content=controls};panel.Show();
            desktop.Exit+=(_,_)=>{ai.Close();hud.Close();runtime.Dispose();};
            base.OnFrameworkInitializationCompleted();return;
        }
        Dispatcher.UIThread.Post(async()=>
        {
            int passed=0;void Check(bool value,string text){if(!value)throw new Exception("FAIL: "+text);passed++;Console.WriteLine("PASS: "+text);}
            var issues=new HashSet<string>();var tabs=settings.FindControl<TabControl>("ModuleTabs")!;
            var temp=Path.Combine(Path.GetTempPath(),"IslandIndustrial-"+Guid.NewGuid().ToString("N"));
            var store=new PersonalAssistantStore(Path.Combine(temp,"p.dpapi"));PersonalTestData.Memory(store,"我喜歡安靜的介面","介面偏好");PersonalTestData.Remind(store,"驗收",1800);
            var session=new AssistantSession(Path.Combine(temp,"s.dpapi"));
            for(int i=0;i<15;i++)session.Append("請保留中文原文，不要因為切換介面語言翻譯我。",new AssistantReply("這是合成測試，保留原訊息。這段較長的內容用來驗證捲軸與左右頭像不會相互重疊。","local",null,false,null,null,null));
            var ai=new AssistantIslandWindow(store,session);var palace=new MemoryPalaceWindow(store);
            try
            {
                foreach(var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English})
                {
                    LocalizationManager.SetLanguage(language);typeof(SettingsWindow).GetMethod("ApplyLocalization",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(settings,null);
                    foreach(int index in new[]{0,1,2,3,4,5})
                    {
                        tabs.SelectedIndex=index;await Task.Delay(100);Save(settings,folder,$"settings-{language}-{index}");
                        if(language==AppLanguage.English)
                            foreach(var node in ((TabItem)tabs.Items[index]!).GetLogicalDescendants())
                            {
                                string? value=node is TextBlock text?text.Text:node is ContentControl c?c.Content as string:node is TextBox box?box.Watermark as string:null;
                                if(value is not null&&!value.StartsWith("© 2026 GlacierGlimmer_")&&Regex.IsMatch(value,"[\\p{IsCJKUnifiedIdeographs}]"))issues.Add(value);
                            }
                    }
                    ai.ShowInput(new AppSettings());await Task.Delay(150);Save(ai,folder,"chat-"+language);
                    Check(ai.FindControl<Button>("SendButton")!.Content?.ToString()==(language==AppLanguage.English?"Send":"送出"),"assistant send label follows language "+language);
                    Check(session.Turns[0].Question.StartsWith("請保留"),"language change preserves original chat "+language);ai.HideIsland();
                    palace.Show();await Task.Delay(100);Save(palace,folder,"memory-"+language);
                    var palaceTabs=palace.GetLogicalDescendants().OfType<TabControl>().Single();palaceTabs.SelectedIndex=1;await Task.Delay(50);Save(palace,folder,"reminders-"+language);
                    if(language==AppLanguage.English) {
                        var metadata=((TabItem)palaceTabs.Items[1]!).GetLogicalDescendants().OfType<TextBlock>().Where(t=>t.Text?.Contains(" · ID ")==true).ToArray();
                        Check(metadata.Length>0&&metadata.All(t=>!Regex.IsMatch(t.Text!,"[\\p{IsCJKUnifiedIdeographs}]")),"generated reminder metadata is English; original memory quotes stay intact");
                    }
                    palaceTabs.SelectedIndex=0;palace.Hide();
                }
                File.WriteAllText(Path.Combine(folder,"untranslated.json"),JsonSerializer.Serialize(issues,new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine("Untranslated static settings strings: "+issues.Count);
                Check(issues.Count==0,"all six English settings tabs contain translated static UI");
                settings.MinWidth=0;settings.Width=720;tabs.SelectedIndex=0;await Task.Delay(150);Save(settings,folder,"settings-compact-English");
                File.WriteAllText(Path.Combine(folder,"compact-bounds.json"),JsonSerializer.Serialize(new{width=settings.Width,window=settings.Bounds.ToString(),client=settings.ClientSize.ToString(),root=((Control)settings.Content!).Bounds.ToString(),rootDesired=((Control)settings.Content!).DesiredSize.ToString(),tabs=tabs.Bounds.ToString(),descendants=tabs.GetVisualDescendants().OfType<Control>().Take(20).Select(c=>new{type=c.GetType().Name,bounds=c.Bounds.ToString(),desired=c.DesiredSize.ToString()})},new JsonSerializerOptions{WriteIndented=true}));
                Check(tabs.Classes.Contains("settingsCompact"),"compact navigation responds to narrow screen");
                Check(tabs.Bounds.X>=0&&tabs.Bounds.Right<=settings.ClientSize.Width,"narrow settings content stays inside window");
                foreach(var buttonName in new[]{"LanguageChineseBtn","LanguageEnglishBtn","OpenSettingsFolderBtn","SaveBtn"}){var button=settings.FindControl<Button>(buttonName)!;var point=button.TranslatePoint(default,(Control)settings.Content!)!.Value;Check(point.X>=0&&point.X+button.Bounds.Width<=settings.ClientSize.Width&&point.Y+button.Bounds.Height<=tabs.Bounds.Y,"narrow header fits above module content: "+buttonName);}
                settings.Width=1120;
                var editor=palace.GetLogicalDescendants().OfType<TextBox>().First();editor.Text="尚未儲存的記憶草稿";LocalizationManager.SetLanguage(AppLanguage.TraditionalChinese);LocalizationManager.SetLanguage(AppLanguage.English);
                Check(editor.Text=="尚未儲存的記憶草稿"&&((bool)typeof(MemoryPalaceWindow).GetField("_dirty",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(palace)!),"language switching preserves unsaved memory editor");
                var profile=CustomHudSettings.CreateDefaultProfiles().Single(p=>p.BuiltInKey=="system.overview");var requested=HudProfileRenderer.GetRequiredVariables(profile);
                using var hub=new VariableHub();var configuration=CustomHudSettings.CreateDefault();
                var values=await hub.SnapshotAsync(configuration,requested);await Task.Delay(1100);values=await hub.SnapshotAsync(configuration,requested);
                foreach(var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English})
                {
                    LocalizationManager.SetLanguage(language);hud.ApplySettings(new AppSettings{AlwaysVisible=true,GlobalScale=1,ShowClock=true,ShowDate=true});
                    var data=HudProfileRenderer.Render(profile,values);await hud.ShowPersistentAsync(data);await Task.Delay(150);Save(hud,folder,"overview-"+language);
                    Check(data.Metrics?.Count==4,"one HUD contains all four metrics "+language);
                    var overview=hud.FindControl<Grid>("OverviewHost")!;Check(overview.IsVisible&&overview.Children.Count==4,"four live native metric cells "+language);
                    var expected=new[]{"CPU","GPU","VRAM","RAM"};
                    var labels=overview.GetLogicalDescendants().OfType<TextBlock>().Where(c=>expected.Contains(c.Text)).ToArray();
                    Check(labels.Length==4,"all four metric names remain explicit in "+language);
                    using(var pixels=SKBitmap.Decode(Path.Combine(folder,"overview-"+language+"-band.png")))
                    foreach(var label in labels)
                    {
                        var point=label.TranslatePoint(default,overview)!.Value;int painted=0;
                        for(int y=(int)(point.Y*2);y<Math.Min(pixels.Height,(point.Y+label.Bounds.Height)*2);y++)
                        for(int x=(int)(point.X*2);x<Math.Min(pixels.Width,(point.X+label.Bounds.Width)*2);x++)
                        {var color=pixels.GetPixel(x,y);if(color.Red>140&&color.Green>140&&color.Blue>120&&color.Alpha>128)painted++;}
                        Check(painted>10,"name actually paints glyphs in component render: "+label.Text);
                        Check(label.DesiredSize.Width<=label.Bounds.Width+.1,"metric name fits without truncation: "+label.Text);
                    }
                    var clock=hud.FindControl<TextBlock>("ClockText")!;var overviewStart=overview.TranslatePoint(default,(Control)hud.Content!)!.Value;
                    var clockEnd=clock.TranslatePoint(new Point(0,clock.Bounds.Height),(Control)hud.Content!)!.Value;
                    var clockStart=clock.TranslatePoint(default,(Control)hud.Content!)!.Value;
                    Check(clockStart.X>=overviewStart.X+overview.Bounds.Width&&clock.FontSize<=10,"quiet clock stays outside metric columns "+language);
                    foreach(var metric in overview.GetLogicalDescendants().OfType<TextBlock>())
                    {var point=metric.TranslatePoint(default,overview)!.Value;Check(point.X>=0&&point.Y>=0&&point.X+metric.Bounds.Width<=overview.Bounds.Width+.1&&point.Y+metric.Bounds.Height<=overview.Bounds.Height+.1,"metric text stays inside reserved HUD band: "+metric.Text);}
                    File.WriteAllText(Path.Combine(folder,"overview-bounds-"+language+".json"),JsonSerializer.Serialize(overview.GetLogicalDescendants().OfType<Control>().Select(c=>new{type=c.GetType().Name,text=(c as TextBlock)?.Text,bounds=c.Bounds.ToString(),point=c.TranslatePoint(new Point(0,0),(Control)hud.Content!)?.ToString(),desired=c.DesiredSize.ToString(),opacity=c.Opacity}),new JsonSerializerOptions{WriteIndented=true}));
                    File.WriteAllText(Path.Combine(folder,"live-metrics-"+language+".json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));await hud.HideAnimatedAsync();
                }
                foreach(double size in new[]{.4,.8,1d,1.4})
                {
                    hud.ApplySettings(new AppSettings{AlwaysVisible=true,GlobalScale=size,ShowClock=true,ShowDate=true});
                    await hud.ShowPersistentAsync(HudProfileRenderer.Render(profile,values));await Task.Delay(50);
                    var screen=hud.Screens.ScreenFromWindow(hud)!;double scaling=screen.Scaling;
                    double width=560*size*scaling,height=60*size*scaling;
                    double left=hud.Position.X+(hud.Width*scaling-width)/2,top=hud.Position.Y+(hud.Height*scaling-height)/2;
                    Check(hud.IsPointInsideInteractiveHud(new PixelPoint((int)(left+width/2),(int)(top+height/2))),"scaled overview body accepts input: "+size);
                    Check(!hud.IsPointInsideInteractiveHud(new PixelPoint((int)(left+2),(int)(top+2))),"scaled overview transparent corner passes through: "+size);
                    Check(!hud.IsPointInsideInteractiveHud(new PixelPoint((int)(left+width+8),(int)(top+height/2))),"scaled overview outside edge passes through: "+size);
                    Check(Math.Abs(hud.FindControl<Border>("Pill")!.Height-60)<.1,"overview retains original capsule height: "+size);
                    await hud.HideAnimatedAsync();
                }
                Check(requested.Contains("gpu.dedicated_used_bytes")&&requested.Contains("memory.total_bytes")&&requested.Contains("cpu.usage"),"shared snapshot requests correct telemetry");
                Console.WriteLine($"{passed}/{passed} PASS; native renders exported; real local telemetry sampled; no model calls.");desktop.Shutdown(0);
            }
            catch(Exception ex){Console.WriteLine(ex);desktop.Shutdown(1);}
            finally{ai.Close();palace.Close();hud.Close();runtime.Dispose();settings.Close();}
        });base.OnFrameworkInitializationCompleted();
    }
    private static void Save(Window window,string folder,string name)
    {
        var content=(Control)window.Content!;double scale=window.RenderScaling;
        using var image=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width*scale),(int)Math.Ceiling(window.Bounds.Height*scale)),new Vector(96*scale,96*scale));image.Render(content);image.Save(Path.Combine(folder,name+".png"));
        if(name.StartsWith("overview-"))
        {
            var band=window.FindControl<Grid>("OverviewHost")!;
            var margin=band.Margin;var size=band.Bounds.Size;band.Margin=default;band.Measure(size);band.Arrange(new Rect(size));
            using var detail=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(band.Bounds.Width*2),(int)Math.Ceiling(band.Bounds.Height*2)),new Vector(192,192));
            detail.Render(band);detail.Save(Path.Combine(folder,name+"-band.png"));
            band.Margin=margin;window.UpdateLayout();
        }
    }
}
