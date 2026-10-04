using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
public class ProbeApplication:Application
{
 public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;}
 public override void OnFrameworkInitializationCompleted()
 {
  if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop){desktop.MainWindow=new ProbeWindow();}
  base.OnFrameworkInitializationCompleted();
 }
}
public class ProbeWindow:Window
{
 readonly HudWindow hud=new(){ShowInTaskbar=true,Title="HUD body-click 驗證"};
 readonly CustomHudRuntime runtime;
 readonly TextBlock status=new(){FontSize=22,HorizontalAlignment=HorizontalAlignment.Center};
 readonly TextBox input=new(){Watermark="背景鍵盤焦點驗證",Width=400};
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 AppSettings settings=new(){UiLanguage="zh-TW",GlobalScale=1,DisplayDurationSeconds=3,HudOffsetY=200};int clicks=0;
 public ProbeWindow()
 {
  Title="靈動島實際滑鼠驗證背景";Width=1100;Height=720;
  runtime=new CustomHudRuntime(hud);hud.ApplySettings(settings);runtime.ApplySettings(settings);
  var grid=new Grid();var background=new Button{Content="透明區域背景點擊見證",HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(Color.Parse("#15232D")),FontSize=28};
  background.Click+=(_,_)=>clicks++;grid.Children.Add(background);
  var panel=new StackPanel{Orientation=Orientation.Vertical,Spacing=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(30)};
  panel.Children.Add(status);panel.Children.Add(input);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=16};
  var preview=new Button{Content="Preview 3 秒"};preview.Click+=async(_,_)=>{settings=settings with{DisplayDurationSeconds=3};runtime.ApplySettings(settings);await runtime.PreviewActiveAsync();};buttons.Children.Add(preview);var pinPreview=new Button{Content="Preview 10 秒"};pinPreview.Click+=async(_,_)=>{settings=settings with{DisplayDurationSeconds=10};runtime.ApplySettings(settings);await runtime.PreviewActiveAsync();};buttons.Children.Add(pinPreview);
  var always=new CheckBox{Content="AlwaysVisible"};always.IsCheckedChanged+=async(_,_)=>{settings=settings with{AlwaysVisible=always.IsChecked==true,PersistentLayer=PersistentHudLayer.Topmost};await runtime.ApplySettingsWithTransitionAsync(settings);};buttons.Children.Add(always);
  var focusPreview=new Button{Content="聚焦背景再 Preview"};focusPreview.Click+=async(_,_)=>{hud.ShowInTaskbar=false;settings=settings with{DisplayDurationSeconds=10};hud.ApplySettings(settings);runtime.ApplySettings(settings);input.Focus();await runtime.PreviewActiveAsync();};buttons.Children.Add(focusPreview);
  var exit=new Button{Content="結束驗證"};exit.Click+=(_,_)=>Close();buttons.Children.Add(exit);panel.Children.Add(buttons);grid.Children.Add(panel);Content=grid;
  KeyDown+=async(_,e)=>{if(e.Key==Avalonia.Input.Key.P&&e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)){settings=settings with{DisplayDurationSeconds=3};hud.ApplySettings(settings);runtime.ApplySettings(settings);await runtime.PreviewActiveAsync();e.Handled=true;}};
  timer.Tick+=(_,_)=>{var pinned=(bool)(typeof(CustomHudRuntime).GetField("_userPinned",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.GetValue(runtime)??false);status.Text=$"背景點擊 {clicks} · HUD visible={hud.IsVisible} · Pinned={pinned} · 背景 active={IsActive} · 輸入 focus={input.IsFocused} · HUDpos={hud.Position.X},{hud.Position.Y}";};
  Opened+=(_,_)=>{if(Screens.Primary is { } screen){Width=screen.WorkingArea.Width/screen.Scaling;Height=screen.WorkingArea.Height/screen.Scaling;Position=screen.WorkingArea.Position;}runtime.Start();timer.Start();};Closed+=(_,_)=>{timer.Stop();runtime.Dispose();hud.Close();};
 }
}
