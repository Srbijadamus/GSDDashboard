using System.Globalization;
using System.Text;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

// ONE-OFF data repair for the 2026-10-15 "WIC_DUTY row without its on-site WicShiftEntries
// row" defect (live example: 9074345, Wesel, 2026-11-05/12/19/26, RosterBatch 265).
//
//   For every ShiftEntries row in [2026-10-15 .. 2027-02-28] that carries a WIC DUTY —
//     * ShiftType = WIC_DUTY, or
//     * the flipped encoding: WORKING with a WIC centre name in AgentTask, or
//     * a PLAN back-office day (RawValue='BO', no ManualChangeBy) whose agent's priority
//       centre is open (the live WIC-over-BO rule) —
//   and whose centre is OPEN on that date per WicHoursResolver and has NO on-site
//   WicShiftEntries row, write exactly that missing on-site row:
//     WorkingShift = the row's ShiftStart..ShiftEnd CUT TO the centre's window, or the
//     centre's window when the two do not touch (the shared WicWorkingShift rule).
//
//   ShiftEntries is NOT changed except for the provenance marker
//   SourceModule = 'SHIFT_FIX 2026-10-15 WIC_SYNC' (the same convention the 2026-10-14
//   SHIFT_MISMATCH repair used); ManualChangeBy is NEVER written. Absences (AL/SL/UL/OL/PH/
//   LPH), HALF_AL, training, OFF, a manual RTM BO day and a closed centre are skipped.
//
//   DryRun (default) only reads and writes plan.csv. -Apply requires the expected row count,
//   writes the backup + rollback.sql first, then performs ONE guarded transaction.
//
// The rule itself is the PRODUCTION code (WicHoursResolver / WicWorkingShift /
// WicLocationMatcher / WicBackOfficePriority), so the repair can never drift from what the
// application reads.

const string ConnectionString =
    @"Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;";

/// <summary>The provenance marker. NOT a manual marker: ManualChangeBy stays NULL.</summary>
const string Marker = "SHIFT_FIX 2026-10-15 WIC_SYNC";

var mode = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "dryrun";
var outDir = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();
var expected = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : -1;

var from = new DateOnly(2026, 10, 15);
var to   = new DateOnly(2027, 2, 28);

Directory.CreateDirectory(outDir);

var options = new DbContextOptionsBuilder<GSDContext>().UseSqlServer(ConnectionString).Options;
using var db = new GSDContext(options);

Console.WriteLine($"WIC shift-plan sync repair — mode={mode}, window={from:yyyy-MM-dd}..{to:yyyy-MM-dd}");

var employees = db.Employees.ToList();
var empById   = employees.ToDictionary(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase);
var locations = db.WicLocations.Where(l => l.IsActive).ToList();
var hours     = db.WicOpeningHours.ToList();
var index     = WicBackOfficePriority.BuildIndex(
    db.WicAgentAssignments.Where(a => a.IsActive).ToList(), locations, employees);

var shifts = db.ShiftEntries.Where(s => s.ShiftDate >= from && s.ShiftDate <= to).ToList();
var wicRows = db.WicShiftEntries.Where(w => w.ShiftDate >= from && w.ShiftDate <= to).ToList();

WicLocation? CentreFor(ShiftEntry s)
{
    if (!string.IsNullOrWhiteSpace(s.AgentTask))
    {
        var byTask = locations.FirstOrDefault(l => WicLocationMatcher.MatchesAgentTask(s.AgentTask, l));
        if (byTask != null) return byTask;
    }
    if (WicBackOfficePriority.IsPlanBackOffice(s))
        return WicBackOfficePriority.PriorityCentre(index, s.EmployeeId, s.ShiftDate, hours);
    return null;
}

string Kind(ShiftEntry s) =>
    string.Equals(s.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase) || s.IsWicDuty
        ? "WIC_DUTY_TYPE"
        : WicBackOfficePriority.IsPlanBackOffice(s) ? "PLAN_BO_CONVERTED" : "WIC_DUTY_ENCODING";

