using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace EndfieldChargePlus.Notifications;

public enum NotificationAccess { Allowed, Denied, Unspecified, Unavailable }
public sealed record NotificationRead(NotificationAccess Access, IReadOnlyList<NotificationCard> Cards, int RawCount = 0, int SkippedCount = 0);
public interface INotificationSource
{
    Task<NotificationRead> ReadAsync(CancellationToken token);
    Task<NotificationAccess> RequestAccessAsync(CancellationToken token);
}

public sealed class WindowsNotificationSource : INotificationSource
{
    private static NotificationAccess Access(UserNotificationListenerAccessStatus value) => value switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccess.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccess.Denied,
        _ => NotificationAccess.Unspecified,
    };
    public async Task<NotificationAccess> RequestAccessAsync(CancellationToken token)
    {
        // Invoked exclusively by the Settings button on the UI thread.
        try { return Access(await UserNotificationListener.Current.RequestAccessAsync().AsTask(token)); }
        catch (OperationCanceledException) { throw; }
        catch { return NotificationAccess.Unavailable; }
    }
    public async Task<NotificationRead> ReadAsync(CancellationToken token)
    {
        var listener = UserNotificationListener.Current;
        var access = Access(listener.GetAccessStatus());
        if (access != NotificationAccess.Allowed) return new(access, Array.Empty<NotificationCard>());
        var records = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(token);
        // A revoked permission can look like an empty list; check again after the read.
        access = Access(listener.GetAccessStatus());
        if (access != NotificationAccess.Allowed) return new(access, Array.Empty<NotificationCard>());
        var cards = new List<NotificationCard>();
        foreach (var record in records.OrderByDescending(r => r.CreationTime).Take(512))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var visual = record.Notification.Visual;
                var binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric) ?? visual.Bindings.FirstOrDefault();
                var texts = binding?.GetTextElements();
                if (texts is null || texts.Count == 0) continue;
                string appName = "Windows 通知";
                try
                {
                    // Unpackaged applications can have readable text but no usable AppInfo.
                    var name = record.AppInfo?.DisplayInfo?.DisplayName;
                    if (!string.IsNullOrWhiteSpace(name)) appName = name;
                }
                catch { /* Missing branding must not discard an otherwise readable notification. */ }
                cards.Add(NotificationCard.Create("windows/" + record.Id,
                    appName, texts[0].Text, string.Join("\n", texts.Skip(1).Select(t => t.Text)), record.CreationTime));
            }
            catch { /* One unsupported toast never prevents the remaining notifications. */ }
        }
        return new(access, cards, records.Count, Math.Min(records.Count, 512) - cards.Count);
    }
}
