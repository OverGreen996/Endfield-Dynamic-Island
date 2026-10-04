using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using EndfieldChargePlus.Diagnostics;
using SkiaSharp;
using System.IO;

namespace EndfieldChargePlus.Interop;

/// <summary>Cached native outline for islands whose interior is continuously painted.</summary>
internal sealed class PaintedIslandRegion
{
    private (Size Size, double Scaling, Rect Body, CornerRadius Radius)? _key;
    private bool _building;
    public void Update(Window window, Border body, WindowsHudHitTest? hitTest)
    {
        if (!window.IsVisible || hitTest is null || _building || body.Bounds.Width <= 0 || body.Bounds.Height <= 0) return;
        var key = (window.Bounds.Size, window.RenderScaling, body.Bounds, body.CornerRadius);
        if (_key == key) return;
        _building = true;
        try
        {
            using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * window.RenderScaling), (int)Math.Ceiling(window.Bounds.Height * window.RenderScaling)), new Vector(96 * window.RenderScaling, 96 * window.RenderScaling));
            image.Render((Visual)window.Content!);
            using var buffer = new MemoryStream(); image.Save(buffer); buffer.Position = 0;
            using var pixels = SKBitmap.Decode(buffer);
            var rows = new (int Left, int Right)[pixels.Height];
            bool painted = false;
            for (int y = 0; y < pixels.Height; y++)
            {
                int left = 0, right = pixels.Width;
                while (left < right && pixels.GetPixel(left, y).Alpha == 0) left++;
                while (right > left && pixels.GetPixel(right - 1, y).Alpha == 0) right--;
                rows[y] = (left, right); painted |= right > left;
            }
            if (!painted) return; // Wait for the first arranged/painted frame.
            if (!hitTest.SetPaintedRegion(rows)) throw new InvalidOperationException("Windows rejected the painted island region.");
            _key = key;
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to build antialiased island outline; using safe body region.", ex);
            hitTest.SetInteractiveRegion(body.Bounds, window.RenderScaling, body.CornerRadius.TopLeft);
            _key = key;
        }
        finally { _building = false; }
    }
}