bool IsWicDuty(ShiftEntry s)
{
    if (WicBackOfficePriority.IsManualBackOffice(s)) return false;   // RTM's BO stands
    if (string.Equals(s.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase)) return true;
    if (s.IsWicDuty && string.Equals(s.ShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase))
        return locations.Any(l => WicLocationMatcher.MatchesAgentTask(s.AgentTask, l));
    if (WicBackOfficePriority.IsPlanBackOffice(s)) return true;
    return string.Equals(s.ShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(s.AgentTask)
        && locations.Any(l => WicLocationMatcher.MatchesAgentTask(s.AgentTask, l));
}

var planned = new List<Plan>();
var alreadyCovered = 0;
var skippedNoCentre = 0;
var skippedClosed = 0;

foreach (var row in shifts.Where(IsWicDuty))
{
    var centre = CentreFor(row);
    if (centre == null) { skippedNoCentre++; continue; }
    if (!WicHoursResolver.IsOpenOn(hours, centre, row.ShiftDate)) { skippedClosed++; continue; }
    if (wicRows.Any(w => w.IsOnSite
            && string.Equals(w.EmployeeId, row.EmployeeId, StringComparison.OrdinalIgnoreCase)
            && w.ShiftDate == row.ShiftDate
            && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, centre)))
    {
        alreadyCovered++;
        continue;
    }

    var (centreStart, centreEnd) = WicWorkingShift.CentreWindow(hours, centre, row.ShiftDate);
    var working = WicWorkingShift.Slice(row.ShiftStart, row.ShiftEnd, centreStart, centreEnd)
                  ?? WicWorkingShift.FallbackWindow(centreStart, centreEnd);
    if (string.IsNullOrWhiteSpace(working)) { skippedClosed++; continue; }

    // A row for that employee+date+centre may already exist — written but NOT on site
    // (stood down). The missing commitment is then fixed by turning THAT row on site, never
    // by inserting a second one: (EmployeeId, ShiftDate, SupportLocation) is unique.
    var target = wicRows.FirstOrDefault(w =>
        string.Equals(w.EmployeeId, row.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
        w.ShiftDate == row.ShiftDate &&
        WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, centre));

    planned.Add(new Plan(row, Kind(row), centre, working, target));
}

Console.WriteLine($"WIC duty rows in window                : {shifts.Count(IsWicDuty)}");
Console.WriteLine($"  already have an on-site WIC row      : {alreadyCovered}");
Console.WriteLine($"  centre could not be resolved (skip)  : {skippedNoCentre}");
Console.WriteLine($"  closed centre / no usable window     : {skippedClosed}");
Console.WriteLine($"PLANNED: {planned.Count} missing on-site WicShiftEntries row(s) " +
                  $"({planned.Count(p => p.Target == null)} insert, {planned.Count(p => p.Target != null)} turn on site)");
foreach (var g in planned.GroupBy(p => p.Kind).OrderByDescending(g => g.Count()))
    Console.WriteLine($"    {g.Key,-20} {g.Count(),4}");

// ── plan.csv (receipt + the exact write list) ────────────────────────────────────────────
var csv = new StringBuilder();
csv.AppendLine("ShiftEntryId,EmployeeId,ShiftDate,ShiftType,RawValue,AgentTask,ShiftStart,ShiftEnd," +
               "SourceModuleBefore,SourceId,ManualChangeBy,Kind,CentreCode,CentreName,Action,WicRowId," +
               "WicBefore_WorkingShift,WicBefore_IsOnSite,WicBefore_LocationCode,WicBefore_Task,WorkingShift");
foreach (var p in planned.OrderBy(p => p.Row.ShiftDate).ThenBy(p => p.Centre.DisplayName).ThenBy(p => p.Row.EmployeeId))
{
    var r = p.Row;
    csv.AppendLine(string.Join(',',
        r.Id, r.EmployeeId,
        r.ShiftDate.ToString("yyyy-MM-dd"), r.ShiftType, Csv(r.RawValue), Csv(r.AgentTask),
        r.ShiftStart, r.ShiftEnd, Csv(r.SourceModule), r.SourceId?.ToString() ?? "",
        r.ManualChangeBy ?? "", p.Kind, Csv(p.Centre.LocationCode), Csv(p.Centre.DisplayName),
        p.Target == null ? "INSERT" : "SET_ONSITE", p.Target?.Id.ToString() ?? "",
        Csv(p.Target?.WorkingShift), p.Target == null ? "" : (p.Target.IsOnSite ? "1" : "0"),
        Csv(p.Target?.LocationCode), Csv(p.Target?.Task), p.WorkingShift));
}
File.WriteAllText(Path.Combine(outDir, "plan.csv"), csv.ToString(), new UTF8Encoding(true));

