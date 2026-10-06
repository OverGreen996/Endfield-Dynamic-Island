using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant;

/// <summary>Validate AI decisions for shape/provenance; do not classify intent with trigger words.</summary>
internal static class PersonalMemoryFilter
{
    internal static MemorySuggestion? Validate(string question, MemorySuggestion? suggestion)
    {
        if (suggestion is null || !PersonalAssistantStore.CategoryValid(suggestion.category) ||
            suggestion.subject != "user" || suggestion.stability is not ("durable" or "task")) return null;
        var quote = suggestion.quote?.Trim() ?? "";
        var text = suggestion.text?.Trim() ?? "";
        if (quote.Length is < 1 or > 500 || text.Length is < 1 or > 500) return null;
        var position = question.IndexOf(quote, StringComparison.Ordinal);
        if (position < 0) return null;
        // Require an intact clause, so a selected substring cannot crop away its subject or negation.
        if (position > 0 && !Boundary(question[position - 1])) return null;
        var end = position + quote.Length;
        if (end < question.Length && !Boundary(quote[^1]) && !Boundary(question[end])) return null;
        if (ContainsSecret(quote) || ContainsSecret(text)) return null;
        return suggestion with { quote = quote, text = text };
    }
    internal static bool ContainsSecret(string text) => Regex.IsMatch(text,
        @"AIza[\w-]{20,}|AQ\.[\w-]{30,}|sk-[\w-]{20,}|gsk_[\w-]{16,}|\bBearer\s+[A-Za-z0-9_.-]{12,}", RegexOptions.IgnoreCase);
    private static bool Boundary(char value) => char.IsWhiteSpace(value) || "，,。；;：:！？!?、".Contains(value);
}
