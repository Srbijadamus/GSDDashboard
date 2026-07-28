using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class AgentAvailabilityHandler(
    GSDContext           db,
    AvailabilityResolver resolver) : IDomainHandler
{
    public string DomainKey   => "agent-availability";
    public string DomainLabel => "agent availability";

    private static readonly string[] WicCities =
        ["brokdorf", "emmerthal", "stade", "stadland", "grafenrheinfeld", "essenbach"];

    public int Score(string q)
    {
        if (q.Contains("sick")     || q.Contains("krank"))         return 0;
        if (q.Contains("urlaub")   || q.Contains("vacation")      ||
            q.Contains("holiday")) return 0;
        if (q.Contains("pipeline"))                                return 0;
        if (q.Contains("training") || q.Contains("schulung"))     return 0;
        if (q.Contains("balance")  || q.Contains("urlaubskonto")) return 0;
        if (q.Contains("forecast") || q.Contains("prognose"))     return 0;

        // WIC opening-hours context → yield to WicOpeningHoursHandler
        bool hasHoursKw =
            q.Contains("open")        || q.Contains("offen")      ||
            q.Contains("geoeffnet")   || q.Contains("oeffnung")   ||
            q.Contains("hours")       || q.Contains("schedule")   ||
            q.Contains("geschlossen") || q.Contains("closed");
        if ((q.Contains("wic") || WicCities.Any(c => q.Contains(c))) && hasHoursKw) return 0;

        int score = 0;
        if (q.Contains("available") || q.Contains("verfuegbar"))          score += 80;
        if (q.Contains("free")      || q.Contains("frei"))                score += 70;
        if (q.Contains("working")   || q.Contains("arbeitet"))            score += 60;
        if (q.Contains("on shift")  || q.Contains("schicht"))             score += 60;

        return score;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var q    = parsed.NormalizedQ;
        bool de  = IsGerman(q);
        var date = (q.Contains("morgen") || q.Contains("tomorrow"))
            ? DateOnly.FromDateTime(DateTime.Today).AddDays(1)
            : parsed.DateWasExplicit
                ? parsed.From
                : DateOnly.FromDateTime(DateTime.Today);
        var label = date.ToString("yyyy-MM-dd");

        var emps  = await db.Employees.Where(e => e.IsActive).ToListAsync();
        var names = ExtractPersonNames(parsed.RawQuestion, emps);

        if (names.Count == 0)
            return de
                ? new AssistantResponse(
                    "Kein Mitarbeitername erkannt. Bitte nennen Sie den Namen des Mitarbeiters.",
                    label, null, null)
                : new AssistantResponse(
                    "No employee name was recognised. Please include the employee's name in your question.",
                    label, null, null);

        var sb = new StringBuilder();
        sb.AppendLine(de ? $"Verfügbarkeit am {label}:" : $"Availability for {label}:");
        sb.AppendLine();

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

        return new AssistantResponse(sb.ToString().TrimEnd(), label, null, null);
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

    private static bool IsGerman(string q)
    {
        if (q.Contains("heute")    || q.Contains("morgen")    ||
            q.Contains("verfuegbar") || q.Contains("arbeitet") ||
            q.Contains("schicht"))
            return true;
        return Regex.IsMatch(q, @"\b(sind|ist|wer|frei|welche)\b");
    }

    private static List<string> ExtractPersonNames(string rawQ, List<Employee> emps)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Are", "Is", "And", "Or", "The", "Who", "Which", "What", "When", "How",
            "Und", "Oder", "Sind", "Ist", "Wer", "Welche", "Was", "Wann",
            "Have", "Has", "Will", "Free", "Available", "Today", "Tomorrow",
            "Frei", "Heute", "Morgen", "WIC", "I", "A", "In", "On", "For",
            "Agents", "Agent", "Employees", "Employee", "Working", "Busy",
            "Please", "Thanks", "Thank", "Hi", "Hello", "Hey",
            "Brokdorf", "Emmerthal", "Stade", "Stadland", "Grafenrheinfeld", "Essenbach"
        };

        var candidates = rawQ
            .Split(new[] { ' ', ',', '?', '!', '.', '&', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && char.IsUpper(t[0]) && !stop.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            // Pure full-name scan fallback when no capitalised tokens are present
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var emp in emps)
            {
                if (emp.FirstName != null &&
                    rawQ.Contains(emp.FirstName, StringComparison.OrdinalIgnoreCase))
                    found.Add(emp.FirstName);
                else if (emp.FullName != null &&
                         rawQ.Contains(emp.FullName, StringComparison.OrdinalIgnoreCase))
                    found.Add(emp.FullName);
            }
            return found.ToList();
        }

        // Map each capitalised token to a known employee (partial match) or keep as-is
        // so unrecognised names produce an explicit "no record found" line.
        var result  = new List<string>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var emp = emps.FirstOrDefault(e =>
                string.Equals(e.FirstName, candidate, StringComparison.OrdinalIgnoreCase) ||
                (e.FullName ?? "").Contains(candidate, StringComparison.OrdinalIgnoreCase));
            var name = emp?.FirstName ?? candidate;
            if (seen.Add(name))
                result.Add(name);
        }
        return result;
    }
}