if (mode != "apply")
{
    Console.WriteLine($"DRY RUN — nothing written to the database. plan.csv: {Path.Combine(outDir, "plan.csv")}");
    return 0;
}

// ── APPLY ───────────────────────────────────────────────────────────────────────────────
if (expected >= 0 && planned.Count != expected)
{
    Console.WriteLine($"ABORT: expected {expected} rows, computed {planned.Count}. Nothing written.");
    return 2;
}

var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
var backupDir = Path.Combine(@"C:\ProgramData\PulseRollback", $"wicsync-{stamp}");
Directory.CreateDirectory(backupDir);
Console.WriteLine($"Backup: {backupDir}");

File.WriteAllText(Path.Combine(backupDir, "plan.csv"), csv.ToString(), new UTF8Encoding(true));

// Pre-images: the ShiftEntries provenance the marker replaces, plus every WicShiftEntries row
// of the touched employee+date pairs (a row that somehow appears between plan and write is
// never overwritten, but the snapshot keeps the rollback complete).
var pre = new StringBuilder();
pre.AppendLine("Id,EmployeeId,ShiftDate,ShiftType,RawValue,AgentTask,ShiftStart,ShiftEnd,IsWicDuty,AutoGenerated,SourceModule,SourceId,ManualChangeBy,ManualChangeAt");
foreach (var p in planned)
{
    var r = p.Row;
    pre.AppendLine(string.Join(',', r.Id, r.EmployeeId, r.ShiftDate.ToString("yyyy-MM-dd"),
        r.ShiftType, Csv(r.RawValue), Csv(r.AgentTask), r.ShiftStart, r.ShiftEnd, r.IsWicDuty ? 1 : 0,
        r.AutoGenerated ? 1 : 0, Csv(r.SourceModule), r.SourceId?.ToString() ?? "",
        r.ManualChangeBy ?? "", r.ManualChangeAt?.ToString("O") ?? ""));
}
File.WriteAllText(Path.Combine(backupDir, "ShiftEntries-pre.csv"), pre.ToString(), new UTF8Encoding(true));

var wicPre = new StringBuilder();
wicPre.AppendLine("Id,EmployeeId,ShiftDate,SupportLocation,LocationCode,WorkingShift,IsOnSite,IsGSDDay,IsOffDay,Task,WicOpeningHours");
var keys = planned.Select(p => (p.Row.EmployeeId, p.Row.ShiftDate)).ToHashSet();
foreach (var w in db.WicShiftEntries.Where(w => w.ShiftDate >= from && w.ShiftDate <= to).ToList()
             .Where(w => keys.Contains((w.EmployeeId, w.ShiftDate))))
    wicPre.AppendLine(string.Join(',', w.Id, w.EmployeeId, w.ShiftDate.ToString("yyyy-MM-dd"),
        Csv(w.SupportLocation), Csv(w.LocationCode), Csv(w.WorkingShift), w.IsOnSite ? 1 : 0,
        w.IsGSDDay ? 1 : 0, w.IsOffDay ? 1 : 0, Csv(w.Task), Csv(w.WicOpeningHours)));
File.WriteAllText(Path.Combine(backupDir, "WicShiftEntries-pre.csv"), wicPre.ToString(), new UTF8Encoding(true));

