using System.Globalization;
using System.Text;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

// Regenerates Tools/WicBoRule20261015/bo-rows-classified.csv — the WIC-over-BO rule's row
// export — with the two defects of the first export fixed (2026-10-15, follow-up):
//
//   1. ENCODING: the file was UTF-8 WITHOUT a BOM and its umlauts had already been replaced
//      by U+FFFD upstream (a Windows-1252 read of UTF-8: "Rauscherm?hle"). Every value now
//      comes from the live database and the file is written as UTF-8 WITH BOM, so Excel and
//      PowerShell both show "Rauschermühle".
//   2. HEADER: the 15th column was labelled "HasRegional" while it actually holds the
//      agent's MAIN WIC LocationCode — one stray name, and every reader had to work around
//      it. The header now names every column exactly (…, HasMain, HasBackup, MainCode,
//      Classification, WicCentre, WorkingShift).
//
// Scope and classification are the ones the 2026-10-15 rule used (and the same numbers are
// reproduced: 926 rows / 15 / 39 / 386 / 482 / 4, 20 employees):
//   * every ShiftEntries row in [2026-10-15 .. 2027-02-28] with RawValue='BO' of an agent
//     holding an ACTIVE WIC assignment (MAIN / BACKUP / REGIONAL);
//   * MANUAL_RTM_BO / MANUAL_NON_BO / PLAN_BO_CONVERTED / PLAN_BO_NO_OPEN_CENTRE /
//     PLAN_BO_WICDUTY_ALREADY — the production rule objects decide, never a copy of them.
//
// READ-ONLY against the database; it only writes the CSV.

const string ConnectionString =
    @"Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;";

var outPath = args.Length > 0
    ? args[0]
    : @"C:\GSDDashboard_secure\Tools\WicBoRule20261015\bo-rows-classified.csv";

var from = new DateOnly(2026, 10, 15);
var to   = new DateOnly(2027, 2, 28);

var options = new DbContextOptionsBuilder<GSDContext>().UseSqlServer(ConnectionString).Options;
using var db = new GSDContext(options);

var employees   = db.Employees.ToList();
var empById     = employees.ToDictionary(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase);
var locations   = db.WicLocations.ToList();
var activeLocs  = locations.Where(l => l.IsActive).ToList();
var hours       = db.WicOpeningHours.ToList();
var assignments = db.WicAgentAssignments.ToList();
var index       = WicBackOfficePriority.BuildIndex(
    assignments.Where(a => a.IsActive), activeLocs, employees);

var rank = new[] { "MAIN", "BACKUP", "REGIONAL" };
string[] TiersOf(string? fullName) => assignments
    .Where(a => a.IsActive && string.Equals(a.EmployeeName, fullName, StringComparison.OrdinalIgnoreCase)
                && rank.Contains(a.AssignmentType, StringComparer.OrdinalIgnoreCase))
    .Select(a => a.AssignmentType.ToUpperInvariant())
    .Distinct()
    .ToArray();

WicLocation? ActiveMainOf(string? fullName)
{
    if (string.IsNullOrWhiteSpace(fullName)) return null;
    return assignments
        .Where(a => a.IsActive && string.Equals(a.EmployeeName, fullName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.AssignmentType, "MAIN", StringComparison.OrdinalIgnoreCase))
        .OrderBy(a => a.Id)
        .Select(a => activeLocs.FirstOrDefault(l => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, l)))
        .FirstOrDefault(l => l != null);
}

var rows = db.ShiftEntries
    .Where(s => s.ShiftDate >= from && s.ShiftDate <= to && s.RawValue == "BO")
    .ToList()
    .Where(s =>
    {
        var name = empById.GetValueOrDefault(s.EmployeeId)?.FullName;
        return !string.IsNullOrWhiteSpace(name) && TiersOf(name).Length > 0;
    })
    .OrderBy(s => s.EmployeeId).ThenBy(s => s.ShiftDate)
    .ToList();

var csv = new StringBuilder();
csv.AppendLine("Id,EmployeeId,FullName,ShiftDate,Dow,ShiftType,RawValue,AgentTask,Start,End," +
               "ManualChangeBy,ManualChangeAt,HasMain,HasBackup,MainCode,Classification,WicCentre,WorkingShift");

var counts = new Dictionary<string, int>();
foreach (var s in rows)
{
    var name = empById.GetValueOrDefault(s.EmployeeId)?.FullName ?? "";
    var tiers = TiersOf(name);

    string classification;
    WicLocation? centre = null;
    string? working = null;

    if (string.Equals(s.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase) || s.IsWicDuty)
    {
        // A real WIC_DUTY row that still carries the old RawValue='BO' — already WIC.
        classification = s.IsManualChange ? "MANUAL_NON_BO" : "PLAN_BO_WICDUTY_ALREADY";
    }
    else if (WicBackOfficePriority.IsManualBackOffice(s))
    {
        classification = "MANUAL_RTM_BO";     // RTM set BO by hand: BO stands
    }
    else if (s.IsManualChange)
    {
        classification = "MANUAL_NON_BO";     // leave / OFF / resigned the human set
    }
    else if (WicBackOfficePriority.IsPlanBackOffice(s))
    {
        centre = WicBackOfficePriority.PriorityCentre(index, s.EmployeeId, s.ShiftDate, hours);
        classification = centre == null ? "PLAN_BO_NO_OPEN_CENTRE" : "PLAN_BO_CONVERTED";
        if (centre != null)
        {
            var (cs, ce) = WicWorkingShift.CentreWindow(hours, centre, s.ShiftDate);
            working = WicWorkingShift.Slice(s.ShiftStart, s.ShiftEnd, cs, ce)
                      ?? WicWorkingShift.FallbackWindow(cs, ce);
        }
    }
    else
    {
        classification = "MANUAL_NON_BO";
    }

    counts[classification] = counts.GetValueOrDefault(classification) + 1;
    var main = ActiveMainOf(name);

    csv.AppendLine(string.Join(',',
        s.Id, s.EmployeeId, Csv(name), s.ShiftDate.ToString("yyyy-MM-dd"),
        s.ShiftDate.DayOfWeek.ToString(), s.ShiftType, Csv(s.RawValue), Csv(s.AgentTask),
        Csv(s.ShiftStart), Csv(s.ShiftEnd), Csv(s.ManualChangeBy),
        s.ManualChangeAt?.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "",
        tiers.Contains("MAIN") ? 1 : 0, tiers.Contains("BACKUP") ? 1 : 0,
        Csv(main?.LocationCode), classification, Csv(centre?.DisplayName), Csv(working)));
}

// UTF-8 WITH BOM: the whole point of the re-export.
File.WriteAllText(outPath, csv.ToString(), new UTF8Encoding(true));

Console.WriteLine($"rows: {rows.Count}");
foreach (var kv in counts.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Key,-26} {kv.Value,4}");
Console.WriteLine($"employees: {rows.Select(r => r.EmployeeId).Distinct().Count()}");
Console.WriteLine($"written: {outPath} (UTF-8 with BOM)");
return 0;

static string Csv(string? v) => v == null ? "" : (v.Contains(',') || v.Contains('"')
    ? "\"" + v.Replace("\"", "\"\"") + "\"" : v);
