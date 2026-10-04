using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using System.Reflection;
using System.Runtime.InteropServices;

/// <summary>Exercises the real App coordinator/window. Restricts QA reads to our own test toasts.</summary>
public sealed class NotificationLiveApplication : App
{
    private const string Marker = "Island UI QA";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private object? Field(string name) => typeof(App).GetField(name, Flags)!.GetValue(this);
    private void Field(string name, object? value) => typeof(App).GetField(name, Flags)!.SetValue(this, value);
    private void Call(string name) => typeof(App).GetMethod(name, Flags)!.Invoke(this, null);
    private sealed class QaSource : INotificationSource
    {
        private readonly WindowsNotificationSource _source = new();
        private readonly Dictionary<string, NotificationCard> _captured = new();
        public int Reads, Matched, Total;
        public bool SyntheticOnly;
        public NotificationAccess? OverrideAccess;
        public NotificationCard? CapturedLast => _captured.Values.OrderByDescending(c => c.Created).FirstOrDefault();
        public void Inject(NotificationCard card) => _captured[card.Key] = card;
        public async Task<NotificationRead> ReadAsync(CancellationToken token)
        {
            var read = SyntheticOnly? new NotificationRead(NotificationAccess.Allowed,Array.Empty<NotificationCard>()) : await _source.ReadAsync(token); Reads++; Total = read.Cards.Count;
            if(OverrideAccess is { } access && access!=NotificationAccess.Allowed)return new(access,Array.Empty<NotificationCard>());
            // QA only: retain our genuinely captured transient Shell balloons for UI timing tests.
            // The production provider always returns the live Windows list without this cache.
            foreach(var card in read.Cards.Where(c => c.Title.StartsWith(Marker))) _captured[card.Key] = card;
            foreach(var key in _captured.Where(p => DateTimeOffset.UtcNow - p.Value.Created > TimeSpan.FromMinutes(2)).Select(p => p.Key).ToArray()) _captured.Remove(key);
            var cards = _captured.Values.ToArray(); Matched = cards.Length; return read with { Cards = cards };
        }
        public Task<NotificationAccess> RequestAccessAsync(CancellationToken token) => _source.RequestAccessAsync(token);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        Call("DisposePersonalAssistant");
        var personalPath=Path.Combine(Path.GetTempPath(),"IslandRoutePersonal-"+Guid.NewGuid().ToString("N"),"personal.dpapi");
        var personal=new EndfieldChargePlus.Assistant.PersonalAssistantStore(personalPath);Field("_personalStore",personal);Call("InitializePersonalAssistant");
        ((NotificationMonitor)Field("_notifications")!).Dispose();
        var source = new QaSource{SyntheticOnly=desktop.Args?.Contains("--notification-routing")==true}; var monitor = new NotificationMonitor(source); Field("_notifications", monitor);
        var island = (NotificationIslandWindow)typeof(App).GetMethod("CreateNotificationIsland",Flags)!.Invoke(this,null)!;
        island.ShowInTaskbar=true;island.Title="通知 v17 滑鼠驗收";
        Field("_notificationIsland", island);
        monitor.Changed += () => Call("OnNotificationMonitorChanged"); monitor.Configure(true);
        var settings = (SettingsWindow)Field("_settingsWindow")!;
        settings.Title = "通知 v15 設定驗收";
        settings.FindControl<TabItem>("NotificationTab")!.IsSelected = true;
        int passthrough = 0, sequence = 0;
        var runtime = (EndfieldChargePlus.Customization.CustomHudRuntime)Field("_runtime")!;
        runtime.ApplySettings(runtime.EffectiveSettings with { DisplayDurationSeconds = 10 });
        var controls = new StackPanel { Margin = new Thickness(20, 210, 20, 12), Spacing = 10 };
        var status = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        var send = new Button { Content = "送出真實 Windows QA 通知" };
        send.Click += (_, _) =>
        {
            var tray = Field("_tray")!;
            NotificationProbe.SendTest(tray, Marker + " " + ++sequence);
        };
        controls.Children.Add(send); controls.Children.Add(new TextBox { Watermark = "鍵盤焦點驗收（不會送出）" }); controls.Children.Add(status);
        var replay = new Button {Content = "重顯已捕獲測試通知（僅 QA）"};
        replay.Click += (_,_) => { var card = source.CapturedLast; if(card is null)return; monitor.Defer(card);Call("TryPresentNotification"); };
        controls.Children.Add(replay);
        var synthetic = new Button { Content = "五秒後預覽 UI 測試卡片（非系統通知）" };
        synthetic.Click += async (_,_) => { await Task.Delay(5000);source.Inject(NotificationCard.Create("qa/"+Guid.NewGuid(),"通知介面驗收", "UI 測試卡片 · 黑色工業風", "此為介面測試卡片，不是實際 Windows 通知。點本體固定，再點收起。", DateTimeOffset.UtcNow));await monitor.PollAsync(); };
        controls.Children.Add(synthetic);
        var corner = new Button { Content = "+", Width = 24, Height = 18, Padding = new Thickness(0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        corner.Click += (_, _) => passthrough++;
        var grid = new Grid(); grid.Children.Add(controls); grid.Children.Add(corner);
        var underlay = new Window { Title = "通知 v15 底層驗收", Width = 560, Height = 520, SystemDecorations = SystemDecorations.None, Background = Brush.Parse("#223139"), Content = grid };
        var screen = underlay.Screens.Primary!;
        underlay.Position = new PixelPoint(screen.WorkingArea.X + (screen.WorkingArea.Width - (int)(560 * screen.Scaling)) / 2, screen.WorkingArea.Y + (int)(8 * screen.Scaling));
        underlay.Show(); underlay.Activate();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var noActivate = island.TryGetPlatformHandle() is { } handle ? SendMessage(handle.Handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt32() == 3 : false;
            var summary = new { pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow, sent = sequence, reads = source.Reads, total = source.Total, matched = source.Matched, opening = (bool)Field("_notificationOpening")!, visible = island.IsVisible, pinned = island.Pinned, passthrough, focusOnUnderlay = GetForegroundWindow() == underlay.TryGetPlatformHandle()?.Handle, noActivate, aiVisible = ((Window?)Field("_assistant"))?.IsVisible ?? false, musicVisible = ((Window?)Field("_music"))?.IsVisible ?? false, access = monitor.Access.ToString() };
            status.Text = System.Text.Json.JsonSerializer.Serialize(summary);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "outputs", "Island-Notification-v15-UI-State.json"), status.Text);
        }; timer.Start();
        int overlapSamples=0,animatedSamples=0;
        var frameTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
        frameTimer.Tick+=(_,_)=>{var windows=new Window?[]{(Window?)Field("_assistant"),(Window?)Field("_music"),island,(Window?)Field("_hud"),(Window?)Field("_memoryPalace")};if(windows.Count(w=>w?.IsVisible==true&&w.Opacity>.001)>1)overlapSamples++;if(windows.Any(w=>w?.IsVisible==true&&w.Opacity is >.001 and <.999))animatedSamples++;};frameTimer.Start();
        underlay.Closed += (_, _) => { timer.Stop(); desktop.Shutdown(); };
        if (desktop.Args?.Contains("--notification-routing") == true)
            Dispatcher.UIThread.Post(async () =>
            {
                int passed = 0;
                void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
                async Task Wait(Func<bool> condition)
                { var until=DateTimeOffset.UtcNow.AddSeconds(20);while(!condition()){if(DateTimeOffset.UtcNow>=until)throw new Exception("Routing fixture timed out");await Task.Delay(50);} }
                NotificationCard Card(int id)=>NotificationCard.Create("route/"+id,"Routing QA","QA card "+id,"Synthetic routing test; no private contents.",DateTimeOffset.UtcNow);
                try
                {
                    await Wait(()=>source.Reads>0&&monitor.Access==NotificationAccess.Allowed);
                    runtime.ApplySettings(runtime.EffectiveSettings with {AlwaysVisible=true,DisplayDurationSeconds=3});
                    Call("OpenAssistant");await Wait(()=>((Window?)Field("_assistant"))?.IsVisible==true&&!(bool)Field("_assistantOpening")!);
                    var assistant=(AssistantIslandWindow)Field("_assistant")!;assistant.FindControl<TextBox>("InputBox")!.Text="QA unsent draft; never send";
                    source.Inject(Card(1));await monitor.PollAsync();await Wait(()=>island.IsVisible&&island.Opacity==1);
                    Check(!assistant.IsVisible&&island.Current?.Key=="route/1","notification preempts AI with real App coordinator");
                    Check(assistant.FindControl<TextBox>("InputBox")!.Text=="QA unsent draft; never send","AI draft survives notification preemption");
                    Check(GetForegroundWindow()!=island.TryGetPlatformHandle()?.Handle,"showing notification does not activate notification window");
                    source.Inject(Card(2));await monitor.PollAsync();await Task.Delay(150);
                    Check(island.Current?.Key=="route/1","another notification does not reset current preview");
                    island.HideIsland();await Wait(()=>assistant.IsVisible&&!(bool)Field("_assistantOpening")!);
                    Check(ReferenceEquals(assistant,Field("_assistant"))&&assistant.Opacity==1,"same AI page returns with fade-in completed");
                    Check(assistant.FindControl<TextBox>("InputBox")!.Text=="QA unsent draft; never send","AI draft retained after return");
                    await Wait(()=>island.IsVisible&&island.Current?.Key=="route/2");
                    Check(!assistant.IsVisible,"queued notification respects priority after resume gap");
                    typeof(App).GetMethod("OpenMusic",Flags)!.Invoke(this,new object?[]{false,null});
                    Check(island.IsVisible,"shortcut during notification waits for notification to finish");
                    island.HideIsland();await Wait(()=>((Window?)Field("_music"))?.IsVisible==true&&!(bool)Field("_musicOpening")!);
                    var music=(MusicIslandWindow)Field("_music")!;
                    var musicSession=typeof(MusicIslandWindow).GetField("_session",Flags)!.GetValue(music);
                    source.Inject(Card(3));await monitor.PollAsync();await Wait(()=>island.IsVisible&&island.Current?.Key=="route/3");
                    Check(!music.IsVisible,"notification preempts music page");
                    typeof(NotificationIslandWindow).GetMethod("BodyClick",Flags)!.Invoke(island,null);await Task.Delay(3500);
                    Check(island.IsVisible&&island.Pinned,"explicitly pinned notification remains until click");
                    Check(island.FindControl<TextBlock>("TimeLabel")!.Text?.Contains("已固定")==true,"pinned status explicitly explains no timeout");
                    Check(island.Height==136&&Math.Abs(island.FindControl<Border>("Island")!.Bounds.Height-music.FindControl<Border>("Island")!.Bounds.Height)<.1,"pinned notification keeps same 120 DIP capsule height as music");
                    island.HideIsland();await Wait(()=>music.IsVisible&&!(bool)Field("_musicOpening")!);
                    Check(ReferenceEquals(music,Field("_music"))&&ReferenceEquals(musicSession,typeof(MusicIslandWindow).GetField("_session",Flags)!.GetValue(music)),"same music page/session returns without mode reset or host replacement");
                    music.HideIsland();await Wait(()=>((HudWindow)Field("_hud")!).IsVisible);
                    Check(runtime.EffectiveSettings.AlwaysVisible,"ordinary AlwaysVisible HUD restored after notification/page close");
                    Check(!((bool)typeof(EndfieldChargePlus.Customization.CustomHudRuntime).GetField("_assistantActive",Flags)!.GetValue(runtime)!),"ordinary runtime released from assistant suspension");
                    source.Inject(Card(4));await monitor.PollAsync();await Wait(()=>island.IsVisible);
                    source.OverrideAccess=NotificationAccess.Denied;await monitor.PollAsync();
                    await Wait(()=>monitor.Access==NotificationAccess.Denied);
                    Check(!island.IsVisible&&island.Current is null,"permission revocation immediately hides private notification window");
                    Check(island.FindControl<TextBlock>("BodyLabel")!.Text=="","hidden notification clears private UI text");
                    var preview=(Task<string>)typeof(App).GetMethod("PreviewNotificationAsync",Flags)!.Invoke(this,null)!;
                    var previewResult=await preview;await Wait(()=>island.IsVisible);
                    Check(island.Current?.Key=="preview/local"&&previewResult.Contains("示範"),"local UI preview is explicitly labelled and works without Windows access");
                    await monitor.PollAsync();Check(island.IsVisible,"permission-denied monitor does not suppress harmless local preview");
                    var busyPreview=await (Task<string>)typeof(App).GetMethod("PreviewNotificationAsync",Flags)!.Invoke(this,null)!;
                    Check(busyPreview.Contains("請先"),"manual preview respects existing notification ownership");
                    island.HideIsland();source.OverrideAccess=null;await monitor.PollAsync();
                    await Wait(()=>monitor.Access==NotificationAccess.Allowed);
                    Check(!island.IsVisible&&monitor.Take() is null,"grant recovery does not replay old notification cards");
                    source.Inject(Card(5));await monitor.PollAsync();await Wait(()=>island.IsVisible&&island.Current?.Key=="route/5");
                    Check(island.FindControl<TextBlock>("TimeLabel")!.Text?.Contains("秒後收起")==true,"preview displays countdown or initial transition status");
                    await Wait(()=>!island.IsVisible);
                    Check(island.Current is null,"unhovered unpinned preview actually times out and clears text");
                    source.OverrideAccess=NotificationAccess.Denied;await monitor.PollAsync();monitor.Configure(false);
                    Call("OpenAssistant");await Wait(()=>assistant.IsVisible&&!(bool)Field("_assistantOpening")!);
                    personal.Handle("1秒後提醒我路由測試",DateTimeOffset.Now);
                    await Wait(()=>island.IsVisible&&island.Current?.Key.StartsWith("reminder/")==true);
                    Check(!assistant.IsVisible&&personal.Reminders[0].Displayed is not null,"real scheduled local reminder preempts AI without Windows permission");
                    Call("OnNotificationMonitorChanged");Check(island.IsVisible,"denied/disabled Windows notifications do not suppress local reminder");
                    await Wait(()=>assistant.IsVisible&&!(bool)Field("_assistantOpening")!);
                    Check(assistant.FindControl<TextBox>("InputBox")!.Text=="QA unsent draft; never send","timed reminder returns same AI draft after auto timeout");
                    Check(new EndfieldChargePlus.Assistant.PersonalAssistantStore(personalPath).Due(DateTimeOffset.Now.AddDays(2)).Count==0,"successful on-screen delivery durable across reload");
                    personal.Handle("1秒後提醒我取消測試",DateTimeOffset.Now);var cancelled=personal.Reminders.Last().Id;personal.DeleteReminder(cancelled);
                    await Task.Delay(6500);Check(!island.IsVisible,"deleted scheduled reminder cannot display");
                    var edited=personal.Reminders[0];personal.UpdateReminder(edited.Id,"重新排程測試",DateTimeOffset.Now.AddSeconds(1));
                    await Wait(()=>island.IsVisible&&island.Current?.Key=="reminder/"+edited.Id);
                    Check(personal.Reminders[0].Displayed is not null,"rescheduling same reminder ID can fire again in same run");
                    island.HideIsland();await Wait(()=>assistant.IsVisible&&!(bool)Field("_assistantOpening")!);
                    personal.Handle("記住我喜歡本機驗收",DateTimeOffset.Now);
                    Call("OpenMemoryPalace");await Wait(()=>((Window?)Field("_memoryPalace"))?.IsVisible==true&&!(bool)Field("_memoryOpening")!);
                    var palace=(MemoryPalaceWindow)Field("_memoryPalace")!;
                    Check(!assistant.IsVisible&&!music.IsVisible,"memory palace also replaces AI instead of overlaying it");
                    var editor=palace.GetVisualDescendants().OfType<TextBox>().First();editor.Text="未儲存記憶編輯草稿";
                    personal.Handle("1秒後提醒我記憶宮殿切換測試",DateTimeOffset.Now);
                    await Wait(()=>island.IsVisible&&island.Current?.Key.StartsWith("reminder/")==true);
                    Check(!palace.IsVisible,"notification preempts memory palace without layered windows");
                    await Wait(()=>palace.IsVisible&&!(bool)Field("_memoryOpening")!);
                    Check(ReferenceEquals(palace,Field("_memoryPalace"))&&editor.Text=="未儲存記憶編輯草稿","notification returns same palace and does not overwrite unsaved edit");
                    palace.Close();await Wait(()=>assistant.IsVisible&&!(bool)Field("_assistantOpening")!);
                    Check(overlapSamples==0,"AI/music/notification/ordinary HUD never overlap in sampled transitions");
                    Check(!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled||animatedSamples>0,"enabled animations have observed intermediate opacity frames");
                    Console.WriteLine($"{passed}/{passed} PASS; production App coordinator, synthetic notification inputs; zero LLM calls.");
                }
                catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}
                finally{timer.Stop();frameTimer.Stop();desktop.Shutdown(Environment.ExitCode);}
            });
    }
}
