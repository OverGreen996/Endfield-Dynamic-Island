using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
public sealed class MusicEdgeUnderlayApplication:Application
{
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            int clicks=0;var grid=new Grid();var label=new TextBlock{Margin=new Thickness(30,180,0,0)};grid.Children.Add(label);
            var button=new Button{Content="+",Width=24,Height=150,Padding=new Thickness(0),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top};grid.Children.Add(button);
            button.Click+=(_,_)=>{label.Text="跨程序透明邊緣點穿："+(++clicks);File.WriteAllText(Path.Combine(Environment.CurrentDirectory,"outputs","Island-Music-v16-CrossProcess-Pass.json"),System.Text.Json.JsonSerializer.Serialize(new{pid=Environment.ProcessId,clicks}));};
            var window=new Window{Title="音樂邊緣 v16 跨程序底層",Width=820,Height=350,Position=new PixelPoint(220,250),SystemDecorations=SystemDecorations.None,Background=Brush.Parse("#526774"),Content=grid};desktop.MainWindow=window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
