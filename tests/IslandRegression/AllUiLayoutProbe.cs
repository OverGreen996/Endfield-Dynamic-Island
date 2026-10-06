using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Views;
using SkiaSharp;
public static class AllUiLayoutProbe
{
    public static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        int passed=0;void Check(bool value,string text){if(!value)throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text);passed++;}
        var ai=new AssistantIslandWindow();var body=(Control)ai.Content!;
        var panel=ai.FindControl<StackPanel>("ConversationPanel")!;panel.Children.Clear();
        for(int i=0;i<12;i++)panel.Children.Add(new ConversationMessageRow(i%2==0,ConversationMessageRow.MessageText(string.Concat(Enumerable.Repeat("介面驗收文字，不會送出模型。",8)),i%2==0)));
        ai.FindControl<Border>("ConversationFrame")!.IsVisible=ai.FindControl<ScrollViewer>("ReplyScroll")!.IsVisible=true;
        ai.FindControl<TextBlock>("StatusText")!.Text=string.Concat(Enumerable.Repeat("這是長狀態訊息，需要安全換行。",8));
        foreach(double width in new[]{280d,420d,660d,840d})foreach(double cap in new[]{240d,360d,620d})foreach(bool longInput in new[]{false,true})
        {
            ai.FindControl<TextBox>("InputBox")!.Text=longInput?string.Concat(Enumerable.Repeat("驗收長輸入，需要換行，保留送出按鈕。\n",20)):"介面驗收";
            ai.ApplyViewportLayout(width,cap);
            body.Measure(new Size(ai.Width,ai.Height));body.Arrange(new Rect(0,0,ai.Width,ai.Height));
            foreach(string id in new[]{"BodyHeader","InputBox","FooterGrid","ModeCombo","SendButton"})
            {
                var c=ai.FindControl<Control>(id)!;var p=c.TranslatePoint(new Point(0,0),body)!.Value;
                Check(p.X>=8&&p.Y>=8&&p.X+c.Bounds.Width<=ai.Width-8+.1&&p.Y+c.Bounds.Height<=ai.Height-8+.1,$"AI {id} inside body w={width} cap={cap} input={longInput}, rect={p.X:F1},{p.Y:F1},{c.Bounds.Width:F1},{c.Bounds.Height:F1}");
            }
            var input=ai.FindControl<TextBox>("InputBox")!;var footer=ai.FindControl<Grid>("FooterGrid")!;
            Check(input.Bounds.Bottom<=footer.Bounds.Top+.1,"AI input and footer do not overlap");
            if(width==660&&cap==620&&!longInput){using var image=new RenderTargetBitmap(new PixelSize((int)ai.Width,(int)ai.Height));image.Render(body);image.Save(Path.Combine(Environment.CurrentDirectory,"outputs","Island-v17-AI-Layout.png"));}
        }
        ai.Close();
        var notice=new NotificationIslandWindow();var noticeBody=(Control)notice.Content!;
        foreach(double width in new[]{320d,560d})foreach(bool pinned in new[]{false,true})
        {
            double height=106;
            notice.Width=width;notice.Height=height;notice.ApplyCompactLayout(width);
            notice.FindControl<TextBlock>("AppLabel")!.Text="較長的應用程式名稱";
            notice.FindControl<TextBlock>("TimeLabel")!.Text=pinned?"已固定 · 再點收起":"停留中 · 移開後收起";
            notice.FindControl<TextBlock>("TitleLabel")!.Text="較長的通知標題以省略號處理，不應超出邊框";
            var label=notice.FindControl<TextBlock>("BodyLabel")!;label.Text=string.Concat(Enumerable.Repeat("通知正文測試，需要正常換行。",20));label.MaxLines=pinned?0:1;
            noticeBody.Measure(new Size(width,height));noticeBody.Arrange(new Rect(0,0,width,height));
            foreach(var id in new[]{"AppLabel","TimeLabel","TitleLabel","BodyScroll"}){var c=notice.FindControl<Control>(id)!;var p=c.TranslatePoint(new Point(0,0),noticeBody)!.Value;Check(p.X>=8&&p.Y>=8&&p.X+c.Bounds.Width<=width-8+.1&&p.Y+c.Bounds.Height<=height-8+.1,$"notification {id} inside {width}x{height}, pinned={pinned}");}
        }
        notice.Close();
        var tray=new TrayMenuWindow();var root=(Control)tray.Content!;root.Measure(new Size(tray.Width,tray.Height));root.Arrange(new Rect(0,0,tray.Width,tray.Height));
        foreach(var id in new[]{"MenuAssistant","MenuMusic","MenuPreview","MenuSettings","MenuExit"}){var c=tray.FindControl<Control>(id)!;var p=c.TranslatePoint(new Point(0,0),root)!.Value;Check(p.X>=0&&p.Y>=0&&p.X+c.Bounds.Width<=tray.Width&&p.Y+c.Bounds.Height<=tray.Height,$"tray {id} within window");}tray.Close();
        var hud=new HudWindow();using var runtime=new EndfieldChargePlus.Customization.CustomHudRuntime(hud);
        var settings=new EndfieldChargePlus.Settings.SettingsWindow(new EndfieldChargePlus.Settings.AppSettings(),hud,runtime);settings.MinWidth=0;settings.MinHeight=0;
        foreach(double width in new[]{420d,640d,940d,1120d})
        {
            var content=(Control)settings.Content!;content.Measure(new Size(width,480));content.Arrange(new Rect(0,0,width,480));
            foreach(var id in new[]{"LanguageChineseBtn","LanguageEnglishBtn","OpenSettingsFolderBtn","SaveBtn"}){var c=settings.FindControl<Control>(id)!;var p=c.TranslatePoint(new Point(0,0),content)!.Value;Check(p.X>=0&&p.Y>=0&&p.X+c.Bounds.Width<=width+.1&&p.Y+c.Bounds.Height<=480+.1,$"settings header {id} in viewport {width}");}
        }
        settings.Close();hud.Close();
        string qaRoot=Path.Combine(Path.GetTempPath(),"PalaceLayout-"+Guid.NewGuid().ToString("N"));var store=new PersonalAssistantStore(Path.Combine(qaRoot,"personal.dpapi"));PersonalTestData.Memory(store,"我喜歡中文分類","介面偏好");
        var palace=new MemoryPalaceWindow(store);
        foreach(double width in new[]{380d,760d})foreach(double height in new[]{320d,650d})
        {
            var content=(Control)palace.Content!;content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));
            var tabs=content.GetVisualDescendants().OfType<TabControl>().Single();var p=tabs.TranslatePoint(new Point(0,0),content)!.Value;
            Check(p.X>=0&&p.Y>=0&&p.X+tabs.Bounds.Width<=width+.1&&p.Y+tabs.Bounds.Height<=height+.1&&tabs.Bounds.Height>40,$"palace tab viewport fits {width}x{height}");
        }
        palace.Close();
        Console.WriteLine($"{passed}/{passed} PASS; actual full-window Avalonia layout, long input/status/conversation; no model calls.");
    }
}
