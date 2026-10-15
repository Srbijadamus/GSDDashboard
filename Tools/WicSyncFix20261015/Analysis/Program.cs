using System.Globalization;
using System.Text;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

// READ-ONLY analysis for the 2026-10-15 "WIC_DUTY row without its on-site WicShiftEntries row"
// task. It performs SELECTs only; no write, no deploy. Console + CSV output, no employee names
// in the console summary (CSV carries names for the operators' own record).
//
// Scope (task 1): every ShiftEntries row with a WIC DUTY from today (2026-10-15) to 2027-02-28,
// at a centre that is OPEN per WicHoursResolver, that has NO on-site WicShiftEntries row:
//   * WIC_DUTY by type  (ShiftType='WIC_DUTY' / IsWicDuty=1)
//   * the WIC_DUTY encoding (WORKING whose AgentTask names an active WIC centre)
//   * PLAN_BO_CONVERTED — a plan back-office day (RawValue='BO' / ShiftType='BO', no
//     ManualChangeBy) whose agent's priority centre is OPEN (the live WIC>BO rule)
// Grouped by writer (SourceModule / RosterBatch / ManualChangeBy).

const string ConnectionString =
    @"Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;";

var outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outDir);

var from = new DateOnly(2026, 10, 15);
var to   = new DateOnly(2027, 2, 28);

var options = new DbContextOptionsBuilder<GSDContext>().UseSqlServer(ConnectionString).Options;
using var db = new GSDContext(options);

var employees   = db.Employees.ToList();
var empById     = employees.ToDictionary(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase);
var locations   = db.WicLocations.Where(l => l.IsActive).ToList();
var allHours    = db.WicOpeningHours.ToList();
var assignments = db.WicAgentAssignments.Where(a => a.IsActive).ToList();
var index       = WicBackOfficePriority.BuildIndex(assignments, locations, employees);

var shifts = db.ShiftEntries
    .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
    .ToList();

var wicRows = db.WicShiftEntries
    .Where(w => w.ShiftDate >= from && w.ShiftDate <= to)
    .ToList();

var batches = db.RosterBatches
    .Where(b => b.DateFrom <= to && b.DateTo >= from)
    .ToDictionary(b => b.Id);

var absenceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "AL", "SL", "UL", "OL", "PH", "LPH", "HALF_AL", "OFF", "OFF_WEEKEND", "CD", "RESIGNED", "TRAINING", "EMPTY" };

WicLocation? CentreFor(ShiftEntry s)
{
    // The centre named by the row itself (the WIC_DUTY / flipped-type encoding).
    if (!string.IsNullOrWhiteSpace(s.AgentTask))
    {
        var byTask = locations.FirstOrDefault(l => WicLocationMatcher.MatchesAgentTask(s.AgentTask, l));
        if (byTask != null) return byTask;
    }
    // A plan BO row carries no centre: it comes from the assignment priority rule.
    if (WicBackOfficePriority.IsPlanBackOffice(s))
        return WicBackOfficePriority.PriorityCentre(index, s.EmployeeId, s.ShiftDate, allHours);
    return null;
}

bool IsWicDutyRow(ShiftEntry s)
{
    if (string.Equals(s.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase)) return true;
    if (s.IsWicDuty) return true;
    // the flipped-type encoding: WORKING (+ centre name in AgentTask) is WIC duty
    if (string.Equals(s.ShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(s.AgentTask)
        && locations.Any(l => WicLocationMatcher.MatchesAgentTask(s.AgentTask, l))) return true;
    return WicBackOfficePriority.IsPlanBackOffice(s);
}

string Kind(ShiftEntry s)
{
    if (string.Equals(s.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase) || s.IsWicDuty)
        return "WIC_DUTY_TYPE";
    if (WicBackOfficePriority.IsPlanBackOffice(s)) return "PLAN_BO_CONVERTED";
    return "WIC_DUTY_ENCODING";
}

var miss = new List<Miss>();
var noCentre = new List<(ShiftEntry Row, string Kind)>();
var closedCentre = new List<(ShiftEntry Row, string Kind, WicLocation Loc)>();
var allKindCounts = new Dictionary<string, int>();
var okRows = 0;
var absentWicDuty = 0;
var manualBo = 0;
var halfAlWithWic = 0;
var halfAlWithoutWic = 0;

foreach (var s in shifts)
{
    var isHalfAl = string.Equals(s.ShiftType, ShiftTypes.HalfAL, StringComparison.OrdinalIgnoreCase);
    if (isHalfAl)
    {
        var hasOnSite = wicRows.Any(w => w.IsOnSite
            && string.Equals(w.EmployeeId, s.EmployeeId, StringComparison.OrdinalIgnoreCase)
            && w.ShiftDate == s.ShiftDate);
        if (hasOnSite) halfAlWithWic++; else halfAlWithoutWic++;
        continue;
    }

    if (!IsWicDutyRow(s)) continue;

    if (absenceTypes.Contains(s.ShiftType ?? "") &&
        !string.Equals(s.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase))
    {
        absentWicDuty++;
        continue;
    }

    var kind = Kind(s);
    // A BO day RTM authored is never WIC duty (owner rule 2026-10-15): BO stands.
    if (WicBackOfficePriority.IsManualBackOffice(s)) { manualBo++; continue; }
    allKindCounts[kind] = allKindCounts.GetValueOrDefault(kind) + 1;
    var centre = CentreFor(s);
    if (centre == null) { noCentre.Add((s, kind)); continue; }
    if (!WicHoursResolver.IsOpenOn(allHours, centre, s.ShiftDate)) { closedCentre.Add((s, kind, centre)); continue; }

    var onSite = wicRows.FirstOrDefault(w => w.IsOnSite
        && string.Equals(w.EmployeeId, s.EmployeeId, StringComparison.OrdinalIgnoreCase)
        && w.ShiftDate == s.ShiftDate
        && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, centre));

    if (onSite != null) { okRows++; continue; }

    batches.TryGetValue(s.SourceId ?? -1, out var batch);
    miss.Add(new Miss(s, kind, centre, batch));
}

