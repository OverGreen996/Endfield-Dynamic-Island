using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EndfieldChargePlus.Notifications;

public sealed class NotificationMonitorProbe : Application
{
    private sealed class Source : INotificationSource
    {
        public NotificationRead Snapshot = new(NotificationAccess.Allowed, Array.Empty<NotificationCard>());
        public TaskCompletionSource<NotificationRead>? Hold;
        public int Reads, Requests;
        public bool Fail, RequestedOnUi;
        public Task<NotificationRead> ReadAsync(CancellationToken token)
        {
            Reads++;
            if (Fail) throw new InvalidOperationException("Fixture");
            return Hold?.Task ?? Task.FromResult(Snapshot);
        }
        public Task<NotificationAccess> RequestAccessAsync(CancellationToken token)
        { Requests++; RequestedOnUi = Dispatcher.UIThread.CheckAccess(); return Task.FromResult(NotificationAccess.Allowed); }
    }
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Dispatcher.UIThread.Post(async () =>
        {
            int passed=0;
            void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
            try
            {
                var source=new Source();using var monitor=new NotificationMonitor(source);
                NotificationCard Card(int id)=>NotificationCard.Create("qa/"+id,"app","title","body",DateTimeOffset.UtcNow);
                await monitor.PollAsync();Check(source.Reads==0,"disabled monitor performs no Windows API reads");
                source.Snapshot=new(NotificationAccess.Allowed,new[]{Card(1)});monitor.Configure(true);
                Check(monitor.Enabled&&monitor.Access==NotificationAccess.Allowed&&monitor.Take() is null,"enabled monitor establishes baseline without old card replay");
                Check(source.Requests==0,"startup never auto-requests Windows privacy permission");
                var first=source.Snapshot.Cards[0];source.Snapshot=new(NotificationAccess.Allowed,new[]{first,Card(2)});await monitor.PollAsync();
                Check(monitor.Take()?.Key=="qa/2","new notification delivered from monitor");
                await monitor.PollAsync();Check(monitor.Take() is null,"repeated reads do not deliver duplicates");
                source.Snapshot=new(NotificationAccess.Allowed,new[]{first,Card(3)});await monitor.PollAsync();
                source.Snapshot=new(NotificationAccess.Denied,Array.Empty<NotificationCard>());await monitor.PollAsync();
                Check(monitor.Access==NotificationAccess.Denied&&monitor.Take() is null,"revocation clears pending private cards");
                source.Snapshot=new(NotificationAccess.Allowed,new[]{Card(4)});await monitor.PollAsync();
                Check(monitor.Access==NotificationAccess.Allowed&&monitor.Take() is null,"permission recovery establishes new baseline");
                source.Snapshot=new(NotificationAccess.Allowed,new[]{Card(4),Card(5)});await monitor.PollAsync();
                monitor.Configure(false);Check(!monitor.Enabled&&monitor.Take() is null,"disabling clears notification queue");
                var reads=source.Reads;await monitor.PollAsync();Check(reads==source.Reads,"disabled polling remains idle");
                source.Hold=new(TaskCreationOptions.RunContinuationsAsynchronously);monitor.Configure(true);
                reads=source.Reads;await monitor.PollAsync();Check(reads==source.Reads,"concurrent polls are serialized");
                monitor.Configure(false);source.Hold.SetResult(new(NotificationAccess.Allowed,new[]{Card(6)}));await Task.Delay(30);
                Check(!monitor.Enabled&&monitor.Take() is null,"late read completion after disable cannot revive a card");
                source.Hold=null;monitor.Configure(true);Check(monitor.Take() is null,"reenabling resets seen state");
                source.Fail=true;await monitor.PollAsync();Check(monitor.Access==NotificationAccess.Unavailable&&monitor.Take() is null,"provider failure degrades to unavailable and clears private state");
                source.Fail=false;await monitor.PollAsync();Check(monitor.Access==NotificationAccess.Allowed&&monitor.Take() is null,"provider recovery is automatic without replay");
                await monitor.RequestAccessAsync();Check(source.Requests==1&&source.RequestedOnUi,"explicit permission request runs on UI thread");
                monitor.Dispose();reads=source.Reads;monitor.Configure(true);await monitor.PollAsync();Check(source.Reads==reads&&!monitor.Enabled,"disposed monitor cancels and remains idle");
                Console.WriteLine($"{passed}/{passed} PASS");
            }
            catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}
            finally{((IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!).Shutdown(Environment.ExitCode);}
        });
    }
}
