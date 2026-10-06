using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Views;
using EndfieldChargePlus.Assistant.Native;

internal static class NativeSearchLayoutProbe
{
    internal static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        var count=0;
        foreach(var language in new[]{"zh-TW","en"}){
            LocalizationManager.SetLanguage(language=="en"?AppLanguage.English:AppLanguage.TraditionalChinese);
            var directory=Path.Combine(Path.GetTempPath(),"island-search-layout-"+Guid.NewGuid().ToString("N"));
            var now=DateTimeOffset.Parse("2026-10-06T09:00:00+08:00").ToUnixTimeMilliseconds();
            using var search=new NativeSearch(directory,now:()=>now);
            var config=NativeSearch.DefaultSettings();config["providers"]!["firecrawl"]!["billingDay"]=6;config["providers"]!["firecrawl"]!["billingMinute"]=81;
            search.ConfigureAsync(config,new(){["exa"]="synthetic-layout-key",["tavily"]="synthetic-tavily-key",["firecrawl"]="synthetic-firecrawl-key"},new HashSet<string>(),default).GetAwaiter().GetResult();
            using(var db=new LocalSqlite(Path.Combine(directory,"search-state.sqlite")))foreach(var id in new[]{"exa","tavily"})db.Query("INSERT OR REPLACE INTO providers VALUES(?,?)",id,new System.Text.Json.Nodes.JsonObject{["start"]=now,["end"]=NativeSearch.NextMonthlyRetry(now),["used"]=id=="exa"?0.007:1,["requests"]=1,["reason"]=id+"_failed",["failureReason"]="timeout",["disabledUntil"]=NativeSearch.NextMonthlyRetry(now)}.ToJsonString());
            var window=new SearchSettingsWindow(search){ShowInTaskbar=false,ShowActivated=false,Opacity=0};window.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();var root=(Control)window.Content!;
            foreach(var width in new[]{600d,820d}){
                window.Width=width;Avalonia.Threading.Dispatcher.UIThread.RunJobs();root.Measure(new Size(width,720));root.Arrange(new Rect(0,0,width,720));
                foreach(var input in root.GetVisualDescendants().OfType<TextBox>().Where(x=>x.PasswordChar=='●')){
                    var point=input.TranslatePoint(new Point(0,0),root)!.Value;
                    if(input.Bounds.Width<100||point.X<0||point.X+input.Bounds.Width>width)throw new Exception($"Search input clipped: {language} {width}, x={point.X}, w={input.Bounds.Width}");count++;
                }
                if(!root.GetVisualDescendants().OfType<TextBox>().Any())throw new Exception("Search form not laid out");
                var rotation=root.GetVisualDescendants().OfType<CheckBox>().Single(x=>Equals(x.Content,language=="en"?"Rotate provider after each search turn":"每輪搜尋換一家"));
                var rotationPoint=rotation.TranslatePoint(new Point(0,0),root)!.Value;
                if(rotationPoint.X<0||rotationPoint.X+rotation.Bounds.Width>width||rotation.Bounds.Height<20)throw new Exception("Rotation control missing or clipped");count++;
                if(root.GetVisualDescendants().OfType<NumericUpDown>().Count()!=1)throw new Exception("Firecrawl should expose only its billing-day input");count++;
                if(root.GetVisualDescendants().OfType<TimePicker>().Count()!=1)throw new Exception("Firecrawl renewal time picker missing");count++;
                if(root.GetVisualDescendants().OfType<TextBlock>().Count(x=>x.Text?.Contains("2026/11/02 00:05")==true)!=2)throw new Exception("Retry time must be visible in both languages");count++;
                if(width==820){
                    Directory.CreateDirectory("artifacts/native-qa");
                    using var image=new RenderTargetBitmap(new PixelSize((int)width,720));image.Render(root);image.Save("artifacts/native-qa/search-settings-"+language+".png");
                    var scroll=root.GetVisualDescendants().OfType<ScrollViewer>().First(x=>x.Content is StackPanel);
                    scroll.Offset=new Vector(0,450);Avalonia.Threading.Dispatcher.UIThread.RunJobs();root.Measure(new Size(width,720));root.Arrange(new Rect(0,0,width,720));
                    using var lower=new RenderTargetBitmap(new PixelSize((int)width,720));lower.Render(root);lower.Save("artifacts/native-qa/search-settings-"+language+"-tavily.png");
                    scroll.Offset=new Vector(0,850);Avalonia.Threading.Dispatcher.UIThread.RunJobs();root.Measure(new Size(width,720));root.Arrange(new Rect(0,0,width,720));
                    using var fire=new RenderTargetBitmap(new PixelSize((int)width,720));fire.Render(root);fire.Save("artifacts/native-qa/search-settings-"+language+"-firecrawl.png");
                }
            }
            window.Close();
        }
        Console.WriteLine($"{count}/{count} native search layout checks PASS");
    }
}
