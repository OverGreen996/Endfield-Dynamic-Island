using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Music;
using Avalonia.Threading;

namespace EndfieldChargePlus;

public partial class App
{
    private NotificationMonitor? _notifications;
    private NotificationIslandWindow? _notificationIsland;
    private bool _notificationOpening, _notificationsExiting;
    private enum NotificationReturn { None, Assistant, Music, Memory }
    private NotificationReturn _notificationReturn;
    private MusicMode? _notificationReturnMode;
    private bool _notificationReturnOpenPlaylist;
    private DateTimeOffset _notificationNotBefore;
    private DispatcherTimer? _notificationRetry;
    private const string NotificationPreviewKey = "preview/local";
    private void InitializeNotifications(AppSettings settings)
    {
        _notifications = new NotificationMonitor();
        _notifications.Changed += OnNotificationMonitorChanged;
        ApplyNotificationSettings(settings);
        if (Environment.GetCommandLineArgs().Contains("--notification-self-test")) _ = RunNotificationSelfTestAsync();
    }
    private void OnNotificationMonitorChanged()
    {
        if (_notificationsExiting || _notifications is null) return;
        _settingsWindow?.RefreshNotificationStatus();
        if ((!_notifications.Enabled || _notifications.Access != NotificationAccess.Allowed) && !IsLocalNotification && _notificationIsland?.Current?.Key != NotificationPreviewKey) _notificationIsland?.HideIsland();
        TryPresentNotification();
    }
    private void ConnectNotificationSettings(SettingsWindow window)
    {
        window.SettingsApplied += ApplyNotificationSettings;
        window.NotificationStatus = () => _notifications?.Status ?? LocalizationManager.TranslateLiteral("通知顯示器正在初始化");
        window.RequestNotificationAccess = async () =>
        {
            if (_notifications is null) return LocalizationManager.TranslateLiteral("通知顯示器尚未就緒");
            var access = await _notifications.RequestAccessAsync();
            return access == NotificationAccess.Allowed ? LocalizationManager.TranslateLiteral("Windows 已允許通知讀取") : _notifications.Status;
        };
        window.SendWindowsTestNotification = () => _tray?.ShowNotification("靈動島通知測試 · " + DateTime.Now.ToString("HH:mm:ss.fff"), "這是一則 Windows 測試通知。點島固定，再點收起。") == true;
        window.PreviewNotification = PreviewNotificationAsync;
    }
    private async Task<string> PreviewNotificationAsync()
    {
        if (_runtime is null || _notificationsExiting) return LocalizationManager.TranslateLiteral("通知顯示器尚未就緒");
        if (_assistantOpening || _musicOpening || _memoryOpening || _notificationOpening || _notificationIsland?.IsVisible == true) return LocalizationManager.TranslateLiteral("請先收起目前的通知或等待切換完成，再預覽通知介面");
        _notificationOpening = true;
        try
        {
            await SuspendForNotificationAsync();
            if (_notificationsExiting) return LocalizationManager.TranslateLiteral("通知顯示器已關閉");
            _notificationIsland ??= CreateNotificationIsland();
            _notificationIsland.ShowNotification(NotificationCard.Create(NotificationPreviewKey, AppBrand.Name, "Windows 通知靈動島", "這是介面預覽，不是系統通知。點本體固定，再點收起。", DateTimeOffset.UtcNow), _runtime.EffectiveSettings);
            return LocalizationManager.TranslateLiteral("正在顯示本機示範卡片；這不代表已讀取 Windows 通知");
        }
        catch { return LocalizationManager.TranslateLiteral("通知介面預覽失敗，請稍後重試"); }
        finally { _notificationOpening = false; RestoreHud(); }
    }
    private void ApplyNotificationSettings(AppSettings settings)
    {
        _notificationIsland?.HideIsland();
        _notifications?.Configure(settings.HudEnabled && settings.NotificationsEnabled);
        _settingsWindow?.RefreshNotificationStatus();
    }
    private async void TryPresentNotification()
    {
        if (_notificationsExiting || _notificationOpening || _assistantOpening || _musicOpening || _memoryOpening || _runtime is null ||
            _notificationIsland?.IsVisible == true) return;
        if (DateTimeOffset.UtcNow < _notificationNotBefore) { ScheduleNotificationRetry(); return; }
        var reminder=NextReminder();
        var card = reminder is not null ? NotificationCard.Create("reminder/"+reminder.Id,LocalizationManager.TranslateLiteral("定時提醒"),reminder.Text,$"{reminder.Due.LocalDateTime:MM/dd HH:mm} · {(DateTimeOffset.Now-reminder.Due>TimeSpan.FromMinutes(1)?"逾期補提醒 · ":"")}點本體固定，再點收起。",DateTimeOffset.UtcNow)
            : _notifications?.Enabled==true&&_notifications.Access==NotificationAccess.Allowed ? _notifications.Take() : null;
        if (card is null) return;
        _notificationOpening = true;
        try
        {
            await SuspendForNotificationAsync();
            if (_notificationsExiting || (reminder is null && (_notifications?.Enabled!=true || _notifications.Access != NotificationAccess.Allowed)))
            { if(reminder is null)_notifications?.Defer(card); return; }
            if(reminder is not null && !_personalStore.Due(DateTimeOffset.Now).Any(r=>r.Id==reminder.Id))return;
            _notificationIsland ??= CreateNotificationIsland();
            _notificationIsland.ShowNotification(card, _runtime.EffectiveSettings);
            if(reminder is not null)
            {
                _remindersShownThisRun.Add(ReminderOccurrence(reminder));
                _personalStore.MarkDisplayed(reminder.Id,DateTimeOffset.Now);
            }
        }
        catch { /* Do not log notification contents or break the other islands. */ }
        finally { _notificationOpening = false; RestoreHud(); }
    }
    private NotificationIslandWindow CreateNotificationIsland()
    {
        var window = new NotificationIslandWindow();
        window.IslandHidden += () => { _notificationNotBefore = DateTimeOffset.UtcNow.AddMilliseconds(1200); RestoreHud(); ScheduleNotificationRetry(); };
        return window;
    }
    private async Task SuspendForNotificationAsync()
    {
        if (_assistant?.IsVisible == true)
        {
            _notificationReturn = NotificationReturn.Assistant;
            await _assistant.HideAnimatedAsync();
        }
        else if (_music?.IsVisible == true)
        {
            _notificationReturn = NotificationReturn.Music;
            await IslandTransition.FadeOutAsync(_music); _music.HideIsland();
        }
        else if (_memoryPalace?.IsVisible == true)
        {
            _notificationReturn=NotificationReturn.Memory;
            await IslandTransition.FadeOutAsync(_memoryPalace);_memoryPalace.Hide();
        }
        await _runtime!.BeginAssistantAsync();
    }
    private async void ResumeAfterNotification()
    {
        if (_notificationsExiting || _runtime is null || _notificationReturn == NotificationReturn.None) return;
        var target = _notificationReturn; _notificationReturn = NotificationReturn.None;
        var mode = _notificationReturnMode; bool open = _notificationReturnOpenPlaylist;
        _notificationReturnMode = null; _notificationReturnOpenPlaylist = false;
        // Use the same windows; conversation/input and music source remain intact.
        if (target == NotificationReturn.Assistant)
        {
            _assistantOpening = true;
            try
            {
                await _runtime.BeginAssistantAsync();
                if (_assistant is null) { _assistant = new AssistantIslandWindow(_personalStore); _assistant.IslandHidden += RestoreHud; _assistant.MusicRequested += () => OpenMusic(false); _assistant.MemoryRequested+=OpenMemoryPalace; }
                IslandTransition.PrepareShow(_assistant); _assistant.ShowInput(_runtime.EffectiveSettings); await IslandTransition.FadeInAsync(_assistant);
            }
            catch { _assistant?.HideIsland(); _runtime.EndAssistant(); }
            finally { _assistantOpening = false; }
        }
        else if(target==NotificationReturn.Memory)
        {
            _memoryOpening=true;
            try{await _runtime.BeginAssistantAsync();_memoryPalace??=CreateMemoryPalace();IslandTransition.PrepareShow(_memoryPalace);_memoryPalace.Show();_memoryPalace.Activate();await IslandTransition.FadeInAsync(_memoryPalace);}
            catch{_memoryPalace?.Hide();_runtime.EndAssistant();}
            finally{_memoryOpening=false;}
        }
        else
        {
            _musicOpening = true;
            try
            {
                await _runtime.BeginAssistantAsync();
                if (_music is null) { _music = new MusicIslandWindow(); _music.IslandHidden += RestoreHud; _music.SettingsRequested += () => { OpenSettingsWindow(); _settingsWindow?.SelectMusicTab(); }; }
                IslandTransition.PrepareShow(_music); _music.ShowMusic(_runtime.EffectiveSettings);
                if (mode == MusicMode.Browser) _music.FollowBrowser(); else if (open || mode == MusicMode.Playlist) _music.OpenSavedPlaylist();
                await IslandTransition.FadeInAsync(_music);
            }
            catch { _music?.HideIsland(); _runtime.EndAssistant(); }
            finally { _musicOpening = false; }
        }
        ScheduleNotificationRetry();
    }
    private void ScheduleNotificationRetry()
    {
        if (_notificationsExiting || _notificationRetry?.IsEnabled == true) return;
        _notificationRetry ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        _notificationRetry.Tick -= RetryNotifications;
        _notificationRetry.Tick += RetryNotifications;
        _notificationRetry.Start();
    }
    private void RetryNotifications(object? sender, EventArgs e) { _notificationRetry?.Stop(); TryPresentNotification(); }
    private void DisposeNotifications()
    {
        _notificationsExiting = true;
        _notificationRetry?.Stop(); _notificationReturn = NotificationReturn.None;
        _notifications?.Dispose(); _notifications = null;
        _notificationIsland?.Close(); _notificationIsland = null;
    }
}
