using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace EndfieldChargePlus;

public partial class App
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    /// <summary>Explicit local CLI smoke check; no notification contents enter the report.</summary>
    private async Task RunNotificationSelfTestAsync()
    {
        await Task.Delay(4000);
        if (_notificationsExiting) return;
        var marker = "Island self test " + Guid.NewGuid().ToString("N")[..8];
        var foreground = GetForegroundWindow();
        var accepted = _tray?.ShowNotification(marker, "本機 Windows 通知端到端驗收，不含私人資料。") == true;
        bool displayed = false, focusRetained = false;
        for (int i = 0; i < 50 && !_notificationsExiting; i++)
        {
            var card = _notificationIsland?.Current;
            if (card is not null && _notificationIsland?.IsVisible == true && (card.Title.Contains(marker) || card.Body.Contains(marker)))
            { displayed = true; focusRetained = GetForegroundWindow() == foreground; break; }
            await Task.Delay(400);
        }
        var report = new { submittedToWindows = accepted, displayedThroughWindowsReader = displayed,
            foregroundRetained = focusRetained, access = _notifications?.Access.ToString(),
            rawCount = _notifications?.LastRawCount, skippedCount = _notifications?.LastSkippedCount };
        try { await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "notification-self-test.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* A read-only installation still works normally. */ }
        if (displayed) _notificationIsland?.HideIsland();
    }
}
