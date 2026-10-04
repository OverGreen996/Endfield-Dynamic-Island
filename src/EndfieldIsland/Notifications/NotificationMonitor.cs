using Avalonia.Threading;

namespace EndfieldChargePlus.Notifications;

public sealed class NotificationMonitor : IDisposable
{
    private readonly INotificationSource _source;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly NotificationInbox _inbox = new();
    private CancellationTokenSource? _generation;
    private bool _reading, _disposed;
    public bool Enabled => _generation is not null;
    public NotificationAccess Access { get; private set; } = NotificationAccess.Unspecified;
    public int LastRawCount { get; private set; }
    public int LastSkippedCount { get; private set; }
    public string Status => !Enabled ? LocalizationManager.TranslateLiteral("通知顯示已停用") : Access switch
    {
        NotificationAccess.Allowed => _inbox.Initialized ? LocalizationManager.TranslateLiteral("已連接 Windows 通知 · 只顯示新通知") : LocalizationManager.TranslateLiteral("正在讀取 Windows 通知…"),
        NotificationAccess.Denied => LocalizationManager.TranslateLiteral("Windows 已拒絕通知讀取權限；請在 Windows 設定調整"),
        NotificationAccess.Unavailable => LocalizationManager.TranslateLiteral("此 Windows 環境暫時無法使用通知讀取 API"),
        _ => LocalizationManager.TranslateLiteral("尚未授權；請按「要求通知讀取權限」"),
    };
    public event Action? Changed;
    public NotificationMonitor(INotificationSource? source = null)
    {
        _source = source ?? new WindowsNotificationSource();
        _timer.Tick += async (_, _) => await PollAsync();
    }
    public void Configure(bool enabled)
    {
        if (_disposed || enabled == Enabled) return;
        _generation?.Cancel(); _generation?.Dispose(); _generation = null;
        _timer.Stop(); _inbox.Reset();
        if (enabled) { _generation = new(); _timer.Start(); _ = PollAsync(); }
        Changed?.Invoke();
    }
    public NotificationCard? Take() => _inbox.Take(DateTimeOffset.UtcNow);
    public void Defer(NotificationCard card) => _inbox.Defer(card, DateTimeOffset.UtcNow);
    public async Task<NotificationAccess> RequestAccessAsync()
    {
        // Must remain on UI thread; never auto-request privacy permission at launch.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var access = await _source.RequestAccessAsync(deadline.Token);
        if (!_disposed) { Access = access; await PollAsync(); Changed?.Invoke(); }
        return access;
    }
    public async Task PollAsync()
    {
        if (_reading || _disposed || _generation is null) return;
        _reading = true; var generation = _generation;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(generation.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var result = await _source.ReadAsync(deadline.Token);
            if (_disposed || !ReferenceEquals(generation, _generation)) return;
            Access = result.Access;
            LastRawCount = result.RawCount; LastSkippedCount = result.SkippedCount;
            if (Access == NotificationAccess.Allowed) _inbox.Sync(result.Cards, DateTimeOffset.UtcNow);
            else _inbox.Reset();
            Changed?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!_disposed && ReferenceEquals(generation, _generation))
            { Access = NotificationAccess.Unavailable; _inbox.Reset(); Changed?.Invoke(); }
        }
        finally { _reading = false; }
    }
    public void Dispose()
    {
        _disposed = true; _timer.Stop(); _generation?.Cancel(); _generation?.Dispose(); _generation = null; _inbox.Reset();
    }
}
