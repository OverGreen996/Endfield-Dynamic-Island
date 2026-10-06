using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Animations;

// Music and notifications share the upstream choreography instead of maintaining
// separate timing curves. Their callers own cancellation and the final state.
internal static class OriginalCapsuleIntro
{
    private static T Part<T>(Window window,string prefix,string name) where T:Control
    {
        string key=prefix.Length==0?name switch{"MotionIcon"=>"BoltIcon","IntroTitle"=>"TitleHost",_=>name}:prefix+name;
        return window.FindControl<T>(key) ?? throw new InvalidOperationException("Missing capsule part: "+key);
    }
    internal static async Task RunAsync(Window window,Border pill,Grid content,string prefix,AnimationOptions options,CancellationToken token)
    {
        pill.Height=60;pill.CornerRadius=new CornerRadius(30);pill.Opacity=1;content.Opacity=0;
        if(!IslandTransition.AnimationsEnabled)return;
        if(pill.Width<559){await HudAnimations.SimpleFadeIn(options).RunAsync(content,token);return;}
        var icon=Part<Grid>(window,prefix,"MotionIcon");var circle=Part<Grid>(window,prefix,"CircleForm");var square=Part<Grid>(window,prefix,"SquareForm");
        var title=Part<StackPanel>(window,prefix,"IntroTitle");var ripple=Part<Grid>(window,prefix,"RippleHost");
        icon.Opacity=0;icon.RenderTransform=new TransformGroup{Children={new ScaleTransform(.4,.4),new TranslateTransform()}};
        circle.Opacity=square.Opacity=title.Opacity=0;ripple.RenderTransform=new TranslateTransform();
        var inner=Part<Ellipse>(window,prefix,"RippleInner");var mid=Part<Ellipse>(window,prefix,"RippleMid");var outer=Part<Ellipse>(window,prefix,"RippleOuter");
        foreach(var ring in new[]{inner,mid,outer}){ring.Opacity=0;ring.RenderTransform=new ScaleTransform(0,0);}
        await Task.WhenAll(
            HudAnimations.PillAppear(options).RunAsync(pill,token),HudAnimations.PillHeight(options).RunAsync(pill,token),HudAnimations.PillCorner(options).RunAsync(pill,token),
            HudAnimations.BoltIcon(options).RunAsync(icon,token),HudAnimations.CircleForm(options).RunAsync(circle,token),HudAnimations.SquareForm(options).RunAsync(square,token),
            HudAnimations.TitleHost(options).RunAsync(title,token),HudAnimations.NumHost(options).RunAsync(content,token),HudAnimations.RippleHost(options).RunAsync(ripple,token),HudAnimations.PillHeight(options).RunAsync(ripple,token),
            HudAnimations.Ripple(options,1.5,.5).RunAsync(inner,token),HudAnimations.Ripple(options,2,.5).RunAsync(mid,token),HudAnimations.Ripple(options,2.5,.6).RunAsync(outer,token),
            HudAnimations.RippleRise(options).RunAsync(Part<Grid>(window,prefix,"RippleInnerHost"),token),
            HudAnimations.RippleRise(options).RunAsync(Part<Grid>(window,prefix,"RippleMidHost"),token),
            HudAnimations.RippleRise(options).RunAsync(Part<Grid>(window,prefix,"RippleOuterHost"),token));
    }
    internal static void Finish(Window window,Border pill,Grid content,string prefix)
    {
        pill.Height=60;pill.CornerRadius=new CornerRadius(30);pill.Opacity=1;pill.RenderTransform=new ScaleTransform(1,1);
        Part<Grid>(window,prefix,"MotionIcon").Opacity=Part<StackPanel>(window,prefix,"IntroTitle").Opacity=0;
        foreach(var name in new[]{"RippleInner","RippleMid","RippleOuter"})Part<Ellipse>(window,prefix,name).Opacity=0;
        content.Opacity=1;
    }
    internal static Geometry Ring(double fraction)
    {
        double sweep=Math.Clamp(fraction*360,.5,359.5),radius=20.75;
        var end=new Point(23+radius*Math.Sin(sweep*Math.PI/180),23-radius*Math.Cos(sweep*Math.PI/180));
        return new PathGeometry{Figures=new PathFigures{
            new PathFigure{StartPoint=new Point(0,0),IsClosed=false},new PathFigure{StartPoint=new Point(46,46),IsClosed=false},
            new PathFigure{StartPoint=new Point(23,2.25),IsClosed=false,Segments=new PathSegments{new ArcSegment{Point=end,Size=new Size(radius,radius),IsLargeArc=sweep>180,SweepDirection=SweepDirection.Clockwise}}}}};
    }
}
