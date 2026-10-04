using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EndfieldChargePlus.Interop;

internal static class IslandTransition
{
    private static readonly ConditionalWeakTable<Window, CancellationTokenSource> Active = new();
    public static void Cancel(Window window)
    { if (Active.TryGetValue(window, out var previous)) { Active.Remove(window); previous.Cancel(); previous.Dispose(); } }
    public static void PrepareShow(Window window) { Cancel(window); window.Opacity = 0; }
    public static Task FadeInAsync(Window window) => FadeAsync(window, 1, 160);
    public static Task FadeOutAsync(Window window) => FadeAsync(window, 0, 90);
    public static bool AnimationsEnabled
    {
        get { try { return !OperatingSystem.IsWindows() || new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; } catch { return true; } }
    }
    public static async Task CollapseUpAsync(Window window, Border body, Control content, Border surface)
    {
        Cancel(window);
        if(!AnimationsEnabled || !window.IsVisible){window.Opacity=0;return;}
        var cancellation=new CancellationTokenSource();var token=cancellation.Token;Active.Add(window,cancellation);
        double width=Math.Max(1,body.Bounds.Width),height=Math.Max(1,body.Bounds.Height);
        surface.Width=width;surface.Height=height;surface.CornerRadius=body.CornerRadius;
        surface.Background=body.Background;surface.BorderBrush=body.BorderBrush;
        surface.BorderThickness=body.BorderThickness;surface.IsVisible=true;
        body.Background=Brushes.Transparent;body.BorderBrush=Brushes.Transparent;
        var clip=new RectangleGeometry();body.Clip=clip;
        void UpdateClip()
        {
            clip.Rect=new Rect((width-surface.Width)/2,0,surface.Width,surface.Height);
            clip.RadiusX=clip.RadiusY=Math.Min(24,surface.Height/2);
        }
        void SurfaceChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
        { if(args.Property==Control.WidthProperty||args.Property==Control.HeightProperty)UpdateClip(); }
        surface.PropertyChanged+=SurfaceChanged;UpdateClip();
        // A native compositor clock morphs only the backdrop. Text is translated/faded,
        // never scaled or reflowed, and the clip follows the visible surface.
        Animation Track(params KeyFrame[] frames)
        {
            var animation=new Animation{Duration=TimeSpan.FromMilliseconds(280),Easing=new CubicEaseOut(),FillMode=FillMode.Forward};
            foreach(var frame in frames)animation.Children.Add(frame);return animation;
        }
        KeyFrame Frame(double cue,params Setter[] setters)
        { var frame=new KeyFrame{Cue=new Cue(cue)};foreach(var setter in setters)frame.Setters.Add(setter);return frame; }
        var shape=Track(
            Frame(0,new(Control.WidthProperty,width),new(Control.HeightProperty,height),new(Border.CornerRadiusProperty,body.CornerRadius)),
            Frame(1,new(Control.WidthProperty,Math.Min(width,288)),new(Control.HeightProperty,28d),new(Border.CornerRadiusProperty,new CornerRadius(14))));
        shape.Delay=TimeSpan.FromMilliseconds(80);shape.Duration=TimeSpan.FromMilliseconds(200);
        var move=new TranslateTransform();content.RenderTransform=move;
        var letters=Track(Frame(0,new Setter(Visual.OpacityProperty,1d)),Frame(1,new Setter(Visual.OpacityProperty,0d)));
        letters.Easing=new LinearEasing();letters.Duration=TimeSpan.FromMilliseconds(80);
        var lift=Track(Frame(0,new Setter(TranslateTransform.YProperty,0d)),Frame(1,new Setter(TranslateTransform.YProperty,-8d)));
        lift.Easing=new CubicEaseOut();lift.Duration=TimeSpan.FromMilliseconds(80);
        var dissolve=Track(Frame(0,new Setter(Visual.OpacityProperty,window.Opacity)),Frame(.65,new Setter(Visual.OpacityProperty,window.Opacity)),Frame(1,new Setter(Visual.OpacityProperty,0d)));
        dissolve.Easing=new LinearEasing();
        try
        {
            await Task.WhenAll(shape.RunAsync(surface,token),letters.RunAsync(content,token),lift.RunAsync(content,token),dissolve.RunAsync(window,token));
            token.ThrowIfCancellationRequested();window.Opacity=0;
        }
        catch(OperationCanceledException){ }
        catch { cancellation.Cancel();throw; }
        finally{surface.PropertyChanged-=SurfaceChanged;if(Active.TryGetValue(window,out var current)&&ReferenceEquals(current,cancellation)){Active.Remove(window);cancellation.Dispose();}}
    }
    private static async Task FadeAsync(Window window, double target, int milliseconds)
    {
        Cancel(window);
        bool enabled = AnimationsEnabled;
        if (!enabled || !window.IsVisible) { window.Opacity = target; return; }
        var cancellation = new CancellationTokenSource(); var token = cancellation.Token; Active.Add(window, cancellation);
        double from = window.Opacity; var elapsed = Stopwatch.StartNew();
        try
        {
            while (elapsed.ElapsedMilliseconds < milliseconds)
            {
                token.ThrowIfCancellationRequested();
                double t = Math.Clamp(elapsed.Elapsed.TotalMilliseconds / milliseconds, 0, 1);
                window.Opacity = from + (target - from) * t * t * (3 - 2 * t);
                await Task.Delay(16, token);
            }
            window.Opacity = target;
        }
        catch (OperationCanceledException) { }
        finally { if (Active.TryGetValue(window, out var current) && ReferenceEquals(current, cancellation)) { Active.Remove(window); cancellation.Dispose(); } }
    }
}
