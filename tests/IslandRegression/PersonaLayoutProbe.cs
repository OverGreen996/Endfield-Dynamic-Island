using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant.Native;
using EndfieldChargePlus.Views;

internal static class PersonaLayoutProbe
{
    internal static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        var count=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);count++;}
        using var http=new HttpClient(new AiBackupProbe.Handler((_,_)=>throw new Exception("FAIL: settings must not call any API")));
        using var service=new NativeAssistantService(AiBackupProbe.Root(),http);
        LocalizationManager.SetLanguage(AppLanguage.TraditionalChinese);
        var window=new AssistantPersonaWindow(service.Personas){ShowActivated=false,ShowInTaskbar=false,Opacity=0};window.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();var root=(Control)window.Content!;
        T Find<T>(string name)where T:Control=>root.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
        void Click(string name){Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Avalonia.Threading.Dispatcher.UIThread.RunJobs();}
        Click("PersonaExample");var name=Find<TextBox>("PersonaName");var character=Find<TextBox>("PersonaCharacter");var rules=Find<TextBox>("PersonaRules");
        Check(character.Text!.Contains("性格底色")&&rules.Text!.Contains("人格使用"),"example has separate character and rules");
        Check(!Find<Button>("PersonaActivate").IsEnabled,"unsaved draft cannot accidentally activate");
        character.Text+="\nsynthetic draft marker";var draft=character.Text;LocalizationManager.SetLanguage(AppLanguage.English);Check(character.Text==draft&&name.Text=="莊方宜","language changes preserve user-authored text");
        var list=Find<ListBox>("PersonaList");list.SelectedIndex=0;Check(character.IsReadOnly&&Find<Button>("PersonaDuplicate").IsEnabled,"default is protected but can be copied");list.SelectedIndex=1;Check(character.Text==draft,"switching list selection preserves unsaved draft");
        Click("PersonaSave");Check(service.Personas.Profiles.Count==2&&service.Personas.Active.Id=="default"&&Find<Button>("PersonaActivate").IsEnabled,"save does not activate the profile");
        Click("PersonaActivate");Check(service.Personas.Active.Name=="莊方宜"&&!Find<Button>("PersonaActivate").IsEnabled,"explicit activation selects saved persona");
        foreach(var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English}){
            LocalizationManager.SetLanguage(language);
            foreach(var width in new[]{700d,1080d}){
                window.Width=width;Avalonia.Threading.Dispatcher.UIThread.RunJobs();root.InvalidateMeasure();root.Measure(new Size(width,790));root.Arrange(new Rect(0,0,width,790));
                foreach(var input in new[]{name,character,rules}){
                    var point=input.TranslatePoint(new Point(0,0),root)!.Value;Check(input.Bounds.Width>180&&point.X>=0&&point.X+input.Bounds.Width<=width,"editor must fit within window");
                }
                foreach(var button in new[]{"PersonaSave","PersonaActivate","PersonaDelete","PersonaDuplicate"}.Select(Find<Button>)){
                    var point=button.TranslatePoint(new Point(0,0),root)!.Value;Check(point.X>=0&&point.X+button.Bounds.Width<=width&&point.Y>=0&&point.Y+button.Bounds.Height<=790,"footer actions remain visible");
                }
                Directory.CreateDirectory("artifacts/native-qa");using var image=new RenderTargetBitmap(new PixelSize((int)width,790));image.Render(root);image.Save($"artifacts/native-qa/personas-{(language==AppLanguage.English?"en":"zh-TW")}-{width}.png");
            }
        }
        Click("PersonaDuplicate");Check(name.Text!.Contains("copy")&&character.Text==draft,"duplicate starts editable copy with same profile fields");
        window.Close();Check(window.IsVisible&&Find<Button>("PersonaDiscard").IsVisible,"closing with unsaved draft requires explicit discard");Click("PersonaKeepEditing");Check(window.IsVisible,"keep editing retains drafts");Click("PersonaDiscard");Check(!window.IsVisible&&character.Text=="","discard closes and clears private editor buffers");
        var pool=new GeminiPoolWindow(service){ShowActivated=false,ShowInTaskbar=false,Opacity=0};pool.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();var poolRoot=(Control)pool.Content!;
        var keys=poolRoot.GetVisualDescendants().OfType<TextBox>().ToArray();Check(keys.Length==4&&keys.All(k=>k.PasswordChar=='●'),"four extra accounts are masked; primary remains in existing settings");keys[0].Text="synthetic unsaved key";
        foreach(var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English}){
            LocalizationManager.SetLanguage(language);Check(keys[0].Text=="synthetic unsaved key","translation preserves account draft");
            foreach(var width in new[]{460d,780d}){
                pool.Width=width;Avalonia.Threading.Dispatcher.UIThread.RunJobs();poolRoot.InvalidateMeasure();poolRoot.Measure(new Size(width,810));poolRoot.Arrange(new Rect(0,0,width,810));
                foreach(var key in keys){var point=key.TranslatePoint(new Point(0,0),poolRoot)!.Value;Check(key.Bounds.Width>150&&point.X>=0&&point.X+key.Bounds.Width<=width,"key inputs stay inside narrow window");}
                if(width==780){using var image=new RenderTargetBitmap(new PixelSize(780,810));image.Render(poolRoot);image.Save($"artifacts/native-qa/gemini-pool-{(language==AppLanguage.English?"en":"zh-TW")}.png");}
            }
        }
        pool.Close();Check(keys.All(k=>k.Text==""),"closed settings clear credentials");
        Console.WriteLine($"{count}/{count} native persona and Gemini rotation layout checks PASS; bilingual wide/narrow UI, synthetic data, zero API calls.");
    }
}
