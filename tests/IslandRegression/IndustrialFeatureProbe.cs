using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Customization;
using SkiaSharp;
public static class IndustrialFeatureProbe
{
    public static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        int passed=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
        var root=Path.Combine(Path.GetTempPath(),"IslandAvatar-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var input=Path.Combine(root,"source.png");using(var source=new SKBitmap(400,200)){using var canvas=new SKCanvas(source);canvas.Clear(SKColors.Cyan);using var image=SKImage.FromBitmap(source);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(input,data.ToArray());}
        var avatars=new AvatarStore(Path.Combine(root,"avatars"));int changes=0;avatars.Changed+=()=>changes++;
        avatars.Save(true,input);Check(avatars.Image(true) is {}bitmap&&bitmap.PixelSize.Width==128&&bitmap.PixelSize.Height==128,"wide source becomes a bounded square avatar");
        Check(avatars.Image(false) is null&&changes==1,"user photo does not replace AI photo");
        using(var decoded=SKBitmap.Decode(Path.Combine(root,"avatars","user.png")))Check(decoded.GetPixel(128,128)==SKColors.Cyan,"crop preserves image content");
        Check(new AvatarStore(Path.Combine(root,"avatars")).Image(true)?.PixelSize.Width==128,"local avatar survives reload");
        using(var unlocked=File.Open(Path.Combine(root,"avatars","user.png"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(unlocked.Length>0,"avatar read releases its file handle");
        var invalid=Path.Combine(root,"invalid.png");File.WriteAllText(invalid,"not an image");byte[] before=File.ReadAllBytes(Path.Combine(root,"avatars","user.png"));bool rejected=false;
        try{avatars.Save(true,invalid);}catch(InvalidOperationException){rejected=true;}Check(rejected&&before.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"avatars","user.png"))),"invalid input cannot replace a valid avatar");
        var large=Path.Combine(root,"large.png");using(var file=File.Create(large))file.SetLength(10*1024*1024+1);rejected=false;try{avatars.Save(false,large);}catch(InvalidOperationException){rejected=true;}Check(rejected&&avatars.Image(false) is null,"oversized image rejected without modifying AI");
        avatars.Save(false,input);
        foreach(bool user in new[]{false,true}){var row=new EndfieldChargePlus.Views.ConversationMessageRow(user,new TextBlock{Text="QA"},avatars);var avatar=(Border)row.Children[1];Check(avatar.Child is Border&&((ISolidColorBrush)avatar.BorderBrush!).Color==Color.Parse(user?"#E6E744":"#13C8EB"),"custom photo keeps its role-specific ring: "+user);}
        avatars.Reset();Check(avatars.Image(true) is null&&avatars.Image(false) is null&&changes==3,"reset restores both default identities");
        var config=CustomHudSettings.CreateDefault();var overview=config.Profiles.Single(p=>p.BuiltInKey=="system.overview");
        Check(config.ActiveProfileId==overview.Id,"new installations use unified overview");
        var previous=config.Profiles.Single(p=>p.BuiltInKey=="system.cpu");var old=config with{ActiveProfileId=previous.Id};var upgraded=HudSettingsNormalizer.Normalize(old);
        Check(upgraded.ActiveProfileId==previous.Id&&upgraded.Profiles.Single(p=>p.BuiltInKey=="system.overview").Id==overview.Id,"upgrade preserves existing selection and profile IDs");
        var chosen=overview with{GpuAdapterId="test-adapter"};config=config with{Profiles=config.Profiles.Select(p=>p.Id==chosen.Id?chosen:p).ToList()};Check(HudSettingsNormalizer.Normalize(config).Profiles.Single(p=>p.Id==chosen.Id).GpuAdapterId=="test-adapter","overview preserves selected GPU adapter");
        var values=new Dictionary<string,object?>{{"cpu.usage",10d},{"cpu.frequency_ghz",4.2d},{"gpu.usage",37d},{"gpu.dedicated_used_bytes",2147483648d},{"gpu.vram_bytes",8589934592d},{"memory.usage",62d},{"memory.used_bytes",16*1073741824d},{"memory.total_bytes",32*1073741824d}};
        LocalizationManager.SetLanguage(AppLanguage.English);var metrics=HudProfileRenderer.Render(overview,values).Metrics!;
        Check(metrics.Select(m=>m.Value).SequenceEqual(new[]{"10%","37%","2.0 GB","62%"})&&metrics[2].Usage==25,"four metrics retain independent readings and VRAM ratio");
        Check(HudProfileRenderer.Render(previous,values).Metrics is null,"ordinary CPU profile retains its layout contract");
        var missing=HudProfileRenderer.Render(overview,new Dictionary<string,object?>{{"cpu.usage",double.NaN},{"gpu.usage",double.PositiveInfinity}}).Metrics!;
        Check(missing.All(m=>m.Value=="—"&&m.Usage is null),"missing or nonfinite values are unknown, not fabricated zeros");
        values["gpu.uses_unified_memory"]=true;metrics=HudProfileRenderer.Render(overview,values).Metrics!;Check(metrics[2].Value=="Shared"&&metrics[2].Usage is null,"integrated GPU does not invent dedicated VRAM");
        var required=HudProfileRenderer.GetRequiredVariables(overview);Check(required.Contains("cpu.usage")&&required.Contains("gpu.usage")&&required.Contains("gpu.dedicated_used_bytes")&&required.Contains("memory.usage"),"overview requests all metric families from shared provider");
        var personal=new PersonalAssistantStore(Path.Combine(root,"p.dpapi"));var session=new AssistantSession(Path.Combine(root,"s.dpapi"));var ai=new EndfieldChargePlus.Views.AssistantIslandWindow(personal,session);var modes=ai.FindControl<ComboBox>("ModeCombo")!;
        Check(((ComboBoxItem)modes.SelectedItem!).Content?.ToString()=="Auto","initial English selected mode renders Auto");modes.SelectedIndex=2;ai.FindControl<TextBox>("InputBox")!.Text="保留中文草稿";
        LocalizationManager.SetLanguage(AppLanguage.TraditionalChinese);Check(modes.SelectedIndex==2&&((ComboBoxItem)modes.SelectedItem!).Content?.ToString()=="只搜尋","Chinese switch preserves selected search mode");
        LocalizationManager.SetLanguage(AppLanguage.English);Check(modes.SelectedIndex==2&&((ComboBoxItem)modes.SelectedItem!).Content?.ToString()=="Search only"&&ai.FindControl<TextBox>("InputBox")!.Text=="保留中文草稿","English switch translates mode without mutating draft");ai.Close();
        HudProfileSaveProbe.Run(Check);
        Console.WriteLine($"{passed}/{passed} PASS; isolated local assets/settings, no API calls.");
    }
}
