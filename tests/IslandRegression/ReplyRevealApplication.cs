using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Views;

public sealed class ReplyRevealApplication:Application
{
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var desktop=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async()=>{
            var passed=0;
            void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);passed++;Console.WriteLine("PASS: "+label);}
            var root=Path.Combine(Path.GetTempPath(),"reply-reveal-"+Guid.NewGuid().ToString("N"));
            var session=new AssistantSession(Path.Combine(root,"session.dpapi"));
            var personal=new PersonalAssistantStore(Path.Combine(root,"personal.dpapi"));
            AssistantReply Reply(string text)=>new(text,"model","test",true,[new("S1","synthetic source","https://example.org/test",null,null,null)],null,null);
            var text="快速逐字回覆，保留完整的 🐳、👨‍👩‍👧‍👦、cafe\u0301 與繁體中文。"+new string('測',440);
            var state=new ReplyTextReveal(text);
            Check(state.VisibleText=="快"&&!state.IsComplete,"new reply begins with one text element");
            state.Advance(TimeSpan.FromSeconds(5));
            Check(state.VisibleText.Length<30,"UI stall does not dump the full reply");
            var unicode="👨‍👩‍👧‍👦e\u0301🐳繁體中文";
            var cursor=new ReplyTextReveal(unicode);
            var boundaries=System.Globalization.StringInfo.ParseCombiningCharacters(unicode).Append(unicode.Length).ToHashSet();
            var intact=true;
            while(!cursor.IsComplete){intact&=boundaries.Contains(cursor.VisibleText.Length);cursor.Advance(TimeSpan.FromMilliseconds(24));}
            Check(intact&&cursor.VisibleText==unicode,"emoji families and combining accents stay intact");
            var tiny=new ReplyTextReveal("好");Check(tiny.IsComplete&&tiny.VisibleText=="好","single-character replies complete immediately");
            for(int i=0;i<8;i++)session.Append("舊問題 "+i,Reply("舊對話 "+i+new string('舊',170)));
            var window=new AssistantIslandWindow(personal,session){Topmost=false,ShowActivated=false,ShowInTaskbar=false,Opacity=.01};
            SelectableTextBlock LastText()=>window.FindControl<StackPanel>("ConversationPanel")!.GetVisualDescendants().OfType<SelectableTextBlock>().Last();
            DispatcherTimer Timer()=>(DispatcherTimer)typeof(AssistantIslandWindow).GetField("_replyTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            try {
                window.AppendAndRevealReply("新問題",Reply(text));
                Check(new AssistantSession(Path.Combine(root,"session.dpapi")).Turns[^1].Reply.text==text,"complete reply encrypted before any animation frame");
                Check(!Timer().IsEnabled,"hidden assistant uses no reveal timer");
                window.Show();
                var firstFrameDeadline=DateTime.UtcNow.AddSeconds(3);
                while(LastText().Text!.Length<=1&&DateTime.UtcNow<firstFrameDeadline)await Task.Delay(20);
                var early=LastText().Text!;
                Check(early.Length>1&&early.Length<text.Length&&text.StartsWith(early),"native timer shows an intermediate prefix instead of full text");
                Check(!window.FindControl<StackPanel>("ConversationPanel")!.GetVisualDescendants().OfType<Expander>().Any(),"sources remain internal and no source list appears in the conversation");
                var body=(Control)window.Content!;Directory.CreateDirectory("artifacts/native-qa");
                using(var frame=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Width),(int)Math.Ceiling(window.Height)))){frame.Render(body);frame.Save("artifacts/native-qa/reply-reveal-intermediate.png");}
                Check(body.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t=>t.Text?.StartsWith("舊對話 0")==true),"existing history stays fully visible without replay");
                window.HideIsland();var paused=LastText().Text;
                await Task.Delay(180);
                Check(!Timer().IsEnabled&&LastText().Text==paused,"notification preemption pauses text and stops timer");
                LocalizationManager.SetLanguage(AppLanguage.English);
                Check(LastText().Text==paused,"language refresh preserves the presentation cursor");
                window.Show();await Task.Delay(160);
                var resumed=LastText().Text!;
                Check(resumed.Length>paused!.Length&&resumed.Length<text.Length,"return resumes from the preserved prefix");
                var scroll=window.FindControl<ScrollViewer>("ReplyScroll")!;
                Check(scroll.Extent.Height>scroll.Viewport.Height+100,"scroll test contains overflowing real conversation");
                scroll.Offset=new Vector(0,0);await Task.Delay(180);
                Check(scroll.Offset.Y<5,"reading older messages is not forced back to the bottom");
                var sizes=new HashSet<int>();var deadline=DateTime.UtcNow.AddSeconds(6);
                while(Timer().IsEnabled&&DateTime.UtcNow<deadline){sizes.Add(LastText().Text!.Length);await Task.Delay(35);}
                Check(sizes.Count>3&&LastText().Text==text&&!Timer().IsEnabled,"multiple real frames finish with exact complete text and release timer");
                window.AppendAndRevealReply("下一個問題",Reply(new string('新',300)));
                Check(session.Turns[^2].Reply.text==text&&LastText().Text!.Length<300,"next answer keeps previous reply complete and starts a fresh reveal");
                typeof(AssistantIslandWindow).GetMethod("NewConversation",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                await Task.Delay(100);
                Check(session.Turns.Count==0&&!Timer().IsEnabled&&window.FindControl<StackPanel>("ConversationPanel")!.Children.Count==0,"new conversation cannot receive stale animation frames");
                window.AppendAndRevealReply("關閉測試",Reply(new string('關',300)));window.Close();
                await Task.Delay(80);
                Check(!Timer().IsEnabled&&new AssistantSession(Path.Combine(root,"session.dpapi")).Turns[^1].Reply.text==new string('關',300),"closing stops presentation without truncating saved reply");
                Console.WriteLine($"{passed}/{passed} PASS; real native reply frames, pause/resume, Unicode and encrypted synthetic history; zero API calls.");
            } catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}
            finally{window.Close();desktop.Shutdown(Environment.ExitCode);}
        });
    }
}
