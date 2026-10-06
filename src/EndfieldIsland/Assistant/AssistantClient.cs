using System.Text.Json;
using EndfieldChargePlus.Assistant.Native;

namespace EndfieldChargePlus.Assistant;

public sealed record ChatMessage(string role, string text);
public sealed record EvidenceSource(string id, string title, string url, string? source_type, string? published_at, string? updated_at);
public sealed record ContextInfo(int received_turns, int recent_complete_turns, bool older_excerpts, bool reduced);
public sealed record MemorySuggestion(string category, string quote, string? text = null, string? subject = null, string? stability = null, string? replace_id = null);
public sealed record PersonalAction(string kind, string quote, string text, string due, string id);
public sealed record AssistantReply(string text, string answer_kind, string? model, bool search_used,
    EvidenceSource[]? sources, ContextInfo? context, string? notice, MemorySuggestion[]? memory_suggestions = null, PersonalAction[]? personal_actions = null);
public sealed record SearchProviderInfo(string id, string name, bool hasKey, string reason);
public sealed record SearchServiceInfo(bool configured, SearchProviderInfo[] providers);

/// <summary>Presentation facade over the native assistant inside this process.</summary>
public sealed class AssistantClient : IDisposable
{
    public Task<AssistantReply> AskAsync(string text,IReadOnlyList<ChatMessage> history,string mode,CancellationToken token,ImageInput? image=null)=>Task.Run(async ()=>
    {
        try{return await NativeAssistantService.Shared.AskAsync(text,history,mode,token,image);}
        catch(AssistantFailure e){throw new InvalidOperationException(ErrorText(e.Code));}
    },token);
    public Task<string> UsageAsync(CancellationToken token)=>Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();
        try{
            var root=NativeAssistantService.Shared.Usage();var model=root["models"]!["gemini-3.5-flash-lite"]!;
            var used=J.N(model,"used_requests_today");var cap=J.N(root,"daily_local_request_limit");var reason=J.S(model,"locked_reason");
            var backups=NativeAssistantService.Shared.Backups.Status().Where(p=>p.Configured).Select(p=>(p.Id=="groq"?"Groq":"Cloudflare")+": "+LocalizationManager.Text(p.Reason=="ready"?"可使用":"請查看備援設定",p.Reason=="ready"?"ready":"check fallback settings")).ToArray();
            var count=J.B(root,"provider_managed_limits")?LocalizationManager.Text($"今日 {used} 次（本機紀錄） · 無本機限額",$"Today {used} requests (local record) · No local quota cap"):LocalizationManager.Text($"今日 {used}/{cap}（本機紀錄）",$"Today {used}/{cap} (local record)");
            return "Gemini 3.5 Lite · "+count+" · "+
                (!J.B(root,"key_present")?LocalizationManager.Text("尚未設定 Gemini Key","No Gemini key"):reason.Length>0?ErrorText(reason):LocalizationManager.Text("依官方 API 限額自動輪替","Automatic fallback follows provider limits"))+(backups.Length>0?" · "+string.Join(" · ",backups):"");
        }catch(AssistantFailure e){throw new InvalidOperationException(ErrorText(e.Code));}
    },token);
    public Task<SearchServiceInfo> SearchStatusAsync(CancellationToken token)=>Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();
        return NativeAssistantService.Shared.Search.Status().Deserialize<SearchServiceInfo>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("搜尋狀態不完整。");
    },token);
    public static string ErrorText(string? code) => code switch
    {
        "provider_daily_quota"=>LocalizationManager.Text("Gemini 今日額度已用完，於太平洋時間午夜恢復。","Gemini daily quota is exhausted; it resets at midnight Pacific time."),
        "provider_rate_limit"=>LocalizationManager.Text("Gemini 暫時限流，依 API 的重試時間恢復。","Gemini is rate-limited temporarily; recovery follows the API retry time."),
        "provider_auth"=>LocalizationManager.Text("Gemini 金鑰或權限有問題，請檢查設定。","Check the Gemini key and permissions."),
        "backup_key_invalid"=>LocalizationManager.Text("備援金鑰格式不正確；Groq Key 應以 gsk_ 開頭。","Invalid backup key format; Groq keys start with gsk_."),
        "backup_account_invalid"=>LocalizationManager.Text("Cloudflare 需要 32 碼 Account ID，請核對後儲存。","Cloudflare requires a 32-character Account ID. Check it before saving."),
        "backup_free_confirmation_required"=>LocalizationManager.Text("請先確認備援服務使用免費方案，且未啟用付費。","Confirm the backup providers use free plans with paid billing disabled."),
        "backup_storage_error"=>LocalizationManager.Text("AI 備援資料無法讀取；原檔保留，暫停寫入。","Unable to read AI fallback data. Existing files are preserved; writing is paused."),
        "backup_models_unavailable"=>LocalizationManager.Text("AI 主力與備援目前都無法使用。請在設定 → AI 模型備援檢查金鑰、權限與重試時間；只搜尋仍可使用。","Primary and backup AI are unavailable. Check keys, permissions and retry times in AI model fallback settings. Search only remains available."),
        "invalid_policy" or "invalid_verification_binding" or "usage_database_missing" or "existing_configuration_incomplete" => LocalizationManager.Text("本機 AI 設定或用量資料不完整，已停止請求。原資料保留，請檢查設定。", "Local AI policy or usage data is incomplete. Requests are stopped and existing data is preserved. Check settings."),
        "free_confirmation_required" => LocalizationManager.Text("金鑰格式不正確，或尚未確認免費方案。原設定保留。", "The key format is invalid or the free tier has not been confirmed. Existing settings are preserved."),
        "provider_temporary_unavailable" or "provider_http_500" or "provider_http_503" => LocalizationManager.Text("Gemini 暫時無法使用，請一分鐘後重試。", "Gemini is temporarily unavailable. Retry in one minute."),
        "invalid_assistant_response" or "invalid_search_plan" or "empty_model_response" => LocalizationManager.Text("Gemini 回覆格式不完整，請稍後再試。", "Gemini returned an incomplete response. Try again later."),
        "disabled" => LocalizationManager.Text("AI 尚未啟用。請在設定 → AI 助理貼上自己的金鑰，確認免費方案後套用。", "AI is not enabled. Open Settings → AI Assistant, paste your key, confirm the free tier and apply."),
        "api_key_not_configured_or_mismatch" => LocalizationManager.TranslateLiteral("尚未設定 Gemini。請在設定 → AI 助理輸入 API Key；也可切換「只搜尋」。"),
        "free_tier_verification_expired" => LocalizationManager.TranslateLiteral("免費方案驗證已到期，Gemini 已鎖定；已設定的搜尋 API 仍可使用。"),
        "daily_request_limit" or "daily_token_limit" => LocalizationManager.TranslateLiteral("今日 Gemini 本機用量已達上限；可切換「只搜尋」。"),
        "minute_request_limit" or "minute_input_token_limit" => LocalizationManager.TranslateLiteral("Gemini 本分鐘已達上限，請稍後再送出。"),
        "input_token_limit" => LocalizationManager.TranslateLiteral("這次內容超過上下文上限，請縮短訊息或開新對話。"),
        "invalid_image" or "image_dimensions_limit" => LocalizationManager.Text("圖片格式或尺寸不符，請重新貼上較小的截圖。","Image format or dimensions are unsupported. Paste a smaller screenshot."),
        "image_ocr_language_missing"=>LocalizationManager.Text("Windows 尚未安裝可用的 OCR 語言。請在 Windows 設定的語言選項安裝繁體中文或英文文字辨識。","No Windows OCR language is installed. Install Traditional Chinese or English text recognition in Windows language settings."),
        "image_ocr_no_text"=>LocalizationManager.Text("這張圖片沒有辨識到文字。請貼上文字較清楚的截圖。","No text was recognized. Paste a clearer text screenshot."),
        "image_ocr_too_much_text"=>LocalizationManager.Text("圖片文字太多，請分成較小範圍貼上。","Too much image text. Paste smaller sections."),
        "image_ocr_timeout"=>LocalizationManager.Text("Windows 文字辨識逾時，請縮小截圖後重試。","Windows OCR timed out. Try a smaller screenshot."),
        "image_ocr_unavailable"=>LocalizationManager.Text("Windows 本機文字辨識無法完成，請重新複製清楚的截圖。","Windows local OCR could not complete. Copy a clear screenshot again."),
        "assistant_busy" => LocalizationManager.TranslateLiteral("靈動島 AI 服務正在處理其他請求，請稍後再試。"),
        "unknown_usage_lock" or "provider_429_lock" or "reservation_exceeded_lock" => LocalizationManager.TranslateLiteral("Gemini 用量保護已鎖定；可使用已設定的搜尋 API。"),
        "local_auth_required" => LocalizationManager.Text("本機 AI 設定驗證失敗，請檢查金鑰。", "Local AI validation failed. Check the key."),
        _ => LocalizationManager.TranslateLiteral("靈動島 AI 服務目前無法完成請求。請檢查金鑰、網路或稍後再試。")
    };
    public void Dispose() { }
}
