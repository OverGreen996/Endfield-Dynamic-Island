using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Views;
using SkiaSharp;
using System.Buffers.Binary;

public static class ImageInputProbe
{
    public static void Run()
    {
        int passed=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
        using var bitmap=new SKBitmap(2200,1200);using(var canvas=new SKCanvas(bitmap)){
            canvas.Clear(new SKColor(25,29,29));using var ink=new SKPaint{Color=new SKColor(19,200,235),TextSize=80,IsAntialias=true};
            canvas.DrawText("IMAGE INPUT / TEST",100,180,ink);ink.Color=SKColors.Yellow;canvas.DrawRect(100,300,1800,600,ink);
        }
        using var img=SKImage.FromBitmap(bitmap);using var encoded=img.Encode(SKEncodedImageFormat.Png,100);
        var input=ClipboardImage.Normalize(encoded.ToArray());
        Check(input.Width==1600&&input.Height<=1600,"large clipboard image scaled locally with aspect ratio");
        Check(Convert.FromBase64String(input.data).Length<=2*1024*1024&&input.mime_type=="image/png","bounded metadata-free inline PNG");
        var dib=new byte[40+16];BinaryPrimitives.WriteInt32LittleEndian(dib,40);BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4),2);BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8),2);BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12),1);BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14),24);
        Check(ClipboardImage.Normalize(ClipboardImage.DibToBmp(dib)).Width==2,"Windows DIB clipboard translated to valid PNG");
        foreach(var invalid in new[]{new byte[2],dib[..40]}){bool blocked=false;try{ClipboardImage.DibToBmp(invalid);}catch(InvalidOperationException){blocked=true;}Check(blocked,"invalid DIB rejected before decoding");}
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        var dir=Path.Combine(Path.GetTempPath(),"IslandImageQA-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var ai=new AssistantIslandWindow(new PersonalAssistantStore(Path.Combine(dir,"personal.dpapi")),new AssistantSession(Path.Combine(dir,"session.dpapi")));
        ai.SetPastedImage(input);var root=(Control)ai.Content!;
        foreach(double width in new[]{280d,420d,660d,840d})foreach(double cap in new[]{360d,620d}){
            ai.ApplyViewportLayout(width,cap);root.Measure(new Size(ai.Width,ai.Height));root.Arrange(new Rect(0,0,ai.Width,ai.Height));
            foreach(string id in new[]{"ImagePreviewFrame","InputBox","FooterGrid","RemoveImageButton","SendButton"}){
                var c=ai.FindControl<Control>(id)!;var p=c.TranslatePoint(new Point(0,0),root)!.Value;
                Check(p.X>=8&&p.Y>=8&&p.X+c.Bounds.Width<=ai.Width-8+.1&&p.Y+c.Bounds.Height<=ai.Height-8+.1,$"image preview {id} fits width={width} cap={cap}");
            }
            var preview=ai.FindControl<Border>("ImagePreviewFrame")!;var composer=ai.FindControl<TextBox>("InputBox")!;
            Check(preview.Bounds.Bottom<=composer.Bounds.Top+.1,"image preview cannot cover composer");
        }
        NewFolderAndSave();
        void NewFolderAndSave(){Directory.CreateDirectory("outputs");using var render=new RenderTargetBitmap(new PixelSize((int)ai.Width,(int)ai.Height));render.Render(root);render.Save(Path.Combine("outputs","Island-v23-PastedImage.png"));File.WriteAllBytes(Path.Combine("outputs","image-test-fixture.png"),Convert.FromBase64String(input.data));}
        ai.Close();Check(!File.Exists(Path.Combine(dir,"personal.dpapi")),"preview alone never stores memory");
        Console.WriteLine($"{passed}/{passed} PASS; synthetic images, no clipboard or model reads.");
    }
}
