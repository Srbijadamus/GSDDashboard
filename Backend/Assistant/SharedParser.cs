using System.Text.RegularExpressions;

namespace GSDDashboard.API.Modules.Assistant;

internal static class SharedParser
{
    private static readonly string[] DateWords =
    [
        "next", "this", "last", "week", "weeks", "month",
        "naechste", "naechsten", "woche", "wochen",
        "heute", "morgen", "today", "tomorrow", "two", "zwei", "diese",
        "january", "february", "march", "april", "may", "june", "july",
        "august", "september", "october", "november", "december",
        "januar", "februar", "maerz", "juni", "juli", "dezember"
    ];

    private static readonly string[] StopWords =
    [
        "the", "a", "an", "on", "in", "at", "for", "with", "by",
        "who", "wer", "ist", "is", "be", "been",
        "annual", "leave", "urlaub", "next", "this", "week", "woche",
        "today", "tomorrow", "heute", "morgen"
    ];

    private static readonly Dictionary<string, int> MonthMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["january"]=1, ["januar"]=1,  ["jan"]=1,
            ["february"]=2,["februar"]=2, ["feb"]=2,
            ["march"]=3,   ["maerz"]=3,   ["mar"]=3,
            ["april"]=4,   ["apr"]=4,
            ["may"]=5,     ["mai"]=5,
            ["june"]=6,    ["juni"]=6,    ["jun"]=6,
            ["july"]=7,    ["juli"]=7,    ["jul"]=7,
            ["august"]=8,  ["aug"]=8,
            ["september"]=9, ["sep"]=9,   ["sept"]=9,
            ["october"]=10,  ["oktober"]=10, ["oct"]=10, ["okt"]=10,
            ["november"]=11, ["nov"]=11,
            ["december"]=12, ["dezember"]=12, ["dec"]=12, ["dez"]=12,
        };

    private static readonly Regex EuDateRx  = new(@"(\d{1,2})\.(\d{1,2})\.(\d{4})?", RegexOptions.Compiled);
    private static readonly Regex IsoDateRx = new(@"\b(\d{4})-(\d{2})-(\d{2})\b",    RegexOptions.Compiled);

    internal static string NormalizeUmlauts(string s) =>
        s.Replace("ä","ae").Replace("ö","oe").Replace("ü","ue").Replace("ß","ss");

    internal static AssistantParsedQuery Parse(string rawQ)
    {
        var q = NormalizeUmlauts((rawQ ?? "").ToLowerInvariant().Trim());
        var (from, to, explicit_) = ParseDateRange(q);
        return new AssistantParsedQuery(
            RawQuestion    : rawQ ?? "",
            NormalizedQ    : q,
            From           : from,
            To             : to,
            DateWasExplicit: explicit_,
            PersonHint     : ExtractPerson(q),
            LocationHint   : ExtractLocation(q),
            TeamLeadHint   : ExtractTeamLead(q),
            IsCountQuery   : q.Contains("how many") || q.Contains("wie viele") ||
                             q.Contains("count")    || q.Contains("anzahl"),
            IsLowestQuery  : q.Contains("lowest")    || q.Contains("fewest") ||
                             q.Contains("wenigsten") || q.Contains("which day") ||
                             q.Contains("welcher tag") || q.Contains("worst day"));
    }

    private static (DateOnly from, DateOnly to, bool explicit_) ParseDateRange(string q)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var year  = today.Year;

        var isoM = IsoDateRx.Matches(q);
        if (isoM.Count >= 2 &&
            DateOnly.TryParse(isoM[0].Value,  out var iA) &&
            DateOnly.TryParse(isoM[^1].Value, out var iB))
            return iA <= iB ? (iA, iB, true) : (iB, iA, true);

        if (isoM.Count == 1 && DateOnly.TryParse(isoM[0].Value, out var iS))
            return (iS, iS, true);

        var euM = EuDateRx.Matches(q);
        if (euM.Count >= 2 &&
            TryEu(euM[0], year, out var eA) && TryEu(euM[^1], year, out var eB))
            return eA <= eB ? (eA, eB, true) : (eB, eA, true);

        if (euM.Count == 1 && TryEu(euM[0], year, out var eS))
            return (eS, eS, true);

        foreach (var (name, num) in MonthMap)
        {
            // Word-boundary check prevents "summary" → "mar", "email" → "mai", etc.
            if (!Regex.IsMatch(q, @"\b" + Regex.Escape(name) + @"\b")) continue;
            var first = new DateOnly(year, num, 1);
            var last  = new DateOnly(year, num, DateTime.DaysInMonth(year, num));
            return (first, last, true);
        }

        if (q.Contains("next two weeks") || q.Contains("naechsten zwei wochen") || q.Contains("next 2 weeks"))
            return (today, today.AddDays(14), true);

        if (q.Contains("next week") || q.Contains("naechste woche"))
        {
            int daysToMon = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            if (daysToMon == 0) daysToMon = 7;
            var mon = today.AddDays(daysToMon);
            return (mon, mon.AddDays(6), true);
        }

        if (q.Contains("this week") || q.Contains("diese woche"))
        {
            var mon = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            return (mon, mon.AddDays(6), true);
        }

        if (q.Contains("tomorrow") || q.Contains("morgen"))
            return (today.AddDays(1), today.AddDays(1), true);

        if (q.Contains("today") || q.Contains("heute"))
            return (today, today, true);

        return (today, today.AddDays(14), false);
    }

    private static bool TryEu(Match m, int defaultYear, out DateOnly result)
    {
        result = default;
        if (!int.TryParse(m.Groups[1].Value, out var d) ||
            !int.TryParse(m.Groups[2].Value, out var mo)) return false;
        var yr = m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var y) ? y : defaultYear;
        try { result = new DateOnly(yr, mo, d); return true; }
        catch { return false; }
    }

    private static string? ExtractPerson(string q)
    {
        string[] patterns =
        [
            @"\bis\s+([a-z][a-z\s\-]{1,30}?)\s+on\s+(?:annual\s+)?leave",
            @"\bis\s+([a-z][a-z\-]{1,30}?)\s+(?:vacation|away|absent|off|sick|krank)",
            @"\bist\s+([a-z][a-z\s\-]{1,30}?)\s+im\s+urlaub",
            @"\bist\s+([a-z][a-z\s\-]{1,30}?)\s+krank",
            @"\bcheck\s+([a-z][a-z\s\-]{1,30})(?:\?|$)",
        ];
        foreach (var pat in patterns)
        {
            var m = Regex.Match(q, pat, RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var name = m.Groups[1].Value.Trim();
            if (name.Length < 2 || IsStop(name) || HasDateWord(name)) continue;
            return name;
        }
        return null;
    }

    private static string? ExtractLocation(string q)
    {
        string[] patterns =
        [
            @"\baway\s+in\s+([a-z][a-z\s\-]{1,30}?)(?:\s+(?:in|on|for|im|am|this|next)|\?|$)",
            @"\bwho\s+is\s+away\s+in\s+([a-z][a-z\s\-]{1,30}?)(?:\?|$|\s+in)",
            @"\bwho\s+is\s+in\s+([a-z][a-z\s\-]{1,30}?)\s+(?:on\s+leave|absent)",
            @"\bin\s+([a-z][a-z\s\-]{1,30}?)\s+(?:on\s+leave|im\s+urlaub|abwesend)",
            @"\bwer\s+ist\s+in\s+([a-z][a-z\s\-]{1,30}?)\s+im\s+urlaub",
        ];
        foreach (var pat in patterns)
        {
            var m = Regex.Match(q, pat, RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var loc = m.Groups[1].Value.Trim();
            if (loc.Length < 2 || IsStop(loc) || HasDateWord(loc)) continue;
            return loc;
        }
        return null;
    }

    private static string? ExtractTeamLead(string q)
    {
        string[] patterns =
        [
            @"\bteam\s*lead\s+([a-z][a-z\s\-]{1,30}?)(?:\?|$|\s+(?:team|agent|employee))",
            @"\bteamleiter\s+([a-z][a-z\s\-]{1,30}?)(?:\?|$)",
        ];
        foreach (var pat in patterns)
        {
            var m = Regex.Match(q, pat, RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var tl = m.Groups[1].Value.Trim();
            if (tl.Length < 2 || IsStop(tl) || HasDateWord(tl)) continue;
            return tl;
        }
        return null;
    }

    private static bool IsStop(string s) =>
        Array.IndexOf(StopWords, s.ToLowerInvariant().Trim()) >= 0;

    private static bool HasDateWord(string s)
    {
        foreach (var w in s.ToLowerInvariant().Split(' '))
            if (Array.IndexOf(DateWords, w) >= 0) return true;
        return false;
    }
}
