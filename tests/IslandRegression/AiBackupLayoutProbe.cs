using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant.Native;
using EndfieldChargePlus.Views;

internal static class AiBackupLayoutProbe
{
    internal static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        var count=0;var calls=0;
        using var http=new HttpClient(new AiBackupProbe.Handler((_,_)=>{calls++;throw new Exception("FAIL: layout must not call APIs");}));
        using var service=new NativeAssistantService(AiBackupProbe.Root(),http);
        LocalizationManager.SetLanguage(AppLanguage.TraditionalChinese);
        var window=new AiBackupSettingsWindow(service){ShowInTaskbar=false,ShowActivated=false,Opacity=0};
        window.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();var root=(Control)window.Content!;
        var password=root.GetVisualDescendants().OfType<TextBox>().Where(x=>x.PasswordChar=='●').ToArray();
        if(password.Length!=2)throw new Exception("FAIL: two masked credential inputs required");count++;
        password[0].Text="synthetic unsaved draft";
        foreach(var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English}){
            LocalizationManager.SetLanguage(language);
            if(password[0].Text!="synthetic unsaved draft")throw new Exception("FAIL: language change discarded credential draft");count++;
            if(!root.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text?.Contains(language==AppLanguage.English?"Not configured":"尚未設定")==true))throw new Exception("FAIL: status not translated");count++;
            foreach(var width in new[]{480d,740d}){
                window.Width=width;Avalonia.Threading.Dispatcher.UIThread.RunJobs();root.InvalidateMeasure();root.Measure(new Size(width,760));root.Arrange(new Rect(0,0,width,760));
                foreach(var input in root.GetVisualDescendants().OfType<TextBox>()){
                    var point=input.TranslatePoint(new Point(0,0),root)!.Value;
                    if(input.Bounds.Width<100||point.X<0||point.X+input.Bounds.Width>width)throw new Exception($"FAIL: credential input clipped {language}, window {width}, x {point.X}, input width {input.Bounds.Width}, watermark {input.Watermark}");count++;
                }
                if(root.GetVisualDescendants().OfType<Button>().Count(x=>x.Content?.ToString()?.Contains(language==AppLanguage.English?"one request":"一次請求")==true)!=2)throw new Exception("FAIL: tests must explicitly disclose model requests");count++;
                if(width==740){
                    Directory.CreateDirectory("artifacts/native-qa");
                    using var image=new RenderTargetBitmap(new PixelSize(740,760));image.Render(root);image.Save("artifacts/native-qa/ai-backups-"+(language==AppLanguage.English?"en":"zh-TW")+".png");
                    var scroll=root.GetVisualDescendants().OfType<ScrollViewer>().First(x=>x.Content is StackPanel);scroll.Offset=new Vector(0,500);Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    using var lower=new RenderTargetBitmap(new PixelSize(740,760));lower.Render(root);lower.Save("artifacts/native-qa/ai-backups-"+(language==AppLanguage.English?"en":"zh-TW")+"-lower.png");scroll.Offset=new Vector(0,0);
                }
            }
        }
        window.Close();if(password.Any(x=>!string.IsNullOrEmpty(x.Text)))throw new Exception("FAIL: credentials retained by closed window");count++;
        if(calls!=0)throw new Exception("FAIL: layout called API");count++;
        Console.WriteLine($"{count}/{count} native AI backup layout checks PASS; bilingual form and synthetic credentials, zero API calls.");
    }
}
