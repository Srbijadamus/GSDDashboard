using GSDDashboard.API.Services;
using System.Text.RegularExpressions;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class WicCoverageDetailHandler(
    WicCoverageService                 svc,
    ILogger<WicCoverageDetailHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "wic-coverage";
    public string DomainLabel => "WIC coverage roles";

    // Populated on first HandleAsync(); checked by Score() for bare location-name queries.
    private static volatile HashSet<string>? _knownLocNames;

    public int Score(string q)
    {
        if (q.Contains("who covers")        || q.Contains("wer deckt")          ||
            q.Contains("main agent")        || q.Contains("hauptagent")         ||
            q.Contains("backup b")          || q.Contains("backup agent")       ||
            q.Contains("who is assigned")   || q.Contains("wer ist zustaendig") ||
            q.Contains("coverage role")     || q.Contains("deckungsrolle")      ||
            q.Contains("who can cover")     || q.Contains("wer kann")           ||
            q.Contains("abdeckung"))
            return 90;
        if (Regex.IsMatch(q, @"\bcovers?\s"))
            return 90;
        if (q.Contains("coverage") && !q.Contains("wic coverage") && !q.Contains("wic deckung"))
            return 85;
        if (q.Contains("wic") && (q.Contains("backup") || q.Contains("main") ||
            q.Contains("role") || q.Contains("zustaendig")))
            return 85;
        if (q.Contains("backup") || q.Contains("zustaendig"))
            return 60;
        // Bare location name ("Demmin", "Hamburg", "Saffig") — cache populated after first request.
        var names = _knownLocNames;
        if (names != null && IsKnownLocation(names, q.Trim()))
            return 50;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var hint = parsed.LocationHint ?? ExtractCoverageCity(parsed.NormalizedQ) ?? ExtractLocationCode(parsed.NormalizedQ);

        List<WicListItemDto> wics;
        try   { wics = await A.FetchWithRetry(() => svc.GetWicsAsync(null)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "WicCoverage list fetch failed");
            return new AssistantResponse(A.BackendError, "current", null, A.BackendError);
        }

        EnsureNamesLoaded(wics);

        // Bare location name — Score() fired on cached data; now we have the DB-loaded hint.
        if (hint is null)
        {
            var t = parsed.NormalizedQ.Trim();
            var names = _knownLocNames;
            if (names != null && IsKnownLocation(names, t))
                hint = t;
        }

        if (hint is null)
        {
            var rows = wics.Select(w => new AssistantTableRow(
                w.DisplayName,
                w.LocationCode,
                $"Main: {w.MainCount}",
                $"Backup A: {w.BackupACount}",
                null,
                w.City ?? "",
                w.Bundesland ?? "")).ToArray();
            return new AssistantResponse(
                $"{wics.Count} WIC location{(wics.Count == 1 ? "" : "s")} found.",
                "current", rows.Length > 0 ? rows : null, null);
        }

        var matched = wics.Where(w =>
            WicLocationMatcher.MatchesQuery(hint, w.DisplayName, w.City, w.LocationCode)).ToList();

        if (matched.Count == 0)
            return new AssistantResponse(
                $"No WIC location found matching \"{hint}\".",
                "current", null, null);

        var detailRows = new List<AssistantTableRow>();
        foreach (var wic in matched.Take(3))
        {
            WicCoverageDto? detail;
            try   { detail = await A.FetchWithRetry(() => svc.GetWicByCodeAsync(wic.LocationCode)); }
            catch { detail = null; }
            if (detail is null) continue;
            AddTierRows(detailRows, detail.Main,    wic.DisplayName, "MAIN");
            AddTierRows(detailRows, detail.BackupA, wic.DisplayName, "BACKUP_A");
            AddTierRows(detailRows, detail.BackupB, wic.DisplayName, "BACKUP_B");
        }

        var ans = detailRows.Count > 0
            ? $"Coverage agents for \"{hint}\" ({matched.Count} location match{(matched.Count == 1 ? "" : "es")})."
            : $"No coverage agents found for \"{hint}\".";
        return new AssistantResponse(ans, "current", detailRows.Count > 0 ? detailRows.ToArray() : null, null);
    }

    private static void EnsureNamesLoaded(List<WicListItemDto> wics)
    {
        if (_knownLocNames != null) return;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in wics)
        {
            if (!string.IsNullOrWhiteSpace(w.City))
            {
                var city = WicLocationMatcher.StripUmlauts(w.City).ToLowerInvariant().Trim();
                if (city.Length >= 3) set.Add(city);
            }
            if (!string.IsNullOrWhiteSpace(w.DisplayName))
            {
                var disp = WicLocationMatcher.StripUmlauts(w.DisplayName).ToLowerInvariant().Trim();
                if (disp.Length >= 3) set.Add(disp);
            }
        }
        _knownLocNames = set;
    }

    // True if any cached name equals term exactly, or term is a prefix word of a cached compound name.
    private static bool IsKnownLocation(HashSet<string> names, string term)
    {
        if (term.Length < 3) return false;
        return names.Any(n =>
            n.Equals(term, StringComparison.OrdinalIgnoreCase) ||
            n.StartsWith(term + " ", StringComparison.OrdinalIgnoreCase) ||
            n.StartsWith(term + "-", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddTierRows(List<AssistantTableRow> list, AgentTierDto[] tier, string location, string tierLabel)
    {
        foreach (var a in tier)
            list.Add(new AssistantTableRow(
                a.Name,
                a.EmployeeId ?? "",
                location, "",
                null,
                tierLabel,
                a.PrimaryKid ?? ""));
    }

    private static string? ExtractCoverageCity(string q)
    {
        string[] patterns =
        [
            // Specific German forms first — must precede the generic "wer deckt" pattern to
            // prevent "demmin ab" being captured when "wer deckt X ab?" is the full phrase.
            @"\bwer\s+deckt\s+([a-z][a-z0-9\s\-\.]{1,40}?)\s+ab(?:\?|\s*$)",
            @"\bwer\s+kann\s+([a-z][a-z0-9\s\-\.]{1,40}?)\s+abdecken(?:\?|\s*$)",
            // "who covers X" / "who can cover X [tomorrow]" / generic "wer [kann] deckt X"
            @"\b(?:who\s+(?:can\s+)?covers?|wer\s+(?:kann\s+)?deckt?)\s+([a-z][a-z0-9\s\-\.]{1,40}?)(?:\?|tomorrow|today|\s*$)",
            // "coverage for X" / "agents for X"
            @"\b(?:coverage\s+for|agents?\s+for)\s+([a-z][a-z0-9\s\-\.]{1,40}?)(?:\?|\s*$)",
            // "cover X" / "covers X" (bare, no "who")
            @"\bcovers?\s+([a-z][a-z0-9\s\-\.]{1,40}?)(?:\?|tomorrow|today|\s*$)",
            // "X coverage"
            @"\b([a-z][a-z0-9\s\-\.]{1,40}?)\s+coverage(?:\?|\s*$)",
            // "coverage X"
            @"\bcoverage\s+([a-z][a-z0-9\s\-\.]{1,40}?)(?:\?|\s*$)",
            // "X abdecken" (generic — fires only after specific "wer kann X abdecken" above)
            @"\b([a-z][a-z0-9\s\-\.]{1,40}?)\s+abdecken(?:\?|\s*$)",
            // "X abdeckung"
            @"\b([a-z][a-z0-9\s\-\.]{1,40}?)\s+abdeckung(?:\?|\s*$)",
        ];
        foreach (var pat in patterns)
        {
            var m = Regex.Match(q, pat, RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var city = m.Groups[1].Value.Trim();
            if (city.Length >= 2) return city;
        }
        return null;
    }

    private static string? ExtractLocationCode(string q)
    {
        var m = Regex.Match(q, @"\b(de|nl)_\w+", RegexOptions.IgnoreCase);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }
}