// ── writers, grouped ─────────────────────────────────────────────────────────────────────
string WriterOf(Miss m)
{
    var b = m.Batch;
    var who = b?.CreatedByKid;
    var manual = string.IsNullOrWhiteSpace(m.Row.ManualChangeBy) ? "auto" : "manual";
    return $"{m.Row.SourceModule ?? "(none)"} | batch={b?.Id.ToString() ?? "-"} | by={who ?? "-"} | {manual} | {m.Kind}";
}

var byWriter = miss.GroupBy(WriterOf).OrderByDescending(g => g.Count()).ToList();
var byModule = miss.GroupBy(m => m.Row.SourceModule ?? "(none)").OrderByDescending(g => g.Count()).ToList();
var byKind   = miss.GroupBy(m => m.Kind).OrderByDescending(g => g.Count()).ToList();
var byCentre = miss.GroupBy(m => m.Centre.DisplayName).OrderByDescending(g => g.Count()).ToList();
var byEmp    = miss.GroupBy(m => m.Row.EmployeeId).OrderByDescending(g => g.Count()).ToList();

var sb = new StringBuilder();
sb.AppendLine($"WIC duty rows in window {from:yyyy-MM-dd}..{to:yyyy-MM-dd}: {shifts.Count} ShiftEntries rows total");
sb.AppendLine($"  WIC duty rows WITH an on-site WicShiftEntries row : {okRows}");
sb.AppendLine($"  WIC duty rows WITHOUT one (the defect)            : {miss.Count}");
sb.AppendLine($"  WIC duty row whose centre could not be resolved   : {noCentre.Count}");
sb.AppendLine($"  WIC duty row at a CLOSED centre (excluded)        : {closedCentre.Count}");
sb.AppendLine($"  absence-typed rows carrying IsWicDuty (skipped)   : {absentWicDuty}");
sb.AppendLine($"  manual RTM BO days (never WIC, excluded)          : {manualBo}");
sb.AppendLine($"  HALF_AL rows with an on-site WIC row / without    : {halfAlWithWic} / {halfAlWithoutWic}");
sb.AppendLine();
sb.AppendLine("── all WIC duty rows by kind ──");
foreach (var g in allKindCounts.OrderByDescending(k => k.Value)) sb.AppendLine($"{g.Key,-20} {g.Value,6}");
sb.AppendLine();
sb.AppendLine("── by kind (rows WITHOUT an on-site row) ──");
foreach (var g in byKind) sb.AppendLine($"{g.Key,-20} {g.Count(),6}");
sb.AppendLine();
sb.AppendLine("── by SourceModule ──");
foreach (var g in byModule) sb.AppendLine($"{g.Key,-24} {g.Count(),6}");
sb.AppendLine();
sb.AppendLine("── by writer (SourceModule | batch | CreatedByKid | manual/auto | kind) ──");
foreach (var g in byWriter) sb.AppendLine($"{g.Count(),6}  {g.Key}");
sb.AppendLine();
sb.AppendLine("── by centre ──");
foreach (var g in byCentre) sb.AppendLine($"{g.Count(),6}  {g.Key}");
sb.AppendLine();
sb.AppendLine("── by employee ──");
foreach (var g in byEmp)
    sb.AppendLine($"{g.Count(),6}  {g.Key}");

Console.WriteLine(sb.ToString());

