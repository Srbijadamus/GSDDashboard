using GSDDashboard.API.Modules.Vacations;
using GSDDashboard.API.Services;
using System.Text.RegularExpressions;

namespace GSDDashboard.API.Modules.WicAssistant;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record AskRequest(string Question);

public record AssistantTableRow(
    string Employee, string EmployeeId,
    string Start, string End,
    int?   WorkDays, string WicLocation, string Role);

public record AssistantResponse(
    string AnswerText, string DateRangeChecked,
    AssistantTableRow[]? Table, string? Error);

// ─── Service ──────────────────────────────────────────────────────────────────

public class WicAssistantService(
    VacationService    vacSvc,
    WicCoverageService wicSvc,
    ILogger<WicAssistantService> logger)
{
    private const string BackendError  = "Could not retrieve data from the backend";
    private const string OutOfScopeMsg = "I can only answer questions about WIC annual leave and coverage.";

    public async Task<AssistantResponse> AskAsync(string rawQuestion)
    {
        var q = (rawQuestion ?? "").Trim();
        if (q.Length > 500) q = q[..500];

        ParsedQuery parsed;
        try   { parsed = IntentParser.Parse(q); }
        catch { return new AssistantResponse(OutOfScopeMsg, "", null, null); }

        if (parsed.Intent == Intent.OutOfScope)
            return new AssistantResponse(OutOfScopeMsg, "", null, null);

        var rangeStr = $"{parsed.From:yyyy-MM-dd} → {parsed.To:yyyy-MM-dd}";
        var fromStr  = parsed.From.ToString("yyyy-MM-dd");
        var toStr    = parsed.To.ToString("yyyy-MM-dd");

        List<AgentCoverageDto> agents;
        List<VacationDto>      vacations;
        try
        {
            agents    = await FetchWithRetry(() => wicSvc.GetAgentsAsync(null));
            vacations = await FetchWithRetry(() => vacSvc.GetVacationsAsync(fromStr, toStr, null, null, null));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WicAssistant fetch failed for: {Q}", q);
            return new AssistantResponse(BackendError, rangeStr, null, BackendError);
        }

        var agentById = agents
            .Where(a => !string.IsNullOrWhiteSpace(a.EmployeeId))
            .ToDictionary(a => a.EmployeeId!, StringComparer.OrdinalIgnoreCase);

        var wicVacs = vacations
            .Where(v => v.EmployeeId != null && agentById.ContainsKey(v.EmployeeId))
            .ToList();

        return parsed.Intent switch
        {
            Intent.ListLeave       => BuildListResponse(wicVacs, agentById, rangeStr),
            Intent.PersonLeave     => BuildPersonResponse(wicVacs, agentById, agents, rangeStr, parsed),
            Intent.LocationLeave   => BuildLocationResponse(wicVacs, agentById, rangeStr, parsed),
            Intent.CountOnDate     => BuildCountResponse(wicVacs, agentById, rangeStr, parsed),
            Intent.LowestCoverage  => BuildLowestCoverageResponse(wicVacs, agentById, agents, rangeStr, parsed),
            Intent.BackupCoverage  => BuildBackupCoverageResponse(wicVacs, agentById, agents, rangeStr, parsed),
            _                      => new AssistantResponse(OutOfScopeMsg, rangeStr, null, null)
        };
    }

    // ── Response builders ─────────────────────────────────────────────────────

    private static AssistantResponse BuildListResponse(
        List<VacationDto> wicVacs,
        Dictionary<string, AgentCoverageDto> agentById,
        string rangeStr)
    {
        var rows = ToTableRows(wicVacs, agentById);
        var text = rows.Length == 0
            ? $"No annual leave found for WIC agents ({rangeStr})."
            : $"{rows.Length} WIC agent{(rows.Length == 1 ? "" : "s")} on annual leave ({rangeStr}).";
        return new AssistantResponse(text, rangeStr, rows.Length > 0 ? rows : null, null);
    }

    private static AssistantResponse BuildPersonResponse(
        List<VacationDto> wicVacs,
        Dictionary<string, AgentCoverageDto> agentById,
        List<AgentCoverageDto> allAgents,
        string rangeStr,
        ParsedQuery parsed)
    {
        var hint    = parsed.PersonHint!;
        var matches = allAgents
            .Where(a => (a.FullName ?? "").Contains(hint, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return new AssistantResponse(
                $"No WIC agent found with that name (\"{hint}\").", rangeStr, null, null);

        if (matches.Count > 1)
        {
            var names = string.Join(", ", matches.Select(m => m.FullName));
            return new AssistantResponse(
                $"Multiple WIC agents match \"{hint}\": {names}. Please be more specific.",
                rangeStr, null, null);
        }

        var agent      = matches[0];
        var personVacs = wicVacs
            .Where(v => string.Equals(v.EmployeeId, agent.EmployeeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (personVacs.Count == 0)
            return new AssistantResponse(
                $"{agent.FullName} ({GetMainLocation(agent)}) is not on annual leave in the period {rangeStr}.",
                rangeStr, null, null);

        return new AssistantResponse(
            $"{agent.FullName} has annual leave in the period {rangeStr}.",
            rangeStr, ToTableRows(personVacs, agentById), null);
    }

    private static AssistantResponse BuildLocationResponse(
        List<VacationDto> wicVacs,
        Dictionary<string, AgentCoverageDto> agentById,
        string rangeStr,
        ParsedQuery parsed)
    {
        var hint    = parsed.LocationHint!;
        var locVacs = wicVacs.Where(v =>
        {
            if (!agentById.TryGetValue(v.EmployeeId!, out var ag)) return false;
            return ag.WicRoles.Any(r =>
                CleanLocation(r.DisplayName).Contains(hint, StringComparison.OrdinalIgnoreCase));
        }).ToList();

        var rows = ToTableRows(locVacs, agentById);
        var text = rows.Length == 0
            ? $"No WIC agents assigned to \"{hint}\" are on annual leave ({rangeStr})."
            : $"{rows.Length} agent{(rows.Length == 1 ? "" : "s")} from \"{hint}\" on annual leave ({rangeStr}).";
        return new AssistantResponse(text, rangeStr, rows.Length > 0 ? rows : null, null);
    }

    private static AssistantResponse BuildCountResponse(
        List<VacationDto> wicVacs,
        Dictionary<string, AgentCoverageDto> agentById,
        string rangeStr,
        ParsedQuery parsed)
    {
        var day   = parsed.From;
        var onDay = wicVacs.Where(v =>
            DateOnly.TryParse(v.FirstDay, out var fd) &&
            DateOnly.TryParse(v.LastDay,  out var ld) &&
            fd <= day && ld >= day).ToList();

        var dayStr = day.ToString("yyyy-MM-dd");
        var rows   = ToTableRows(onDay, agentById);
        var text   = rows.Length == 0
            ? $"No WIC agents on annual leave on {dayStr}."
            : $"{rows.Length} WIC agent{(rows.Length == 1 ? "" : "s")} on annual leave on {dayStr}.";
        return new AssistantResponse(text, dayStr, rows.Length > 0 ? rows : null, null);
    }

    private static AssistantResponse BuildLowestCoverageResponse(
        List<VacationDto> wicVacs,
        Dictionary<string, AgentCoverageDto> agentById,
        List<AgentCoverageDto> allAgents,
        string rangeStr,
        ParsedQuery parsed)
    {
        var mainIds = allAgents
            .Where(a => a.WicRoles.Any(r => r.AssignmentType == "MAIN"))
            .Select(a => a.EmployeeId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dayCounts = new Dictionary<DateOnly, int>();
        for (var d = parsed.From; d <= parsed.To; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            dayCounts[d] = wicVacs.Count(v =>
                mainIds.Contains(v.EmployeeId ?? "") &&
                DateOnly.TryParse(v.FirstDay, out var fd) &&
                DateOnly.TryParse(v.LastDay,  out var ld) &&
                fd <= d && ld >= d);
        }

        if (dayCounts.Count == 0)
            return new AssistantResponse($"No working days in range {rangeStr}.", rangeStr, null, null);

        var maxAbsent = dayCounts.Values.Max();
        if (maxAbsent == 0)
            return new AssistantResponse(
                $"No MAIN WIC agents are on annual leave on any working day in {rangeStr}. Full coverage.",
                rangeStr, null, null);

        var worstDays = dayCounts
            .Where(kv => kv.Value == maxAbsent)
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Key)
            .ToList();

        var dayList   = string.Join(", ", worstDays.Select(d => d.ToString("ddd dd MMM yyyy")));
        var worstSet  = worstDays.ToHashSet();
        var absentOnWorst = wicVacs
            .Where(v =>
                DateOnly.TryParse(v.FirstDay, out var fd) &&
                DateOnly.TryParse(v.LastDay,  out var ld) &&
                worstSet.Any(wd => fd <= wd && ld >= wd))
            .DistinctBy(v => v.EmployeeId)
            .ToList();

        var rows = ToTableRows(absentOnWorst, agentById);
        var text = $"Lowest coverage: {dayList} — {maxAbsent} MAIN agent{(maxAbsent == 1 ? "" : "s")} absent. Range: {rangeStr}.";
        return new AssistantResponse(text, rangeStr, rows.Length > 0 ? rows : null, null);
    }

    private static AssistantResponse BuildBackupCoverageResponse(
        List<VacationDto> wicVacs,
        Dictionary<string, AgentCoverageDto> agentById,
        List<AgentCoverageDto> allAgents,
        string rangeStr,
        ParsedQuery parsed)
    {
        // Start with anyone already confirmed on leave in range
        var onLeaveIds = wicVacs
            .Select(v => v.EmployeeId)
            .Where(id => id != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        // Determine whose locations need coverage
        List<AgentCoverageDto> awayAgents;
        if (parsed.PersonHint is not null)
        {
            var personAgent = allAgents.FirstOrDefault(a =>
                (a.FullName ?? "").Contains(parsed.PersonHint, StringComparison.OrdinalIgnoreCase));
            if (personAgent is null)
                return new AssistantResponse(
                    $"No WIC agent found matching \"{parsed.PersonHint}\".", rangeStr, null, null);
            awayAgents = new List<AgentCoverageDto> { personAgent };
            // Exclude this person from the available pool even if their vacation isn't recorded
            onLeaveIds.Add(personAgent.EmployeeId ?? "");
        }
        else
        {
            awayAgents = allAgents
                .Where(a => onLeaveIds.Contains(a.EmployeeId ?? "") &&
                            a.WicRoles.Any(r => r.AssignmentType == "MAIN"))
                .ToList();
            if (awayAgents.Count == 0)
                return new AssistantResponse(
                    $"No MAIN WIC agents on leave in {rangeStr}. Full coverage expected.", rangeStr, null, null);
        }

        // Build location → available agent map
        var locCoverage = new Dictionary<string, List<(AgentCoverageDto agent, string role)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var awayAgent in awayAgents)
            foreach (var r in awayAgent.WicRoles.Where(r => r.AssignmentType == "MAIN"))
            {
                var loc = CleanLocation(r.DisplayName ?? r.LocationCode);
                if (!locCoverage.ContainsKey(loc))
                    locCoverage[loc] = new List<(AgentCoverageDto, string)>();
            }

        foreach (var agent in allAgents)
        {
            if (onLeaveIds.Contains(agent.EmployeeId ?? "")) continue;
            foreach (var r in agent.WicRoles)
            {
                var loc = CleanLocation(r.DisplayName ?? r.LocationCode);
                if (!locCoverage.TryGetValue(loc, out var list)) continue;
                if (!list.Any(x => x.agent.EmployeeId == agent.EmployeeId))
                    list.Add((agent, r.AssignmentType));
            }
        }

        var rows = new List<AssistantTableRow>();
        foreach (var (locName, coverList) in locCoverage.OrderBy(kv => kv.Key))
        {
            if (coverList.Count == 0)
            {
                rows.Add(new AssistantTableRow("— NO COVER —", "",
                    parsed.From.ToString("yyyy-MM-dd"), parsed.To.ToString("yyyy-MM-dd"),
                    null, locName, "GAP"));
            }
            else
            {
                foreach (var (agent, role) in coverList
                    .OrderBy(x => x.role == "MAIN" ? 0 : x.role == "BACKUP" ? 1 : 2)
                    .ThenBy(x => x.agent.FullName))
                {
                    rows.Add(new AssistantTableRow(
                        agent.FullName ?? agent.EmployeeId ?? "",
                        agent.EmployeeId ?? "",
                        parsed.From.ToString("yyyy-MM-dd"),
                        parsed.To.ToString("yyyy-MM-dd"),
                        null, locName, role));
                }
            }
        }

        if (rows.Count == 0)
            return new AssistantResponse($"No coverage data found for {rangeStr}.", rangeStr, null, null);

        var locCount   = rows.Select(r => r.WicLocation).Distinct().Count();
        var gapCount   = rows.Count(r => r.Role == "GAP");
        var coverCount = rows.Count(r => r.Role != "GAP");
        var awayNames  = string.Join(", ", awayAgents.Select(a => a.FullName).Distinct());
        var text = gapCount > 0
            ? $"Coverage plan {rangeStr} (covering for {awayNames}): {coverCount} agent{(coverCount == 1 ? "" : "s")} available across {locCount - gapCount} location(s); {gapCount} location(s) have no backup."
            : $"Coverage plan {rangeStr} (covering for {awayNames}): {coverCount} agent{(coverCount == 1 ? "" : "s")} available across {locCount} location(s).";
        return new AssistantResponse(text, rangeStr, rows.ToArray(), null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AssistantTableRow[] ToTableRows(
        List<VacationDto> vacs,
        Dictionary<string, AgentCoverageDto> agentById)
    {
        return vacs
            .OrderBy(v => v.FirstDay)
            .Select(v =>
            {
                agentById.TryGetValue(v.EmployeeId!, out var ag);
                var role = ag?.WicRoles.FirstOrDefault(r => r.AssignmentType == "MAIN")?.AssignmentType
                        ?? ag?.WicRoles.FirstOrDefault()?.AssignmentType
                        ?? "";
                return new AssistantTableRow(
                    v.FirstName ?? v.EmployeeId ?? "",
                    v.EmployeeId ?? "",
                    v.FirstDay, v.LastDay,
                    v.WorkDaysNet,
                    ag is not null ? GetMainLocation(ag) : "",
                    role);
            })
            .ToArray();
    }

    private static string GetMainLocation(AgentCoverageDto agent)
    {
        var role = agent.WicRoles.FirstOrDefault(r => r.AssignmentType == "MAIN")
                ?? agent.WicRoles.FirstOrDefault();
        return role is null ? "" : CleanLocation(role.DisplayName);
    }

    internal static string CleanLocation(string name)
    {
        var clean = Regex.Replace(name ?? "", @"^(DE|NL)_", "", RegexOptions.IgnoreCase);
        return clean.Replace("_", " ").Trim();
    }

    private static async Task<T> FetchWithRetry<T>(Func<Task<T>> fetch)
    {
        try   { return await fetch(); }
        catch { await Task.Delay(1000); return await fetch(); }
    }
}

// ─── Intent / ParsedQuery ─────────────────────────────────────────────────────

internal enum Intent { ListLeave, PersonLeave, LocationLeave, CountOnDate, LowestCoverage, BackupCoverage, OutOfScope }

internal record ParsedQuery(
    Intent  Intent,
    DateOnly From, DateOnly To,
    string? PersonHint, string? LocationHint);

// ─── Intent parser ────────────────────────────────────────────────────────────

internal static class IntentParser
{
    // Keywords that mark the question as WIC-leave-related
    private static readonly string[] InScopeKw =
    [
        "leave", "urlaub", "annual", "absent", "absence", "away", "off", "wic",
        "vacation", "holiday", "frei", "abwesend", "coverage", "deckung",
        "who is", "wer ist", "wer hat", "wer ", "who ", "how many", "wie viele",
        "lowest", "fewest", "which day", "welcher tag", "urlaubs"
    ];

    // Words that indicate a date phrase (used to reject false-positive name matches)
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
            ["january"]=1,["januar"]=1,["jan"]=1,
            ["february"]=2,["februar"]=2,["feb"]=2,
            ["march"]=3,["maerz"]=3,["mar"]=3,
            ["april"]=4,["apr"]=4,
            ["may"]=5,["mai"]=5,
            ["june"]=6,["juni"]=6,["jun"]=6,
            ["july"]=7,["juli"]=7,["jul"]=7,
            ["august"]=8,["aug"]=8,
            ["september"]=9,["sep"]=9,["sept"]=9,
            ["october"]=10,["oktober"]=10,["oct"]=10,["okt"]=10,
            ["november"]=11,["nov"]=11,
            ["december"]=12,["dezember"]=12,["dec"]=12,["dez"]=12,
        };

    private static readonly Regex EuDateRx =
        new(@"(\d{1,2})\.(\d{1,2})\.(\d{4})?", RegexOptions.Compiled);
    private static readonly Regex IsoDateRx =
        new(@"\b(\d{4})-(\d{2})-(\d{2})\b", RegexOptions.Compiled);

    // ── Public entry point ────────────────────────────────────────────────────

    internal static ParsedQuery Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return OOS();

        var q = NormalizeUmlauts(raw.ToLowerInvariant().Trim());

        var (from, to) = ParseDateRange(q);

        if (!InScope(q) && !IsPureDate(q)) return OOS();

        // BackupCoverage intent: user wants to know who can cover a location while someone is away
        if (Regex.IsMatch(q, @"\bcover\b") || q.Contains("backup plan") ||
            q.Contains("abdeckung") || q.Contains("who can") || q.Contains("wer kann"))
            return new ParsedQuery(Intent.BackupCoverage, from, to, ExtractPerson(q), null);

        // Person intent
        var person = ExtractPerson(q);
        if (person is not null)
            return new ParsedQuery(Intent.PersonLeave, from, to, person, null);

        // Location intent
        var loc = ExtractLocation(q);
        if (loc is not null)
            return new ParsedQuery(Intent.LocationLeave, from, to, null, loc);

        // Count intent
        if (q.Contains("how many") || q.Contains("wie viele") ||
            q.Contains("count") || q.Contains("anzahl"))
            return new ParsedQuery(Intent.CountOnDate, from, to, null, null);

        // Lowest-coverage intent
        if (q.Contains("lowest") || q.Contains("fewest") || q.Contains("wenigsten") ||
            q.Contains("which day") || q.Contains("welcher tag") || q.Contains("worst day"))
            return new ParsedQuery(Intent.LowestCoverage, from, to, null, null);

        return new ParsedQuery(Intent.ListLeave, from, to, null, null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ParsedQuery OOS() =>
        new(Intent.OutOfScope, Today(), Today(), null, null);

    private static bool InScope(string q)
    {
        foreach (var kw in InScopeKw)
            if (q.Contains(kw)) return true;
        return false;
    }

    // A bare date like "24.07." should be treated as an in-scope query
    private static bool IsPureDate(string q)
    {
        var s = EuDateRx.Replace(IsoDateRx.Replace(q, ""), "")
                        .Replace("?", "").Replace(".", "")
                        .Replace(" ", "").Replace("/", "").Replace("-", "");
        return s.Length == 0 && (EuDateRx.IsMatch(q) || IsoDateRx.IsMatch(q));
    }

    private static string? ExtractPerson(string q)
    {
        // Ordered from most-specific to least-specific
        string[] patterns =
        [
            @"\bwhile\s+([a-z][a-z\s\-]{1,30}?)\s+is\s+(?:on\s+(?:annual\s+)?leave|away|absent|on\s+vacation|out)",
            @"\bis\s+([a-z][a-z\s\-]{1,30}?)\s+on\s+(?:annual\s+)?leave",
            @"\bis\s+([a-z][a-z\s\-]{1,30}?)\s+(?:vacation|away|absent|off)",
            @"\bist\s+([a-z][a-z\s\-]{1,30}?)\s+im\s+urlaub",
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

    private static (DateOnly from, DateOnly to) ParseDateRange(string q)
    {
        var today = Today();
        var year  = today.Year;

        // Two ISO dates → explicit range
        var isoM = IsoDateRx.Matches(q);
        if (isoM.Count >= 2 &&
            DateOnly.TryParse(isoM[0].Value, out var iA) &&
            DateOnly.TryParse(isoM[^1].Value, out var iB))
            return iA <= iB ? (iA, iB) : (iB, iA);

        // Single ISO date
        if (isoM.Count == 1 && DateOnly.TryParse(isoM[0].Value, out var iS))
            return (iS, iS);

        // Two EU dates → explicit range
        var euM = EuDateRx.Matches(q);
        if (euM.Count >= 2 &&
            TryEu(euM[0], year, out var eA) && TryEu(euM[^1], year, out var eB))
            return eA <= eB ? (eA, eB) : (eB, eA);

        // Single EU date
        if (euM.Count == 1 && TryEu(euM[0], year, out var eS))
            return (eS, eS);

        // Month name → whole month
        foreach (var (name, num) in MonthMap)
        {
            if (!Regex.IsMatch(q, @"\b" + Regex.Escape(name) + @"\b")) continue;
            var first = new DateOnly(year, num, 1);
            var last  = new DateOnly(year, num, DateTime.DaysInMonth(year, num));
            return (first, last);
        }

        // Relative phrases — most-specific first
        if (q.Contains("next two weeks") || q.Contains("naechsten zwei wochen") || q.Contains("next 2 weeks"))
            return (today, today.AddDays(14));

        if (q.Contains("next week") || q.Contains("naechste woche"))
        {
            int daysToMon = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            if (daysToMon == 0) daysToMon = 7;
            var mon = today.AddDays(daysToMon);
            return (mon, mon.AddDays(6));
        }

        if (q.Contains("this week") || q.Contains("diese woche"))
        {
            var mon = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            return (mon, mon.AddDays(6));
        }

        if (q.Contains("tomorrow") || q.Contains("morgen"))
            return (today.AddDays(1), today.AddDays(1));

        if (q.Contains("today") || q.Contains("heute"))
            return (today, today);

        // Default: today + 14 days
        return (today, today.AddDays(14));
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

    private static string NormalizeUmlauts(string s) =>
        s.Replace("ä","ae").Replace("ö","oe").Replace("ü","ue").Replace("ß","ss");

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private static bool IsStop(string s) =>
        Array.IndexOf(StopWords, s.ToLowerInvariant().Trim()) >= 0;

    private static bool HasDateWord(string s)
    {
        foreach (var w in s.ToLowerInvariant().Split(' '))
            if (Array.IndexOf(DateWords, w) >= 0) return true;
        return false;
    }
}

// ─── Endpoint mapper ──────────────────────────────────────────────────────────

public static class WicAssistantEndpointMapper
{
    public static void MapWicAssistantEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wic-assistant").WithTags("WicAssistant");
        grp.MapPost("/ask", async (AskRequest req, WicAssistantService svc) =>
            Results.Ok(await svc.AskAsync(req.Question ?? "")));
    }
}
