using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Assistant;

public sealed record ConversationTurn(string Question, AssistantReply Reply);

public sealed class AssistantSession
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EndfieldChargePlus.Assistant.v1");
    private readonly string _path;
    public List<ConversationTurn> Turns { get; private set; } = new();
    public bool OlderTurnsRemoved { get; private set; }
    public string? StorageNotice { get; private set; }

    public AssistantSession(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EndfieldChargePlus", "assistant-session.dpapi");
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 1500000) throw new InvalidDataException("Session size limit");
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            var turns = JsonSerializer.Deserialize<List<ConversationTurn>>(bytes) ?? new();
            if (turns.Count > 400 || turns.Any(t => string.IsNullOrWhiteSpace(t.Question) || string.IsNullOrWhiteSpace(t.Reply?.text)))
                throw new InvalidDataException("Invalid session");
            Turns = turns;
        }
        catch (Exception ex)
        {
            StorageNotice = "先前對話無法載入，檔案已保留。";
            AppLog.Error("Unable to load encrypted assistant session.", ex);
        }
    }

    public IReadOnlyList<ChatMessage> History() => Turns.SelectMany(t => new[]
    { new ChatMessage("user", t.Question), new ChatMessage("model", t.Reply.text) }).ToArray();
    // Local memory/reminder commands are displayed in chat but never re-imported as model background.
    public IReadOnlyList<ChatMessage> ModelHistory() => Turns.Where(t=>t.Reply.answer_kind!="local").SelectMany(t => new[]
    { new ChatMessage("user", t.Question), new ChatMessage("model", t.Reply.text) }).ToArray();

    public void Append(string question, AssistantReply reply)
    {
        Turns.Add(new ConversationTurn(question, reply));
        while (Turns.Count > 400 || JsonSerializer.SerializeToUtf8Bytes(History()).Length > 900000 ||
               JsonSerializer.SerializeToUtf8Bytes(Turns).Length > 1200000)
        { Turns.RemoveAt(0); OlderTurnsRemoved = true; }
        Save();
    }
    public void Clear() { Turns.Clear(); OlderTurnsRemoved = false; Save(); }
    private void Save()
    {
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Turns);
            File.WriteAllBytes(temp, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temp, _path, true);
            StorageNotice = null;
        }
        catch (Exception ex)
        {
            StorageNotice = "對話仍留在記憶體，但本次未成功儲存。";
            AppLog.Error("Unable to persist encrypted assistant session.", ex);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }
}