// ── CSV (full per-row list; UTF-8 with BOM so Excel shows the umlauts) ───────────────────
var csv = new StringBuilder();
csv.AppendLine("ShiftEntryId,EmployeeId,ShiftDate,ShiftType,RawValue,ShiftStart,ShiftEnd,IsWicDuty,AgentTask," +
               "AutoGenerated,SourceModule,SourceId,ManualChangeBy,BatchId,BatchCreatedByKid,BatchCreatedAt,Kind," +
               "CentreCode,CentreName,CentreOpen,DatesDow");
foreach (var m in miss.OrderBy(m => m.Row.ShiftDate).ThenBy(m => m.Centre.DisplayName).ThenBy(m => m.Row.EmployeeId))
{
    var r = m.Row;
    csv.AppendLine(string.Join(',',
        r.Id, r.EmployeeId,
        r.ShiftDate.ToString("yyyy-MM-dd"), r.ShiftType, Csv(r.RawValue), r.ShiftStart, r.ShiftEnd,
        r.IsWicDuty ? 1 : 0, Csv(r.AgentTask), r.AutoGenerated ? 1 : 0,
        Csv(r.SourceModule), r.SourceId?.ToString() ?? "", r.ManualChangeBy ?? "",
        m.Batch?.Id.ToString() ?? "", Csv(m.Batch?.CreatedByKid), m.Batch?.CreatedAt.ToString("O") ?? "",
        m.Kind, Csv(m.Centre.LocationCode), Csv(m.Centre.DisplayName), 1, r.ShiftDate.DayOfWeek.ToString()));
}
File.WriteAllText(Path.Combine(outDir, "missing-onsite-wic-rows.csv"), csv.ToString(), new UTF8Encoding(true));

var sum = new StringBuilder();
sum.AppendLine("{");
sum.AppendLine($"  \"windowFrom\": \"{from:yyyy-MM-dd}\",");
sum.AppendLine($"  \"windowTo\": \"{to:yyyy-MM-dd}\",");
sum.AppendLine($"  \"totalShiftEntries\": {shifts.Count},");
sum.AppendLine($"  \"wicDutyWithOnSite\": {okRows},");
sum.AppendLine($"  \"wicDutyMissingOnSite\": {miss.Count},");
sum.AppendLine($"  \"centreUnresolved\": {noCentre.Count},");
sum.AppendLine($"  \"closedCentre\": {closedCentre.Count},");
sum.AppendLine($"  \"halfAlWithWic\": {halfAlWithWic},");
sum.AppendLine($"  \"halfAlWithoutWic\": {halfAlWithoutWic},");
sum.AppendLine("  \"byKind\": {" + string.Join(",", byKind.Select(g => $"\"{g.Key}\": {g.Count()}")) + "},");
sum.AppendLine("  \"bySourceModule\": {" + string.Join(",", byModule.Select(g => $"\"{g.Key.Replace("\"", "")}\": {g.Count()}")) + "},");
sum.AppendLine("  \"byCentre\": {" + string.Join(",", byCentre.Select(g => $"\"{g.Key}\": {g.Count()}")) + "},");
sum.AppendLine("  \"byWriter\": {" + string.Join(",", byWriter.Select(g => $"\"{g.Key.Replace("\"", "")}\": {g.Count()}")) + "}");
sum.AppendLine("}");
File.WriteAllText(Path.Combine(outDir, "summary.json"), sum.ToString(), new UTF8Encoding(false));

var txt = new StringBuilder();
txt.AppendLine(sb.ToString());
txt.AppendLine("── unresolved centre ──");
foreach (var (row, kind) in noCentre.Take(200))
    txt.AppendLine($"{row.Id}|{row.EmployeeId}|{row.ShiftDate:yyyy-MM-dd}|{row.ShiftType}|{row.RawValue}|{row.AgentTask}|{kind}|{row.SourceModule}|{row.SourceId}");
txt.AppendLine("── closed centre (excluded from the write) ──");
foreach (var (row, kind, loc) in closedCentre.Take(200))
    txt.AppendLine($"{row.Id}|{row.EmployeeId}|{row.ShiftDate:yyyy-MM-dd}|{row.ShiftType}|{row.RawValue}|{row.AgentTask}|{kind}|{loc.DisplayName}|{row.SourceModule}|{row.SourceId}");
File.WriteAllText(Path.Combine(outDir, "analysis.txt"), txt.ToString(), new UTF8Encoding(true));

Console.WriteLine($"CSV : {Path.Combine(outDir, "missing-onsite-wic-rows.csv")}");
Console.WriteLine($"TXT : {Path.Combine(outDir, "analysis.txt")}");
Console.WriteLine($"JSON: {Path.Combine(outDir, "summary.json")}");
return 0;

static string Csv(string? v) => v == null ? "" : (v.Contains(',') || v.Contains('"')
    ? "\"" + v.Replace("\"", "\"\"") + "\"" : v);

internal sealed record Miss(ShiftEntry Row, string Kind, WicLocation Centre, RosterBatch? Batch);


