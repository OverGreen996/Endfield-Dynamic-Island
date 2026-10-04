using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EndfieldChargePlus.Assistant;

public sealed record ChatMessage(string role, string text);
public sealed record EvidenceSource(string id, string title, string url, string? source_type, string? published_at, string? updated_at);
public sealed record ContextInfo(int received_turns, int recent_complete_turns, bool older_excerpts, bool reduced);
public sealed record MemorySuggestion(string category, string quote);
public sealed record AssistantReply(string text, string answer_kind, string? model, bool search_used,
    EvidenceSource[]? sources, ContextInfo? context, string? notice, MemorySuggestion[]? memory_suggestions = null);
public sealed record XngServiceInfo(bool connected, string? endpoint, string? core_version, string? rules_version, string? installation);

/// <summary>Presentation client only: query planning, evidence selection and quota live in the shared hubs.</summary>
public sealed class AssistantHubClient : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    { BaseAddress = new Uri("http://127.0.0.1:8890"), Timeout = TimeSpan.FromSeconds(110) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static string HubRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChatGPT", "GeminiHub");

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var auth = (await File.ReadAllTextAsync(Path.Combine(HubRoot, "data", "hub.token"), token)).Trim();
        if (auth.Length < 48) throw new InvalidDataException("共用 AI 服務的本機驗證資料不完整。");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    public async Task<AssistantReply> AskAsync(string text, IReadOnlyList<ChatMessage> history, string mode, CancellationToken token, ImageInput? image=null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/assistant")
        { Content = image is null ? JsonContent.Create(new { text, history, mode, search_mode = "normal" })
            : JsonContent.Create(new { text, history, mode, search_mode = "normal", image }) };
        using var response = await SendAsync(request, token);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!response.IsSuccessStatusCode)
        {
            var code = json.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "hub_error";
            throw new InvalidOperationException(ErrorText(code));
        }
        return json.RootElement.Deserialize<AssistantReply>(Json) ?? throw new InvalidDataException("AI 服務回覆格式不完整。");
    }

    public async Task<string> UsageAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/usage");
        using var response = await SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        var model = root.GetProperty("models").GetProperty("gemini-3.5-flash-lite");
        var used = model.GetProperty("used_requests_today").GetInt32();
        var cap = root.GetProperty("daily_local_request_limit").GetInt32();
        string? reason = model.GetProperty("locked_reason").GetString();
        return LocalizationManager.Text($"Gemini 3.5 Lite · 今日 {used}/{cap}（本機紀錄） · ",$"Gemini 3.5 Lite · Today {used}/{cap} (local record) · ") +
               (!root.GetProperty("key_present").GetBoolean() ? LocalizationManager.Text("尚未設定 Key，XNG 搜尋可用","No key; XNG search is available") : reason is not null ? ErrorText(reason) : LocalizationManager.Text("用量鎖已啟用","Usage protection enabled"));
    }

    public async Task<XngServiceInfo> XngStatusAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/xng/status");
        using var response = await SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<XngServiceInfo>(Json, token)
            ?? throw new InvalidDataException("XNG 連線狀態不完整。");
    }

    public static string ErrorText(string? code) => code switch
    {
        "disabled" => LocalizationManager.Text("AI 尚未啟用。請在設定 → AI 助理貼上自己的金鑰，確認免費方案後套用。", "AI is not enabled. Open Settings → AI Assistant, paste your key, confirm the free tier and apply."),
        "api_key_not_configured_or_mismatch" => LocalizationManager.TranslateLiteral("尚未設定 Gemini。請在設定 → AI 助理輸入 API Key；也可切換「只搜尋」。"),
        "free_tier_verification_expired" => LocalizationManager.TranslateLiteral("免費方案驗證已到期，Gemini 已鎖定；XNG 搜尋仍可使用。"),
        "daily_request_limit" or "daily_token_limit" => LocalizationManager.TranslateLiteral("今日 Gemini 本機用量已達上限；可切換「只搜尋」。"),
        "minute_request_limit" or "minute_input_token_limit" => LocalizationManager.TranslateLiteral("Gemini 本分鐘已達上限，請稍後再送出。"),
        "input_token_limit" => LocalizationManager.TranslateLiteral("這次內容超過上下文上限，請縮短訊息或開新對話。"),
        "invalid_image" or "image_dimensions_limit" => LocalizationManager.Text("圖片格式或尺寸不符，請重新貼上較小的截圖。","Image format or dimensions are unsupported. Paste a smaller screenshot."),
        "image_requires_gemini" => LocalizationManager.Text("圖片判讀需要 Gemini，請切換「自動」或「只聊天」。","Image analysis requires Gemini. Select Auto or Chat only."),
        "assistant_busy" => LocalizationManager.TranslateLiteral("共用 AI 服務正在處理其他請求，請稍後再試。"),
        "unknown_usage_lock" or "provider_429_lock" or "reservation_exceeded_lock" => LocalizationManager.TranslateLiteral("Gemini 用量保護已鎖定；可繼續使用 XNG 搜尋。"),
        "local_auth_required" => LocalizationManager.TranslateLiteral("本機 AI 服務驗證失敗，請確認 Gemini Hub 設定。"),
        _ => LocalizationManager.TranslateLiteral("共用 AI 服務目前無法完成請求。請確認 Gemini Hub 已啟動，或稍後再試。")
    };
    public void Dispose() => _http.Dispose();
}
