using System.Text;
using System.Text.Json.Nodes;

namespace EndfieldChargePlus.Assistant.Native;

internal sealed class NativeGemini
{
    private readonly GeminiLedger _ledger;
    private readonly HttpClient _http;
    private readonly string _key;
    internal bool HasKey => NativeConfiguration.KeyValid(_key, _ledger.Policy);
    internal NativeGemini(GeminiLedger ledger, string key, HttpClient http)
    { _ledger = ledger; _key = key; _http = http; }

    internal async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages, string instructions,
        JsonObject? schema, ImageInput? image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!HasKey) throw new AssistantFailure("api_key_not_configured_or_mismatch");
        if (messages.Count is < 1 or > 64 || messages.Count % 2 != 1 || messages.Where((m, i) =>
            m.role != (i % 2 == 0 ? "user" : "model") || string.IsNullOrWhiteSpace(m.text)).Any())
            throw new AssistantFailure("invalid_messages");
        var contents = new JsonArray(messages.Select(m => (JsonNode)new JsonObject
            { ["role"] = m.role, ["parts"] = new JsonArray(new JsonObject { ["text"] = m.text }) }).ToArray());
        if (image is not null) { NativeBrain.ValidateImage(image); contents[^1]!["parts"]!.AsArray().Add(
            new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = image.mime_type, ["data"] = image.data } }); }
        var config = new JsonObject { ["candidateCount"] = 1, ["maxOutputTokens"] = J.N(_ledger.Policy, "maxOutputTokens"),
            ["responseModalities"] = new JsonArray("TEXT"), ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = "LOW" } };
        if (schema is not null) config["responseFormat"] = new JsonObject
            { ["text"] = new JsonObject { ["mimeType"] = "APPLICATION_JSON", ["schema"] = schema.DeepClone() } };
        var body = new JsonObject { ["contents"] = contents, ["generationConfig"] = config,
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = instructions }) } };
        if (Encoding.UTF8.GetByteCount(body.ToJsonString(J.Json)) > J.N(_ledger.Policy, "maxBodyBytes") + (image is null ? 0 : 2796204))
            throw new AssistantFailure("body_too_large");
        _ledger.Preflight();
        JsonObject counted;
        try { var countBody = (JsonObject)body.DeepClone(); countBody["model"] = "models/" + _ledger.Model;
            counted = await PostAsync("countTokens", new JsonObject { ["generateContentRequest"] = countBody }, token); }
        catch (ProviderResponseFailure e) { _ledger.PreflightFailure(e.Status,e.RetryMs,e.DailyQuota); throw new AssistantFailure("provider_http_" + e.Status); }
        token.ThrowIfCancellationRequested();
        var reservation = _ledger.Reserve(J.N(counted, "totalTokens"));
        JsonObject response;
        try { response = await PostAsync("generateContent", body, token); }
        catch (ProviderResponseFailure e) { _ledger.Fail(reservation, e.Status,e.RetryMs,e.DailyQuota); throw new AssistantFailure("provider_http_" + e.Status); }
        catch(OperationCanceledException)when(token.IsCancellationRequested){_ledger.Fail(reservation,0,0);throw;}
        catch { _ledger.Fail(reservation, 0); throw; }
        if (!_ledger.Finish(reservation, response["usageMetadata"])) throw new AssistantFailure("unknown_usage_lock");
        var parts = response["candidates"]?[0]?["content"]?["parts"] as JsonArray;
        var text = string.Concat(parts?.Where(p => p?["text"] is JsonValue && !J.B(p, "thought")).Select(p => J.S(p, "text")) ?? []);
        if (string.IsNullOrWhiteSpace(text)) throw new AssistantFailure("empty_model_response");
        return text;
    }
    private Task<JsonObject> PostAsync(string method, JsonObject body, CancellationToken token) =>
        NativeHttp.JsonAsync(_http, "https://generativelanguage.googleapis.com/v1beta/models/" + _ledger.Model + ":" + method,
            "x-goog-api-key", _key, body, token, 45000);
}
