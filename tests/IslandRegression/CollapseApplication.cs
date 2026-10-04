using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

public sealed class CollapseApplication:Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        string root=Path.Combine(Path.GetTempPath(),"IslandCollapse-"+Guid.NewGuid().ToString("N"));
        var session=new AssistantSession(Path.Combine(root,"s.dpapi"));
        for(int i=0;i<5;i++)session.Append("請保留草稿與對話",new AssistantReply("收起時文字先淡出，面板往上收攏。這是合成驗收資料，沒有使用 Gemini。","local",null,false,null,null,null));
        var ai=new AssistantIslandWindow(new PersonalAssistantStore(Path.Combine(root,"p.dpapi")),session){Title="AI v19 動畫驗收"};desktop.MainWindow=ai;
        ai.ShowInput(new AppSettings());
        if(desktop.Args?.Contains("--collapse-ui")==true)
        {
            var button=new Button{Content="重新開啟動畫驗收頁",Margin=new Thickness(24)};button.Click+=(_,_)=>ai.ShowInput(new AppSettings());
            var control=new Window{Title="v19 動畫驗收控制",Width=400,Height=150,Position=new PixelPoint(500,740),Content=button};desktop.MainWindow=control;control.Show();
            ai.FindControl<TextBox>("InputBox")!.Text="動畫驗收草稿，請勿送出";
            return;
        }
        Dispatcher.UIThread.Post(async()=>
        {
            int passed=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
            var body=ai.FindControl<Border>("Island")!;var surface=ai.FindControl<Border>("CloseSurface")!;
            var content=ai.FindControl<Grid>("AssistantContent")!;var input=ai.FindControl<TextBox>("InputBox")!;
            string output=Path.Combine(Environment.CurrentDirectory,"outputs","Island-v19-Animation");Directory.CreateDirectory(output);
            int hidden=0;ai.IslandHidden+=()=>hidden++;
            try
            {
                input.Text="未送出的測試草稿";await Task.Delay(350);
                bool enabled=new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
                foreach(int cap in new[]{360,620})
                {
                    ai.ApplyViewportLayout(644,cap);await Task.Delay(120);
                    double fullWidth=body.Bounds.Width,fullHeight=body.Bounds.Height,font=input.FontSize;var bounds=content.Bounds;
                    Save("height"+cap+"-00",ai,output);
                    var clock=Stopwatch.StartNew();var close=ai.HideAnimatedAsync();
                    if(enabled)
                    {
                        Check(ai.IsVisible&&surface.IsVisible,"close begins without instant disappearance "+cap);
                        Check(ReferenceEquals(close,ai.HideAnimatedAsync()),"repeated close shares running animation "+cap);
                        Check((GetWindowLongPtr(ai.TryGetPlatformHandle()!.Handle,-20).ToInt64()&0x20)!=0,"closing window passes mouse through "+cap);
                        var samples=new List<object>();double previous=fullHeight;
                        for(int i=1;i<=6&&!close.IsCompleted;i++)
                        {
                            await Task.Delay(i==1?20:30);if(close.IsCompleted)break;
                            var clip=(RectangleGeometry)body.Clip!;
                            Check(surface.Height<=previous+.1&&surface.Height>0&&surface.Width<=fullWidth+.1,"surface shrinks monotonically "+cap+" / "+i);
                            Check(content.Bounds==bounds&&input.FontSize==font&&body.RenderTransform is null,"text geometry stays unchanged "+cap+" / "+i);
                            Check(Math.Abs(clip.Rect.Width-surface.Width)<.1&&Math.Abs(clip.Rect.Height-surface.Height)<.1,"clip matches visible outline "+cap+" / "+i);
                            previous=surface.Height;samples.Add(new{ms=clock.Elapsed.TotalMilliseconds,width=surface.Width,height=surface.Height,contentOpacity=content.Opacity,windowOpacity=ai.Opacity});
                            Save("height"+cap+"-"+i.ToString("00"),ai,output);
                        }
                        File.WriteAllText(Path.Combine(output,"height"+cap+".json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
                    }
                    await close;Check(!ai.IsVisible&&body.Clip is null&&!surface.IsVisible&&content.Opacity==1,"close ends hidden and resets appearance "+cap);
                    Check(input.Text=="未送出的測試草稿"&&session.Turns.Count==5,"draft and conversation survive "+cap);
                    ai.ShowInput(new AppSettings());await Task.Delay(100);
                    Check(ai.IsVisible&&ai.Opacity==1&&(GetWindowLongPtr(ai.TryGetPlatformHandle()!.Handle,-20).ToInt64()&0x20)==0,"reopen restores input and opacity "+cap);
                }
                if(enabled)
                {
                    int before=hidden;var interrupted=ai.HideAnimatedAsync();await Task.Delay(60);ai.ShowInput(new AppSettings());await interrupted;await Task.Delay(350);
                    Check(ai.IsVisible&&ai.Opacity==1&&hidden==before&&body.Clip is null&&!surface.IsVisible,"reopen cancels old close without a late hide");
                    Check(content.Opacity==1&&content.RenderTransform is null,"cancel clears transient text animation");
                    for(int i=0;i<3;i++){var task=ai.HideAnimatedAsync();await Task.Delay(25);ai.ShowInput(new AppSettings());await task;}
                    await Task.Delay(350);Check(ai.IsVisible&&!surface.IsVisible&&input.Text=="未送出的測試草稿","rapid reopen remains usable");
                }
                ai.HideIsland();Check(!ai.IsVisible&&body.Clip is null,"immediate emergency hide resets state");
                Console.WriteLine($"{passed}/{passed} PASS; OS animations enabled={enabled}; source native animation frames exported.");
                desktop.Shutdown(0);
            }
            catch(Exception ex){Console.WriteLine(ex);desktop.Shutdown(1);}
            finally{ai.Close();}
        });base.OnFrameworkInitializationCompleted();
    }
    private static void Save(string name,Window window,string output)
    {
        var content=(Control)window.Content!;double scale=window.RenderScaling;
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width*scale),(int)Math.Ceiling(window.Bounds.Height*scale)),new Vector(96*scale,96*scale));bitmap.Render(content);bitmap.Save(Path.Combine(output,name+".png"));
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
}
