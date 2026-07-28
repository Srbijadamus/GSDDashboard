using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class WicOpeningHoursHandler(
    GSDContext           db,
    AvailabilityResolver resolver) : IDomainHandler
{
    public string DomainKey   => "wic-hours";
    public string DomainLabel => "WIC opening hours";

    private static readonly string[] WicCities =
        ["brokdorf", "emmerthal", "stade", "stadland", "grafenrheinfeld", "essenbach"];

    private static readonly Dictionary<string, DayOfWeek> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monday"]    = DayOfWeek.Monday,    ["montag"]     = DayOfWeek.Monday,
        ["tuesday"]   = DayOfWeek.Tuesday,   ["dienstag"]   = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["mittwoch"]   = DayOfWeek.Wednesday,
        ["thursday"]  = DayOfWeek.Thursday,  ["donnerstag"] = DayOfWeek.Thursday,
        ["friday"]    = DayOfWeek.Friday,    ["freitag"]    = DayOfWeek.Friday,
        ["saturday"]  = DayOfWeek.Saturday,  ["samstag"]    = DayOfWeek.Saturday,
        ["sunday"]    = DayOfWeek.Sunday,    ["sonntag"]    = DayOfWeek.Sunday,
    };

    public int Score(string q)
    {
        if (q.Contains("sick")     || q.Contains("krank"))           return 0;
        if (q.Contains("forecast") || q.Contains("prognose")        ||
            q.Contains("at risk"))                                   return 0;
        if (q.Contains("pipeline"))                                  return 0;
        if (q.Contains("training") || q.Contains("schulung"))       return 0;
        if (q.Contains("balance")  || q.Contains("urlaubskonto"))   return 0;
        if ((q.Contains("who covers") || q.Contains("backup")      ||
             q.Contains("main agent")) && q.Contains("wic"))        return 0;

        bool hasWic     = q.Contains("wic");
        bool hasWicCity = WicCities.Any(c => q.Contains(c));

        bool hasSpecificKw =
            q.Contains("open")           || q.Contains("offen")       ||
            q.Contains("geoeffnet")      || q.Contains("oeffnung")    ||
            q.Contains("oeffnungszeiten")|| q.Contains("hours")       ||
            q.Contains("schedule")       || q.Contains("geschlossen") ||
            q.Contains("closed")         ||
            ((hasWic || hasWicCity) && q.Contains("frei")); // "closed/free" in WIC context

        bool hasTemporalKw =
            q.Contains("wann")    || q.Contains("when")  ||
            q.Contains("show")    || q.Contains("zeige") ||
            q.Contains("compare") || q.Contains("vergleiche");

        bool hasHoursKw = hasSpecificKw ||
                          (hasTemporalKw && (hasWic || hasWicCity));

        bool hasLeaveKw =
            q.Contains("leave")  || q.Contains("urlaub")   ||
            q.Contains("absent") || q.Contains("away")     ||
            q.Contains("abwesend") || q.Contains("vacation") ||
            q.Contains("holiday");
        if (hasLeaveKw && !hasHoursKw) return 0;

        if (!hasHoursKw && !hasWic && !hasWicCity) return 0;

        int score = 0;
        if (hasWic || hasWicCity) score += 80;
        if (hasHoursKw)           score += 80;
        if ((hasWic || hasWicCity) && hasHoursKw) score += 20;

        return score;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var q     = parsed.NormalizedQ;
        bool de   = IsGerman(q);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var label = today.ToString("yyyy-MM-dd");

        var locs     = await db.WicLocations.Where(l => l.IsActive).OrderBy(l => l.DisplayName).ToListAsync();
        var allHours = await db.WicOpeningHours.ToListAsync();

        bool askNow   = q.Contains("now")   || q.Contains("jetzt");
        bool askToday = q.Contains("today") || q.Contains("heute");

        DayOfWeek? dayFilter = null;
        foreach (var (name, dow) in DayMap)
            if (q.Contains(name)) { dayFilter = dow; break; }

        var reqLocs = locs.Where(l =>
            q.Contains(l.DisplayName.ToLowerInvariant()) ||
            q.Contains(SharedParser.NormalizeUmlauts(l.DisplayName.ToLowerInvariant())) ||
            WicCities.Any(k => q.Contains(k) && l.DisplayName.ToLowerInvariant().Contains(k))
        ).ToList();

        if (reqLocs.Count == 0) reqLocs = locs;

        var sb = new StringBuilder();

        if (askNow)
            BuildNow(sb, reqLocs, allHours, today, de);
        else if (askToday && dayFilter == null)
            BuildToday(sb, reqLocs, allHours, today, de, label);
        else if (dayFilter.HasValue)
            BuildDay(sb, reqLocs, allHours, today, dayFilter.Value, de, IsYesNo(q));
        else
            BuildWeekly(sb, reqLocs, allHours, today, de);

        // If question also asks about named agents' availability, append that section
        // Use word-boundary regex for "frei" to avoid matching "freitag"
        bool hasAvailKw =
            q.Contains("available") || q.Contains("verfuegbar")          ||
            q.Contains("free")      || Regex.IsMatch(q, @"\bfrei\b")     ||
            q.Contains("working")   || q.Contains("arbeitet");

        if (hasAvailKw)
        {
            var names = ExtractPersonNames(parsed.RawQuestion, locs.Select(l => l.DisplayName).ToList());
            if (names.Count > 0)
                await AppendAvailability(sb, names, de, today, parsed);
        }

        return new AssistantResponse(sb.ToString().TrimEnd(), label, null, null,
            de ? "Quelle: WIC-Öffnungszeitendaten" : "Source: WIC schedule data");
    }

    private async Task AppendAvailability(StringBuilder sb, List<string> names, bool de, DateOnly today, AssistantParsedQuery parsed)
    {
        var q    = parsed.NormalizedQ;
        var date = (q.Contains("morgen") || q.Contains("tomorrow")) ? today.AddDays(1) :
                   parsed.DateWasExplicit ? parsed.From : today;

        var emps = await db.Employees.Where(e => e.IsActive).ToListAsync();
        sb.AppendLine();
        sb.AppendLine(de ? "**Verfügbarkeit:**" : "**Availability:**");

        foreach (var name in names)
        {
            var emp = emps.FirstOrDefault(e =>
                string.Equals(e.FullName,  name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.FirstName, name, StringComparison.OrdinalIgnoreCase) ||
                (e.FullName ?? "").Contains(name, StringComparison.OrdinalIgnoreCase));

            if (emp == null)
            {
                sb.AppendLine(de
                    ? $"• {name}: Kein Mitarbeitereintrag gefunden."
                    : $"• {name}: No employee record found.");
                continue;
            }

            var status = await resolver.GetStatusAsync(emp.EmployeeId, date);
            sb.AppendLine($"• {emp.FullName ?? name}: {FormatStatus(status, de)}");
        }
    }

    private static void BuildNow(StringBuilder sb, List<WicLocation> locs, List<WicOpeningHour> all, DateOnly today, bool de)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var dow = (int)today.DayOfWeek;

        sb.AppendLine(de
            ? $"WIC-Standorte jetzt ({today:yyyy-MM-dd}, {now:HH:mm}):"
            : $"WIC centers right now ({today:yyyy-MM-dd}, {now:HH:mm}):");

        foreach (var loc in locs)
        {
            var h = WicHoursResolver.Resolve(all, loc.LocationCode, loc.LocationCodeLegacy, dow, today);
            if (h == null || h.IsClosed)
            {
                sb.AppendLine($"• {loc.DisplayName}: {(de ? "Geschlossen." : "Closed.")}");
                continue;
            }

            bool in1     = InSession(now, h.OpenTime, h.CloseTime);
            bool in2     = h.OpenTime2 != null && InSession(now, h.OpenTime2, h.CloseTime2);
            bool inLunch = !in1 && !in2 && h.OpenTime2 != null &&
                           TimeOnly.TryParse(h.CloseTime, out var e1) &&
                           TimeOnly.TryParse(h.OpenTime2, out var s2) &&
                           now > e1 && now < s2;

            if (in1 || in2)
                sb.AppendLine($"• {loc.DisplayName}: {(de ? "Geöffnet" : "Open")} – {FormatHours(h)}");
            else if (inLunch)
                sb.AppendLine($"• {loc.DisplayName}: {(de ? "Mittagspause, öffnet wieder " : "Lunch break, reopens ")}{h.OpenTime2}");
            else
                sb.AppendLine($"• {loc.DisplayName}: {(de ? "Geschlossen." : "Closed.")}");
        }
    }

    private static void BuildToday(StringBuilder sb, List<WicLocation> locs, List<WicOpeningHour> all, DateOnly today, bool de, string label)
    {
        var dow     = (int)today.DayOfWeek;
        var dayName = de ? GermanDay(today.DayOfWeek) : today.DayOfWeek.ToString();

        sb.AppendLine(de ? $"Heute ist {dayName}, {label}:" : $"Today is {dayName}, {label}:");

        foreach (var loc in locs)
        {
            var h   = WicHoursResolver.Resolve(all, loc.LocationCode, loc.LocationCodeLegacy, dow, today);
            bool off = h == null || h.IsClosed;
            sb.AppendLine(off
                ? $"• {loc.DisplayName}: {(de ? "Geschlossen." : "Closed.")}"
                : $"• {loc.DisplayName}: {(de ? "Geöffnet" : "Open")} – {FormatHours(h!)}");
        }
    }

    private static void BuildDay(StringBuilder sb, List<WicLocation> locs, List<WicOpeningHour> all, DateOnly today, DayOfWeek day, bool de, bool yesNo)
    {
        var dayName = de ? GermanDay(day) : day.ToString();

        if (locs.Count == 1)
        {
            var loc = locs[0];
            var h   = WicHoursResolver.Resolve(all, loc.LocationCode, loc.LocationCodeLegacy, (int)day, today);
            bool off = h == null || h.IsClosed;

            if (yesNo)
                sb.AppendLine(off
                    ? (de ? $"Nein – WIC {loc.DisplayName} ist am {dayName} geschlossen."
                           : $"No – WIC {loc.DisplayName} is closed on {dayName}.")
                    : (de ? $"Ja – WIC {loc.DisplayName} ist am {dayName} geöffnet: {FormatHours(h!)}."
                           : $"Yes – WIC {loc.DisplayName} is open on {dayName}: {FormatHours(h!)}."));
            else
                sb.AppendLine(off
                    ? (de ? $"WIC {loc.DisplayName} ist am {dayName} geschlossen."
                           : $"WIC {loc.DisplayName} is closed on {dayName}.")
                    : (de ? $"WIC {loc.DisplayName} ist am {dayName} geöffnet: {FormatHours(h!)}."
                           : $"WIC {loc.DisplayName} is open on {dayName}: {FormatHours(h!)}."));
            return;
        }

        sb.AppendLine(de ? $"WIC-Standorte am {dayName}:" : $"WIC centers on {dayName}:");
        foreach (var loc in locs)
        {
            var h   = WicHoursResolver.Resolve(all, loc.LocationCode, loc.LocationCodeLegacy, (int)day, today);
            bool off = h == null || h.IsClosed;
            if (yesNo)
                sb.AppendLine(off
                    ? $"• {loc.DisplayName}: {(de ? "Nein – geschlossen." : "No – closed.")}"
                    : $"• {loc.DisplayName}: {(de ? "Ja – " : "Yes – ")}{FormatHours(h!)}");
            else
                sb.AppendLine(off
                    ? $"• {loc.DisplayName}: {(de ? "Geschlossen." : "Closed.")}"
                    : $"• {loc.DisplayName}: {FormatHours(h!)}");
        }
    }

    private static void BuildWeekly(StringBuilder sb, List<WicLocation> locs, List<WicOpeningHour> all, DateOnly today, bool de)
    {
        var days = new[]
        {
            (DayOfWeek.Monday,    de ? "Mo" : "Mon"),
            (DayOfWeek.Tuesday,   de ? "Di" : "Tue"),
            (DayOfWeek.Wednesday, de ? "Mi" : "Wed"),
            (DayOfWeek.Thursday,  de ? "Do" : "Thu"),
            (DayOfWeek.Friday,    de ? "Fr" : "Fri"),
            (DayOfWeek.Saturday,  de ? "Sa" : "Sat"),
            (DayOfWeek.Sunday,    de ? "So" : "Sun"),
        };

        foreach (var loc in locs)
        {
            sb.AppendLine(locs.Count > 1 ? $"**{loc.DisplayName}**"
                : (de ? $"WIC {loc.DisplayName} – Öffnungszeiten:" : $"WIC {loc.DisplayName} – Opening hours:"));

            foreach (var (dow, dayLabel) in days)
            {
                var h = WicHoursResolver.Resolve(all, loc.LocationCode, loc.LocationCodeLegacy, (int)dow, today);
                sb.AppendLine($"  {dayLabel}: {((h == null || h.IsClosed) ? (de ? "Geschlossen" : "Closed") : FormatHours(h))}");
            }
            if (locs.Count > 1) sb.AppendLine();
        }
    }

    private static string FormatHours(WicOpeningHour h)
    {
        var s = $"{h.OpenTime}–{h.CloseTime}";
        if (!string.IsNullOrEmpty(h.OpenTime2))
            s += $" / {h.OpenTime2}–{h.CloseTime2}";
        return s;
    }

    private static bool InSession(TimeOnly now, string? open, string? close)
        => TimeOnly.TryParse(open, out var s) && TimeOnly.TryParse(close, out var e) && now >= s && now <= e;

    private static bool IsYesNo(string q)
        => Regex.IsMatch(q, @"\b(is|are|ist|sind)\b") &&
           !q.StartsWith("when") && !q.StartsWith("wann") &&
           !q.Contains("show") && !q.Contains("zeige");

    private static string GermanDay(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday    => "Montag",    DayOfWeek.Tuesday  => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",  DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday    => "Freitag",   DayOfWeek.Saturday => "Samstag",
        DayOfWeek.Sunday    => "Sonntag",   _ => d.ToString()
    };

    private static bool IsGerman(string q)
    {
        if (q.Contains("wann")     || q.Contains("geoeffnet")   ||
            q.Contains("oeffnung") || q.Contains("geschlossen") ||
            q.Contains("heute")    || q.Contains("jetzt")       ||
            q.Contains("zeige")    || q.Contains("vergleiche")  ||
            q.Contains("montag")   || q.Contains("dienstag")    ||
            q.Contains("mittwoch") || q.Contains("donnerstag")  ||
            q.Contains("freitag")  || q.Contains("samstag")     ||
            q.Contains("sonntag")  || q.Contains("bitte"))
            return true;
        return Regex.IsMatch(q, @"\b(sind|ist|welche|alle|wer)\b");
    }

    private static string FormatStatus(AbsenceStatus s, bool de) => (s, de) switch
    {
        (AbsenceStatus.WORKING,     false) => "Available (working day).",
        (AbsenceStatus.WORKING,     true)  => "Verfügbar (Arbeitstag).",
        (AbsenceStatus.HALF_AL,     false) => "Partially available (half annual leave).",
        (AbsenceStatus.HALF_AL,     true)  => "Halb verfügbar (halber Urlaub).",
        (AbsenceStatus.SL,          false) => "Not available – sick leave.",
        (AbsenceStatus.SL,          true)  => "Nicht verfügbar – Krankenstand.",
        (AbsenceStatus.AL,          false) => "Not available – annual leave.",
        (AbsenceStatus.AL,          true)  => "Nicht verfügbar – Urlaub.",
        (AbsenceStatus.OFF,         false) => "Not working (day off).",
        (AbsenceStatus.OFF,         true)  => "Nicht im Dienst (freier Tag).",
        (AbsenceStatus.OFF_WEEKEND, false) => "Not working (weekend).",
        (AbsenceStatus.OFF_WEEKEND, true)  => "Nicht im Dienst (Wochenende).",
        (AbsenceStatus.PH,          false) => "Public holiday.",
        (AbsenceStatus.PH,          true)  => "Feiertag.",
        (AbsenceStatus.TRAINING,    false) => "In training.",
        (AbsenceStatus.TRAINING,    true)  => "Im Training.",
        (AbsenceStatus.RESIGNED,    false) => "No longer active.",
        (AbsenceStatus.RESIGNED,    true)  => "Nicht mehr aktiv.",
        (AbsenceStatus.UNKNOWN,     false) => "No shift data found – availability cannot be verified.",
        (AbsenceStatus.UNKNOWN,     true)  => "Keine Schichtdaten – Verfügbarkeit kann nicht geprüft werden.",
        _                                  => de ? "Status unbekannt." : "Unknown status."
    };

    private static List<string> ExtractPersonNames(string rawQ, List<string> knownLocations)
    {
        var knownLower = new HashSet<string>(knownLocations.Select(l => l.ToLowerInvariant()),
                                            StringComparer.OrdinalIgnoreCase);
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // English stop words
            "Are", "Is", "And", "Or", "The", "Who", "Which", "What", "When", "How",
            "Have", "Has", "Will", "Free", "Available", "Today", "Tomorrow",
            "WIC", "I", "A", "In", "On", "At", "For",
            "Open", "Closed", "Hours", "Now", "Show", "All",
            "Agents", "Agent", "Centers", "Center",
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
            // German stop words
            "Und", "Oder", "Sind", "Ist", "Wer", "Welche", "Was", "Wann",
            "Frei", "Heute", "Morgen", "Alle", "Zeige", "Mir", "Von", "Die", "Jetzt",
            "Oeffnungszeiten", "Hat", "Bitte", "Noch", "Dem", "Des",
            "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag",
        };

        return rawQ
            .Split(new[] { ' ', ',', '?', '!', '.', '&', '/', '(', ')' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && char.IsUpper(t[0]))
            .Where(t => !stop.Contains(t))
            .Where(t => !knownLower.Contains(t.ToLowerInvariant()))
            .ToList();
    }
}
