using System.Globalization;

namespace EndfieldChargePlus.Assistant;

public sealed partial class PersonalAssistantStore
{
    public LocalAssistantResult? ApplyModelAction(string question, PersonalAction action, DateTimeOffset now)
    {
        if (action.quote.Length is < 1 or > 16000 || !question.Contains(action.quote, StringComparison.Ordinal)) return null;
        if (action.kind == "list_memories")
            return new(_state.Memories.Length == 0 ? LocalizationManager.Text("記憶宮殿目前是空的。","Your memory palace is empty.") :
                string.Join("\n", _state.Memories.Select(m => $"{m.Id} · {m.Category} · {m.Text}")));
        if (action.kind == "list_reminders")
            return new(_state.Reminders.Length == 0 ? LocalizationManager.Text("目前沒有定時提醒。","No reminders scheduled.") :
                string.Join("\n", _state.Reminders.OrderBy(r => r.Due).Select(r => $"{r.Id} · {r.Due.ToOffset(TimeSpan.FromHours(8)):MM/dd HH:mm} · {r.Text} · {(r.Displayed is null ? LocalizationManager.Text("待提醒","Pending") : LocalizationManager.Text("已顯示","Shown"))}")));
        if (action.kind is "delete_memory" or "delete_reminder")
        {
            if (!IdValid(action.id)) return null;
            if (action.kind == "delete_memory")
            {
                if (!_state.Memories.Any(m => m.Id == action.id)) return new(LocalizationManager.Text("找不到這筆記憶，沒有刪除資料。","Memory not found. Nothing was deleted."));
                DeleteMemory(action.id); return new(LocalizationManager.Text("已刪除指定記憶。","Selected memory deleted."));
            }
            if (!_state.Reminders.Any(r => r.Id == action.id)) return new(LocalizationManager.Text("找不到這筆提醒，沒有刪除資料。","Reminder not found. Nothing was deleted."));
            DeleteReminder(action.id); return new(LocalizationManager.Text("已取消指定提醒。","Selected reminder canceled."));
        }
        if (action.kind != "remind") return null;
        if (!DateTimeOffset.TryParseExact(action.due, ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var due) ||
            due.Offset != TimeSpan.FromHours(8) || due <= now || due > now.AddDays(366) || action.text.Trim().Length is < 1 or > 500)
            return new(LocalizationManager.Text("尚未建立提醒：時間或事項無效，請補充確定的時間與事項。","Reminder not created: provide a valid time and task."));
        var text = action.text.Trim();
        var existing = _state.Reminders.FirstOrDefault(r => r.Displayed is null && r.Due == due && r.Text == text);
        if (existing is not null) return new(LocalizationManager.Text($"提醒已存在：{due:yyyy/MM/dd HH:mm} · {text}，沒有重複建立。",$"Reminder already exists: {due:yyyy/MM/dd HH:mm} · {text}. No duplicate created."));
        var reminder = new PersonalReminder(NewId(), text, due, Created: now);
        var full = _state.Reminders.Length >= 300;
        Commit(_state with { Reminders = _state.Reminders.Append(reminder).TakeLast(300).ToArray() });
        return new(LocalizationManager.Text($"已設定提醒：{due:yyyy/MM/dd HH:mm} · {text}\n軟體運行時會用通知島提醒；關機期間不會響，重新啟動後會補顯示逾期提醒。",
            $"Reminder scheduled: {due:yyyy/MM/dd HH:mm} · {text}\nThe notification island will show it while the app is running. Overdue reminders appear after restarting.") +
            (full ? LocalizationManager.Text("\n已依建立順序移除最舊提醒。","\nThe oldest created reminder was removed.") : ""));
    }

    private PersonalMemory? SaveModelMemory(MemorySuggestion accepted, DateTimeOffset now)
    {
        var text = accepted.text!;
        var replacement = string.IsNullOrEmpty(accepted.replace_id) ? null : _state.Memories.FirstOrDefault(m => m.Id == accepted.replace_id);
        if (!string.IsNullOrEmpty(accepted.replace_id) && replacement is null) return null;
        var same = _state.Memories.FirstOrDefault(m => m.Category == accepted.category && string.Equals(m.Text, text, StringComparison.OrdinalIgnoreCase));
        if (same is not null) return null;
        if (replacement is null && _state.Memories.Length >= 200) throw new InvalidOperationException("記憶已達 200 筆，請先刪除不需要的內容。");
        var memory = new PersonalMemory(replacement?.Id ?? NewId(), accepted.category, text, "AI 理解本次對話 · 原文：" + accepted.quote, now);
        Commit(_state with { Memories = _state.Memories.Where(m => m.Id != memory.Id).Append(memory).ToArray() });
        return memory;
    }
}
