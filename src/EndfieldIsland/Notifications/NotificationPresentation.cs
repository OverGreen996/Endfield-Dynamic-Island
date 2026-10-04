namespace EndfieldChargePlus.Notifications;

public sealed class NotificationPresentation
{
    public bool Visible { get; private set; }
    public bool Pinned { get; private set; }
    private DateTimeOffset _deadline;
    private double _duration;
    public void Show(DateTimeOffset now, double duration) { Visible = true; Pinned = false; _duration = double.IsFinite(duration) ? Math.Clamp(duration, 3, 10) : 6; _deadline = now.AddSeconds(_duration); }
    public double RemainingSeconds(DateTimeOffset now) => Visible && !Pinned ? Math.Max(0, (_deadline-now).TotalSeconds) : 0;
    public void Hide() { Visible = false; Pinned = false; }
    public void Click() { if (!Visible) return; if (Pinned) Hide(); else Pinned = true; }
    public bool Tick(DateTimeOffset now, bool hovering)
    {
        if (!Visible || Pinned) return false;
        if (hovering) _deadline = now.AddSeconds(_duration);
        if (now < _deadline) return false;
        Hide(); return true;
    }
}