// rollback.sql — restores SourceModule and deletes exactly the inserted on-site rows.
var rollback = new StringBuilder();
rollback.AppendLine("-- ROLLBACK for the 2026-10-15 WIC shift-plan sync repair. NOT executed.");
rollback.AppendLine("-- Run only as a deliberate incident operation.");
rollback.AppendLine("SET XACT_ABORT ON; BEGIN TRANSACTION;");
foreach (var p in planned)
{
    rollback.AppendLine(
        $"UPDATE ShiftEntries SET SourceModule = {Sql(p.Row.SourceModule)} " +
        $"WHERE Id = {p.Row.Id} AND SourceModule = {Sql(Marker)};");
    if (p.Target == null)
    {
        rollback.AppendLine(
            $"DELETE FROM WicShiftEntries WHERE EmployeeId = {Sql(p.Row.EmployeeId)} " +
            $"AND ShiftDate = '{p.Row.ShiftDate:yyyy-MM-dd}' AND SupportLocation = {Sql(p.Centre.DisplayName)};");
    }
    else
    {
        rollback.AppendLine(
            $"UPDATE WicShiftEntries SET SupportLocation = {Sql(p.Target.SupportLocation)}, " +
            $"LocationCode = {Sql(p.Target.LocationCode)}, WorkingShift = {Sql(p.Target.WorkingShift)}, " +
            $"IsOnSite = {(p.Target.IsOnSite ? 1 : 0)}, IsGSDDay = {(p.Target.IsGSDDay ? 1 : 0)}, " +
            $"IsOffDay = {(p.Target.IsOffDay ? 1 : 0)}, Task = {Sql(p.Target.Task)}, " +
            $"WicOpeningHours = {Sql(p.Target.WicOpeningHours)} WHERE Id = {p.Target.Id};");
    }
}
rollback.AppendLine("COMMIT TRANSACTION;");
File.WriteAllText(Path.Combine(backupDir, "rollback.sql"), rollback.ToString(), new UTF8Encoding(true));

int marked = 0, inserted = 0, turnedOnSite = 0, skippedNow = 0;
using (var tx = db.Database.BeginTransaction())
{
    foreach (var p in planned)
    {
        var r = p.Row;
        // Guarded: only the exact row the plan was built from is stamped.
        marked += db.Database.ExecuteSqlRaw(
            "UPDATE ShiftEntries SET SourceModule = {0} WHERE Id = {1}",
            Marker, r.Id);

        var existing = db.WicShiftEntries.AsNoTracking().FirstOrDefault(w =>
            w.EmployeeId == r.EmployeeId && w.ShiftDate == r.ShiftDate &&
            w.SupportLocation == p.Centre.DisplayName);

        if (existing == null)
        {
            db.Database.ExecuteSqlRaw(
                "INSERT INTO WicShiftEntries (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task, LocationCode) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, 1, 0, 0, 'WIC', {5})",
                r.EmployeeId, r.ShiftDate, r.ShiftDate.DayOfWeek.ToString(),
                p.Centre.DisplayName, p.WorkingShift, p.Centre.LocationCode);
            inserted++;
        }
        else
        {
            db.Database.ExecuteSqlRaw(
                "UPDATE WicShiftEntries SET WorkingShift = {0}, LocationCode = {1}, IsOnSite = 1, " +
                "IsGSDDay = 0, IsOffDay = 0, Task = 'WIC' WHERE Id = {2}",
                p.WorkingShift, p.Centre.LocationCode, existing.Id);
            turnedOnSite++;
        }
        if (existing != null && existing.IsOnSite) skippedNow++;
    }
    tx.Commit();
}

File.WriteAllText(Path.Combine(backupDir, "APPLIED.txt"),
    $"Applied {stamp}: plan={planned.Count}; ShiftEntries marker updates={marked}; " +
    $"WicShiftEntries inserts={inserted}; turned on site={turnedOnSite}\r\n" +
    $"Marker: {Marker}\r\nManualChangeBy was never written.\r\n");

Console.WriteLine($"APPLIED: ShiftEntries marker updates={marked}, WicShiftEntries inserts={inserted}, turned on site={turnedOnSite}");
Console.WriteLine($"Backup + rollback.sql: {backupDir}");
return 0;

static string Csv(string? v) => v == null ? "" : (v.Contains(',') || v.Contains('"')
    ? "\"" + v.Replace("\"", "\"\"") + "\"" : v);

static string Sql(string? v) => v == null ? "NULL" : "N'" + v.Replace("'", "''") + "'";

internal sealed record Plan(ShiftEntry Row, string Kind, WicLocation Centre, string WorkingShift, WicShiftEntry? Target);
