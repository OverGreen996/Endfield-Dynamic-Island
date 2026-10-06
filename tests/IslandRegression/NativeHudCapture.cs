using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SkiaSharp;

// Actual compositor capture supplements RenderTargetBitmap, whose scaled text clips
// can differ from the Windows surface. No private app data or model calls are used.
internal static class NativeHudCapture
{
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width,Height; public ushort Planes,Bits;
        public uint Compression,ImageSize; public int X,Y; public uint Used,Important;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    private static SKBitmap Capture(Window window,Control body)
    {
        var point=body.TranslatePoint(default,window)!.Value;
        var end=body.TranslatePoint(new Point(body.Bounds.Width,body.Bounds.Height),window)!.Value;
        double dpi=window.RenderScaling;int width=(int)Math.Round((end.X-point.X)*dpi),height=(int)Math.Round((end.Y-point.Y)*dpi);
        var screen=GetDC(IntPtr.Zero);var dc=CreateCompatibleDC(screen);
        var info=new BitmapInfo{Size=(uint)Marshal.SizeOf<BitmapInfo>(),Width=width,Height=-height,Planes=1,Bits=32};
        var dib=CreateDIBSection(screen,ref info,0,out var data,IntPtr.Zero,0);var old=SelectObject(dc,dib);
        try
        {
            if(!BitBlt(dc,0,0,width,height,screen,window.Position.X+(int)Math.Round(point.X*dpi),window.Position.Y+(int)Math.Round(point.Y*dpi),0x40CC0020))throw new Exception("Windows capture failed");
            var pixels=new byte[width*height*4];Marshal.Copy(data,pixels,0,pixels.Length);
            for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
            var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Opaque));Marshal.Copy(pixels,0,bitmap.GetPixels(),pixels.Length);return bitmap;
        }
        finally{SelectObject(dc,old);DeleteObject(dib);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
    }
    internal static void Save(Window window,Control body,string path)
    {
        using var bitmap=Capture(window,body);using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(path);encoded.SaveTo(file);
    }
    internal static bool LabelsPaint(Window window,Control body,Control band)
    {
        using var bitmap=Capture(window,body);var origin=body.TranslatePoint(default,window)!.Value;double dpi=window.RenderScaling;
        foreach(var label in band.GetVisualDescendants().OfType<TextBlock>().Where(t=>new[]{"CPU","GPU","RAM","VRAM"}.Contains(t.Text)))
        {
            var start=label.TranslatePoint(default,window)!.Value;var end=label.TranslatePoint(new Point(label.Bounds.Width,label.Bounds.Height),window)!.Value;int painted=0;
            for(int y=Math.Max(0,(int)((start.Y-origin.Y)*dpi));y<Math.Min(bitmap.Height,(end.Y-origin.Y)*dpi);y++)
            for(int x=Math.Max(0,(int)((start.X-origin.X)*dpi));x<Math.Min(bitmap.Width,(end.X-origin.X)*dpi);x++)
            {var p=bitmap.GetPixel(x,y);if(p.Red>115&&p.Green>115&&p.Blue>115)painted++;}
            if(painted<6)return false;
        }
        return true;
    }
}
