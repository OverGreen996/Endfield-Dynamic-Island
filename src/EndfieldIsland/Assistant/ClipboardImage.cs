using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace EndfieldChargePlus.Assistant;

public sealed record PastedImage(string mime_type, string data, int Width, int Height)
{
    public ImageInput ToInput() => new(mime_type,data);
}
public sealed record ImageInput(string mime_type,string data);

/// <summary>Read images only after explicit Ctrl+V; strip metadata and bound image memory locally.</summary>
public static class ClipboardImage
{
    private const uint CF_DIB=8,CF_DIBV5=17;
    public static byte[]? ReadEncodedImage()
    {
        if(!OperatingSystem.IsWindows()||!OpenClipboard(IntPtr.Zero))return null;
        try
        {
            uint png=RegisterClipboardFormat("PNG");
            uint format=IsClipboardFormatAvailable(png)?png:IsClipboardFormatAvailable(CF_DIBV5)?CF_DIBV5:CF_DIB;
            if(!IsClipboardFormatAvailable(format))return null;
            var handle=GetClipboardData(format);if(handle==IntPtr.Zero)return null;
            ulong length=GlobalSize(handle).ToUInt64();
            if(length is <40 or >67108864)throw new InvalidOperationException("圖片太大，請先複製較小的截圖。");
            var pointer=GlobalLock(handle);if(pointer==IntPtr.Zero)return null;
            try
            {
                var bytes=new byte[(int)length];Marshal.Copy(pointer,bytes,0,bytes.Length);
                return format==png?bytes:DibToBmp(bytes);
            }
            finally{GlobalUnlock(handle);}
        }
        finally{CloseClipboard();}
    }
    public static byte[] DibToBmp(byte[] dib)
    {
        if(dib.Length<40)throw new InvalidOperationException("圖片格式不完整。");
        int header=BinaryPrimitives.ReadInt32LittleEndian(dib);
        int w=BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)),h=BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8));
        int bits=BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        uint compression=BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        uint colors=BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));
        if(header is <40 or >124||header>dib.Length||w<=0||h==0||h==int.MinValue||w>8192||Math.Abs(h)>8192||
          (long)w*Math.Abs(h)>16777216||bits is not (1 or 4 or 8 or 16 or 24 or 32)||compression is not (0 or 3 or 6))
            throw new InvalidOperationException("圖片格式或尺寸超過支援範圍。");
        long palette=colors>0?colors:bits<=8?1L<<bits:0;
        long offset=14L+header+palette*4+(header==40&&compression==3?12:header==40&&compression==6?16:0);
        long pixelLength=(((long)w*bits+31)/32*4)*Math.Abs(h);
        if(offset>dib.Length+14||pixelLength>dib.Length+14-offset)throw new InvalidOperationException("圖片資料不完整。");
        var bmp=new byte[dib.Length+14];bmp[0]=(byte)'B';bmp[1]=(byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2),bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10),(int)offset);dib.CopyTo(bmp,14);return bmp;
    }
    public static PastedImage Normalize(byte[] encoded)
    {
        using var data=SKData.CreateCopy(encoded);using var codec=SKCodec.Create(data);
        if(codec is null||codec.Info.Width<=0||codec.Info.Height<=0||codec.Info.Width>8192||codec.Info.Height>8192||
          (long)codec.Info.Width*codec.Info.Height>16777216)throw new InvalidOperationException("無法讀取圖片，請重新複製截圖。");
        using var original=SKBitmap.Decode(codec)??throw new InvalidOperationException("圖片資料不完整。");
        // Retain small screenshot text at Windows OCR's supported 2600px edge before reducing to fit the byte cap.
        foreach(int edge in new[]{2600,1600,1200,900})
        {
            double ratio=Math.Min(1,(double)edge/Math.Max(original.Width,original.Height));
            int w=Math.Max(1,(int)Math.Round(original.Width*ratio)),h=Math.Max(1,(int)Math.Round(original.Height*ratio));
            using var scaled=original.Resize(new SKImageInfo(w,h),SKFilterQuality.Medium)??throw new InvalidOperationException("無法建立圖片預覽。");
            using var image=SKImage.FromBitmap(scaled);using var png=image.Encode(SKEncodedImageFormat.Png,100);
            if(png.Size<=2*1024*1024)return new("image/png",Convert.ToBase64String(png.ToArray()),w,h);
        }
        throw new InvalidOperationException("圖片內容過大，請複製較小範圍。");
    }
    [DllImport("user32.dll")]private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]private static extern bool CloseClipboard();
    [DllImport("user32.dll")]private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")]private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll")]private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")]private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")]private static extern bool GlobalUnlock(IntPtr handle);
}
