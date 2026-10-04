using System.Security.Cryptography;
using System.Text;

namespace EndfieldChargePlus.Notifications;

public sealed record NotificationCard(string Key, string App, string Title, string Body, DateTimeOffset Created, DateTimeOffset? Received = null)
{
    public static NotificationCard Create(string key, string app, string title, string body, DateTimeOffset created)
        => new(Limit(key, 256), Limit(app, 100), Limit(title, 240), Limit(body, 1600), created);
    private static string Limit(string text, int size)
    {
        text = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
        return text.Length > size ? text[..size] + "…" : text;
    }
    internal string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Title + "\n" + Body)));
}

/// <summary>Bounded, volatile notification delta queue. Never persists message content.</summary>
public sealed class NotificationInbox
{
    private Dictionary<string, string> _seen = new();
    private readonly List<(NotificationCard Card, DateTimeOffset Received)> _pending = new();
    private bool _initialized;
    public const int Capacity = 10;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(2);
    public int Count => _pending.Count;
    public bool Initialized => _initialized;
    public void Reset() { _seen.Clear(); _pending.Clear(); _initialized = false; }
    public void Sync(IEnumerable<NotificationCard> cards, DateTimeOffset now)
    {
        var current = cards.OrderByDescending(c => c.Created).Take(512).GroupBy(c => c.Key).Select(g => g.First()).ToArray();
        var next = current.ToDictionary(c => c.Key, c => c.Fingerprint);
        _pending.RemoveAll(p => !next.ContainsKey(p.Card.Key) || now - p.Received > MaximumAge);
        if (_initialized)
        {
            foreach (var card in current.OrderBy(c => c.Created))
            {
                bool alreadySeen = _seen.TryGetValue(card.Key, out var hash);
                if (alreadySeen && hash == next[card.Key]) continue;
                _pending.RemoveAll(p => p.Card.Key == card.Key);
                // Resurrected old records should not replay an old private message.
                // Windows can update a toast without changing its original CreationTime.
                // A known ID with changed text is a fresh update; only unknown old IDs are stale.
                if (!alreadySeen && (now - card.Created > MaximumAge || card.Created - now > TimeSpan.FromMinutes(1))) continue;
                _pending.Add((card with { Received = now }, now));
            }
            if (_pending.Count > Capacity) _pending.RemoveRange(0, _pending.Count - Capacity);
        }
        _seen = next; _initialized = true;
    }
    public NotificationCard? Take(DateTimeOffset now)
    {
        _pending.RemoveAll(p => now - p.Received > MaximumAge);
        if (_pending.Count == 0) return null;
        var card = _pending[0].Card; _pending.RemoveAt(0); return card;
    }
    public void Defer(NotificationCard card, DateTimeOffset now)
    {
        var received = card.Received ?? card.Created;
        if (!_seen.TryGetValue(card.Key, out var hash) || hash != card.Fingerprint || now - received > MaximumAge) return;
        _pending.RemoveAll(p => p.Card.Key == card.Key);
        _pending.Insert(0, (card, received));
        if (_pending.Count > Capacity) _pending.RemoveAt(_pending.Count - 1);
    }
}
