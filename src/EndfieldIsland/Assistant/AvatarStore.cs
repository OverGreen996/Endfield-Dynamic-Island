using Avalonia.Media.Imaging;
using SkiaSharp;

namespace EndfieldChargePlus.Assistant;

/// <summary>Small local image assets, independent of chats/keys and never uploaded.</summary>
public sealed class AvatarStore
{
    public static AvatarStore Shared { get; }=new();
    private readonly string _root;
    private readonly object _gate=new();
    private readonly Dictionary<bool,Bitmap> _images=new();
    public event Action? Changed;
    public AvatarStore(string? root=null)=>_root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EndfieldChargePlus","Avatars");
    private string PathFor(bool user)=>Path.Combine(_root,user?"user.png":"assistant.png");
    public Bitmap? Image(bool user)
    {
        lock(_gate)
        {
            if(_images.TryGetValue(user,out var image))return image;
            try { if(File.Exists(PathFor(user))){using var stream=File.OpenRead(PathFor(user));return _images[user]=Bitmap.DecodeToWidth(stream,128);} } catch { }
            return null;
        }
    }
    public void Save(bool user,string file)
    {
        var info=new FileInfo(file);
        if(!info.Exists||info.Length>10*1024*1024)throw new InvalidOperationException(LocalizationManager.Text("圖片不可超過 10 MB。","Image must be no larger than 10 MB."));
        using var stream=File.OpenRead(file);using var codec=SKCodec.Create(stream);
        if(codec is null||codec.Info.Width<=0||codec.Info.Height<=0||(long)codec.Info.Width*codec.Info.Height>16777216)
            throw new InvalidOperationException(LocalizationManager.Text("請選擇有效圖片，最多 1600 萬像素。","Choose a valid image, up to 16 megapixels."));
        using var source=SKBitmap.Decode(codec);if(source is null)throw new InvalidOperationException(LocalizationManager.Text("無法讀取這張圖片。","This image cannot be decoded."));
        using var output=new SKBitmap(256,256);using var canvas=new SKCanvas(output);canvas.Clear(SKColors.Transparent);
        float side=Math.Min(source.Width,source.Height);var crop=SKRect.Create((source.Width-side)/2,(source.Height-side)/2,side,side);
        using var paint=new SKPaint{IsAntialias=true,FilterQuality=SKFilterQuality.High};canvas.DrawBitmap(source,crop,new SKRect(0,0,256,256),paint);
        using var image=SKImage.FromBitmap(output);using var bytes=image.Encode(SKEncodedImageFormat.Png,100);
        lock(_gate)
        {
            Directory.CreateDirectory(_root);string path=PathFor(user),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { File.WriteAllBytes(temp,bytes.ToArray());File.Move(temp,path,true);_images.Remove(user,out var old);old?.Dispose(); }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        Changed?.Invoke();
    }
    public void Reset()
    {
        lock(_gate){foreach(bool user in new[]{true,false}){if(File.Exists(PathFor(user)))File.Delete(PathFor(user));if(_images.Remove(user,out var image))image.Dispose();}}
        Changed?.Invoke();
    }
}
