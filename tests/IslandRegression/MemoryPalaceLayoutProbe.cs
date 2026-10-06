using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Views;

internal static class MemoryPalaceLayoutProbe
{
    internal static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        var passed=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
        foreach(var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English})foreach(var empty in new[]{true,false}) {
            LocalizationManager.SetLanguage(language);
            var store=new PersonalAssistantStore(Path.Combine(Path.GetTempPath(),"palace-layout-"+Guid.NewGuid().ToString("N"),"personal.dpapi"));
            if(!empty){PersonalTestData.Memory(store,"我養一條黑王蛇，喜歡清楚直接的中文分類","寵物飼養與照護");PersonalTestData.Memory(store,"不要每次回答都叫我名字","回覆與互動偏好");}
            var window=new MemoryPalaceWindow(store){ShowInTaskbar=false,ShowActivated=false,Opacity=0};window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();var root=(Control)window.Content!;
            foreach(var size in new[]{new Size(380,480),new Size(760,650)}) {
                window.Width=size.Width;window.Height=size.Height;Avalonia.Threading.Dispatcher.UIThread.RunJobs();root.Measure(size);root.Arrange(new Rect(size));Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var tabs=root.GetVisualDescendants().OfType<TabControl>().Single();var p=tabs.TranslatePoint(new Point(),root)!.Value;
                Check(tabs.Bounds.Height>40&&p.X+tabs.Bounds.Width<=size.Width+.1&&p.Y+tabs.Bounds.Height<=size.Height+.1,$"memory palace viewport {language} {empty} {size}; tabs={tabs.Bounds}; origin={p}; root={root.Bounds}");
                Check(!root.GetVisualDescendants().OfType<ComboBox>().Any(),"memory categories have no fixed dropdown");
                Check(tabs.Items.Cast<TabItem>().Select(t=>t.Header?.ToString()).SequenceEqual(language==AppLanguage.English?new[]{"Personal memory","Reminders"}:new[]{"個人記憶","定時提醒"}),"both tab headers translated before selection");
                if(!empty)Check(root.GetVisualDescendants().OfType<TextBox>().Any(x=>x.MaxLength==24&&x.Text=="寵物飼養與照護"),"AI category shown in editable input");
                else Check(root.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains(language==AppLanguage.English?"No memories yet":"目前沒有記憶")==true),"empty state translated");
                if(size.Width==760) {
                    Directory.CreateDirectory("artifacts/native-qa");
                    using var bitmap=new RenderTargetBitmap(new PixelSize(760,650));bitmap.Render(root);
                    bitmap.Save($"artifacts/native-qa/memory-palace-{language}-{(empty?"empty":"populated")}.png");
                }
            }
            if(!empty) {
                Check(root.GetVisualDescendants().OfType<Button>().Any(b=>b.Content?.ToString()==(language==AppLanguage.English?"Save changes":"儲存修改")),"memory action translated at creation");
                LocalizationManager.SetLanguage(language==AppLanguage.English?AppLanguage.TraditionalChinese:AppLanguage.English);
                Check(root.GetVisualDescendants().OfType<Button>().Any(b=>b.Content?.ToString()==(language==AppLanguage.English?"儲存修改":"Save changes")),"memory action follows language change without losing editor");
            }
            window.Close();
        }
        Console.WriteLine($"{passed}/{passed} PASS; memory palace layout and localization.");
    }
}
