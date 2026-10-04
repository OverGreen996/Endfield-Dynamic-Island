using Avalonia.Threading;
using EndfieldChargePlus.Assistant;

namespace EndfieldChargePlus;

public partial class App
{
    private PersonalAssistantStore _personalStore = PersonalAssistantStore.Shared;
    private DispatcherTimer? _personalTimer;
    private readonly HashSet<string> _remindersShownThisRun = new();
    private void InitializePersonalAssistant()
    {
        _personalTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        _personalTimer.Tick+=(_,_)=>TryPresentNotification();
        _personalStore.Changed+=OnPersonalChanged;
        _personalTimer.Start();
        Dispatcher.UIThread.Post(TryPresentNotification);
    }
    private void OnPersonalChanged()
    {
        var existing=_personalStore.Reminders.Select(ReminderOccurrence).ToHashSet();
        _remindersShownThisRun.RemoveWhere(key=>!existing.Contains(key));
        TryPresentNotification();
    }
    private bool IsLocalNotification => _notificationIsland?.Current?.Key.StartsWith("reminder/")==true;
    private static string ReminderOccurrence(PersonalReminder reminder) => reminder.Id+"/"+reminder.Due.UtcTicks;
    private PersonalReminder? NextReminder() => _personalStore.Due(DateTimeOffset.Now).FirstOrDefault(r=>!_remindersShownThisRun.Contains(ReminderOccurrence(r)));
    private void DisposePersonalAssistant()
    {
        _personalTimer?.Stop();_personalStore.Changed-=OnPersonalChanged;
    }
}
