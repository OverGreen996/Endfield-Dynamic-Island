using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Views;
using System.Reflection;

public sealed class PersonalUiApplication : App
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    private object? Field(string name)=>typeof(App).GetField(name,F)!.GetValue(this);
    private void Field(string name,object value)=>typeof(App).GetField(name,F)!.SetValue(this,value);
    private void Call(string name)=>typeof(App).GetMethod(name,F)!.Invoke(this,null);
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)return;
        ((EndfieldChargePlus.Settings.SettingsWindow)Field("_settingsWindow")!).Hide();
        ((NotificationMonitor)Field("_notifications")!).Configure(false);
        Call("DisposePersonalAssistant");
        string root=Path.Combine(Path.GetTempPath(),"IslandPersonalUi-"+Guid.NewGuid().ToString("N"));
        var store=new PersonalAssistantStore(Path.Combine(root,"personal.dpapi"));Field("_personalStore",store);Call("InitializePersonalAssistant");
        PersonalTestData.Memory(store,"我叫介面測試使用者","身分稱呼");PersonalTestData.Memory(store,"我喜歡黑色介面","介面偏好");PersonalTestData.Memory(store,"以後不要自作主張替我做決定","互動界線");
        var session=new AssistantSession(Path.Combine(root,"session.dpapi"));
        for(int i=0;i<15;i++)session.Append("介面驗收：輸入與送出列不能被切到。",new AssistantReply("這是本機合成的對話。長內容會留在捲動區，底部輸入與按鈕固定保留空間；沒有送出 Gemini。","local",null,false,null,null,null));
        var ai=new AssistantIslandWindow(store,session){Title="AI v18 本機驗收"};Field("_assistant",ai);ai.IslandHidden+=()=>Call("RestoreHud");
        ai.MusicRequested+=()=>typeof(App).GetMethod("OpenMusic",F)!.Invoke(this,new object?[]{false,null});
        ai.MemoryRequested+=()=>Call("OpenMemoryPalace");
        var notice=(NotificationIslandWindow)typeof(App).GetMethod("CreateNotificationIsland",F)!.Invoke(this,null)!;notice.ShowInTaskbar=true;notice.Title="通知 v18 本機驗收";Field("_notificationIsland",notice);
        var controls=new StackPanel{Margin=new Thickness(20),Spacing=14};
        var showAi=new Button{Content="顯示 AI 驗收頁"};showAi.Click+=(_,_)=>Call("OpenAssistant");controls.Children.Add(showAi);
        var showNotice=new Button{Content="五秒後通知（不讀取 Windows 私人內容）"};showNotice.Click+=async(_,_)=>{await Task.Delay(5000);await (Task<string>)typeof(App).GetMethod("PreviewNotificationAsync",F)!.Invoke(this,null)!;};controls.Children.Add(showNotice);
        var memory=new Button{Content="開啟記憶宮殿驗收"};memory.Click+=(_,_)=>Call("OpenMemoryPalace");controls.Children.Add(memory);
        var state=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.White};controls.Children.Add(state);
        var underlay=new Window{Title="靈動島 v18 本機驗收控制",Width=680,Height=320,Content=controls,Background=Brush.Parse("#334347"),Position=new PixelPoint(300,650)};underlay.Show();desktop.MainWindow=underlay;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        timer.Tick+=(_,_)=>{var summary=new{aiVisible=ai.IsVisible,notificationVisible=notice.IsVisible,notice.Pinned,aiOpacity=ai.Opacity,notificationOpacity=notice.Opacity,draft=ai.FindControl<TextBox>("InputBox")!.Text,memories=store.Memories.Count,reminders=store.Reminders.Count,displayed=store.Reminders.Count(r=>r.Displayed is not null)};state.Text=System.Text.Json.JsonSerializer.Serialize(summary);File.WriteAllText(Path.Combine(Environment.CurrentDirectory,"outputs","Island-v18-Native-State.json"),state.Text);};timer.Start();
        underlay.Closed+=(_,_)=>{timer.Stop();desktop.Shutdown();};
        Dispatcher.UIThread.Post(()=>Call("OpenAssistant"));
    }
}
