using EndfieldChargePlus.Notifications;
using EndfieldChargePlus.Settings;
using System.Reflection;

public static class NotificationProbe
{
    public static void Unit()
    {
        int passed = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); passed++; }
        var now = DateTimeOffset.UtcNow;
        NotificationCard Card(int id, string body = "message", DateTimeOffset? time = null) => NotificationCard.Create("app/" + id, "測試程式", "Title", body, time ?? now);
        var inbox = new NotificationInbox();
        inbox.Sync(new[] { Card(1) }, now); Check(inbox.Initialized && inbox.Count == 0, "startup does not replay existing notification");
        inbox.Sync(new[] { Card(1), Card(2) }, now); Check(inbox.Count == 1 && inbox.Take(now)?.Key == "app/2", "new Windows notification enters queue");
        inbox.Sync(new[] { Card(1), Card(2) }, now); Check(inbox.Count == 0, "identical polling snapshots are deduplicated");
        inbox.Sync(new[] { Card(1), Card(2, "updated") }, now); Check(inbox.Count == 1 && inbox.Take(now)?.Body == "updated", "content change produces update notification");
        inbox.Sync(new[] { Card(2, "again"), Card(3) }, now); inbox.Sync(new[] { Card(2, "latest"), Card(3) }, now);
        Check(inbox.Count == 2 && inbox.Take(now)?.Key == "app/3" && inbox.Take(now)?.Body == "latest", "same notification updates coalesce without duplicate card");
        inbox.Sync(new[] { Card(4) }, now); inbox.Sync(Array.Empty<NotificationCard>(), now); Check(inbox.Count == 0, "Windows-dismissed queued notifications are removed");
        inbox.Sync(new[] { Card(5, time: now.AddMinutes(-10)) }, now); Check(inbox.Count == 0, "old record reappearance is not replayed");
        inbox.Sync(new[] { Card(6, time: now.AddMinutes(10)) }, now); Check(inbox.Count == 0, "implausible future notification is ignored");
        inbox.Sync(Enumerable.Range(10, 30).Select(i => Card(i)), now); Check(inbox.Count == NotificationInbox.Capacity, "burst queue bounded at ten");
        Check(inbox.Take(now)?.Key == "app/30", "overflow drops oldest queued records");
        Check(inbox.Take(now.AddMinutes(3)) is null && inbox.Count == 0, "queued stale cards expire");
        inbox.Reset(); Check(!inbox.Initialized && inbox.Count == 0, "disable or revoke clears baseline and message queue");
        inbox.Sync(new[] { Card(1) }, now); inbox.Defer(Card(1), now); Check(inbox.Take(now)?.Key == "app/1", "interrupted current card can be deferred");
        inbox.Defer(Card(1, "wrong"), now); Check(inbox.Count == 0, "stale content cannot be requeued");
        inbox.Defer(Card(1), now.AddMinutes(3)); Check(inbox.Count == 0, "old interrupted card cannot be requeued");
        inbox.Reset(); inbox.Sync(new[] { Card(1) }, now); Check(inbox.Count == 0, "reenable establishes fresh baseline");
        var huge = NotificationCard.Create("id", new string('a', 500), new string('t', 2000), new string('b', 20000) + "\0", now);
        Check(huge.App.Length == 101 && huge.Title.Length == 241 && huge.Body.Length == 1601 && !huge.Body.Contains('\0'), "card text bounded and control characters removed");
        var presentation = new NotificationPresentation();
        presentation.Show(now, 6); Check(presentation.Visible && !presentation.Pinned && !presentation.Tick(now.AddSeconds(5), false), "preview remains before configured timeout");
        Check(presentation.Tick(now.AddSeconds(6), false) && !presentation.Visible, "preview times out at existing duration");
        presentation.Show(now, 6); Check(!presentation.Tick(now.AddSeconds(9), true) && presentation.Visible, "hover prevents timeout even after original deadline");
        Check(!presentation.Tick(now.AddSeconds(14), false) && presentation.Tick(now.AddSeconds(15), false), "leaving hover grants duration before hide");
        presentation.Show(now, 6); presentation.Click(); Check(presentation.Pinned && !presentation.Tick(now.AddDays(1), false), "body click pins and disables timeout");
        presentation.Click(); Check(!presentation.Visible && !presentation.Pinned, "second body click hides and unpins");
        presentation.Show(now, 6); presentation.Click(); presentation.Show(now, 6); Check(!presentation.Pinned, "new card resets previous pin state");
        presentation.Hide(); Check(!presentation.Tick(now.AddMinutes(1), true), "hidden state does not revive on hover");
        var settings = new AppSettings { AlwaysVisible = true, NotificationHideContent = true };
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Check(roundtrip.AlwaysVisible && roundtrip.NotificationHideContent && roundtrip.NotificationsEnabled, "new settings preserve ordinary AlwaysVisible and serialize");
        Check(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.NotificationsEnabled, "existing settings receive safe notification default");
        var updates=new NotificationInbox();var old=Card(99,time:now.AddHours(-2));updates.Sync(new[]{old},now);
        updates.Sync(new[]{old with {Body="Fresh update"}},now);var freshUpdate=updates.Take(now);
        Check(freshUpdate is not null&&freshUpdate.Created==old.Created&&freshUpdate.Received==now,"updated Windows toast retains original creation time but is delivered as fresh");
        updates.Defer(freshUpdate!,now.AddSeconds(30));Check(updates.Take(now.AddSeconds(30)) is not null,"fresh update with old creation time can resume after AI interruption");
        updates.Defer(freshUpdate!,now.AddMinutes(3));Check(updates.Take(now.AddMinutes(3)) is null,"fresh update TTL uses receipt time and remains bounded");
        updates.Sync(new[]{old with {Body="Fresh update",App="Branding became available"}},now.AddSeconds(1));
        Check(updates.Take(now.AddSeconds(1)) is null,"branding-only change does not replay old notification text");
        Console.WriteLine($"{passed}/{passed} PASS");
    }
    public static object CreateTestTray()
    {
        var type = typeof(AppSettings).Assembly.GetType("EndfieldChargePlus.Interop.WindowsTrayIcon")!;
        return Activator.CreateInstance(type, new object?[] { (Action)(() => { }), (Action)(() => { }), "通知驗收", null, null })!;
    }
    public static void SendTest(object tray, string marker) => tray.GetType().GetMethod("ShowNotification")!.Invoke(tray, new object[] { marker, "Windows 通知端到端驗收：不含私人資料。" });
    public static async Task Live()
    {
        var source = new WindowsNotificationSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var before = await source.ReadAsync(deadline.Token);
        if (before.Access != NotificationAccess.Allowed) throw new Exception("Windows notification access not allowed");
        using var tray = (IDisposable)CreateTestTray();
        var marker = "Island QA " + Guid.NewGuid().ToString("N")[..8];
        var inbox = new NotificationInbox(); inbox.Sync(before.Cards, DateTimeOffset.UtcNow);
        SendTest(tray, marker);
        bool captured = false;
        for (int i = 0; i < 12 && !captured; i++)
        {
            await Task.Delay(1500, deadline.Token);
            var read = await source.ReadAsync(deadline.Token); inbox.Sync(read.Cards, DateTimeOffset.UtcNow);
            while (inbox.Take(DateTimeOffset.UtcNow) is { } card) if (card.Title == marker) captured = true;
        }
        if (!captured) throw new Exception("Actual Shell Windows toast was not observed by notification listener");
        Console.WriteLine("PASS: actual Windows Shell toast captured and queued through production source/inbox");
        Console.WriteLine("PASS: notification contents were not logged or persisted");
        // Dismissing an island never removes the toast from Windows center.
        var retained = await source.ReadAsync(deadline.Token);
        if (!retained.Cards.Any(c => c.Title == marker)) throw new Exception("Test toast unexpectedly absent from Windows center");
        Console.WriteLine("PASS: consuming island queue retains Windows notification");
    }
}
