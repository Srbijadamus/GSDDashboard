namespace GSDDashboard.API.Modules.Assistant;

public class AssistantService(
    IEnumerable<IDomainHandler> handlers,
    ILogger<AssistantService>   logger)
{
    // German-only codepoints: U+00C4 Ä, U+00D6 Ö, U+00DC Ü, U+00E4 ä, U+00F6 ö, U+00FC ü, U+00DF ß
    private static readonly HashSet<int> _germanCp =
        new() { 0x00C4, 0x00D6, 0x00DC, 0x00E4, 0x00F6, 0x00FC, 0x00DF };

    private static readonly string[] _foreignKeywords =
    {
        "quelles", "ouverture", "bonjour", "heures", "merci",
        "quando",  "aperto",   "chiuso",
        "cuando",  "cuales",   "cuanto"
    };

    private static bool IsUnsupportedLanguage(string text)
    {
        foreach (var c in text)
        {
            int cp = (int)c;
            if (cp >= 0x0370 && cp <= 0x03FF) return true; // Greek
            if (cp >= 0x0400 && cp <= 0x04FF) return true; // Cyrillic
            if (cp >= 0x0600 && cp <= 0x06FF) return true; // Arabic / Farsi
            if (cp >= 0x0900 && cp <= 0x097F) return true; // Devanagari
            if (cp >= 0x3040 && cp <= 0x9FFF) return true; // Hiragana / Katakana / CJK
            if (cp >= 0xAC00 && cp <= 0xD7AF) return true; // Korean
        }
        // Spanish-only punctuation
        if (text.IndexOf('¿') >= 0 || text.IndexOf('¡') >= 0) return true; // ¿ ¡
        // Any non-ASCII character not in the German-allowed set
        foreach (var c in text)
            if ((int)c > 127 && !_germanCp.Contains((int)c)) return true;
        // Common Romance-language keywords that never appear in English or German
        var lower = text.ToLowerInvariant();
        foreach (var kw in _foreignKeywords)
            if (lower.Contains(kw)) return true;
        return false;
    }

    public async Task<AssistantResponse> AskAsync(string rawQuestion)
    {
        var q = (rawQuestion ?? "").Trim();
        if (q.Length > 500) q = q[..500];

        if (IsUnsupportedLanguage(q))
            return new AssistantResponse(
                "I can help you in English or German. Please ask your question in one of these languages.",
                "", null, null);

        var normalized = SharedParser.NormalizeUmlauts(q.ToLowerInvariant().Trim());

        var scores = handlers
            .Select(h => (handler: h, score: h.Score(normalized)))
            .OrderByDescending(x => x.score)
            .ToList();

        logger.LogInformation("AssistantRouter [{Q}] scores: {S}",
            q, string.Join(" | ", scores.Select(x => $"{x.handler.DomainKey}:{x.score}")));

        var nonzero = scores.Where(x => x.score > 0).ToList();

        if (nonzero.Count == 0)
            return new AssistantResponse(
                "I can help you in English or German. I can answer questions about: WIC opening hours, " +
                "WIC leave, agent availability, sick leave, AL balance, pipeline events, training sessions, " +
                "employees, WIC coverage/forecast, or today's dashboard summary. What would you like to know?",
                "", null, null);

        if (nonzero.Count >= 2 && nonzero[0].score == nonzero[1].score)
            return new AssistantResponse(
                $"Could you clarify — are you asking about {nonzero[0].handler.DomainLabel} or {nonzero[1].handler.DomainLabel}?",
                "", null, null);

        var winner = nonzero[0].handler;
        var parsed = SharedParser.Parse(q);

        try
        {
            return await winner.HandleAsync(parsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handler {Key} failed for: {Q}", winner.DomainKey, q);
            return new AssistantResponse(A.BackendError, "", null, A.BackendError);
        }
    }
}
