using System.Globalization;
using System.Text;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

// ONE-OFF data migration for the owner rule 2026-10-15 (WIC has priority over BO).
//
//   For every ShiftEntries row in [2026-10-15 .. 2027-02-28] with RawValue='BO' that is a
//   PLAN back-office day (no ManualChangeBy) and whose employee holds an ACTIVE WIC
//   assignment (MAIN / BACKUP / REGIONAL) with exactly ONE open centre in the first tier
//   that has any open centre:
//     * create/keep the on-site WicShiftEntries row with
//       WorkingShift = the agent's shift intersected with the centre's working window;
//     * stamp SourceModule = 'RTM_RULE 2026-10-15 WIC>BO' on the ShiftEntries row.
//   ManualChangeBy is never written; manual rows, leave types and HALF_AL are never touched.
//
//   DryRun (default) only reads and writes plan.csv. -Apply requires the expected row count,
//   writes the backup + rollback.sql first, then performs ONE guarded transaction.
//
// The rule itself is the PRODUCTION code (WicBackOfficePriority / WicWorkingShift /
// WicHoursResolver), so the migration can never drift from what the app reads.

const string ConnectionString =
    @"Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;";

var mode = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "dryrun";
var outDir = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();
var expected = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : -1;

var from = new DateOnly(2026, 10, 15);
var to = new DateOnly(2027, 2, 28);

Directory.CreateDirectory(outDir);

var options = new DbContextOptionsBuilder<GSDContext>().UseSqlServer(ConnectionString).Options;
using var db = new GSDContext(options);

Console.WriteLine($"WIC>BO rule migration — mode={mode}, window={from:yyyy-MM-dd}..{to:yyyy-MM-dd}");

// ── classify every BO row in the window ───────────────────────────────────────────────
var boRows = db.ShiftEntries
    .Where(s => s.ShiftDate >= from && s.ShiftDate <= to && s.RawValue == "BO")
    .ToList();

var employees = db.Employees.ToList();
var activeAssignments = db.WicAgentAssignments.Where(a => a.IsActive).ToList();
var locations = db.WicLocations.Where(l => l.IsActive).ToList();
var hours = db.WicOpeningHours.ToList();
var index = WicBackOfficePriority.BuildIndex(activeAssignments, locations, employees);

var assignedIds = employees
    .Where(e => index.HasAssignment(e.EmployeeId))
    .Select(e => e.EmployeeId)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var scoped = boRows.Where(r => assignedIds.Contains(r.EmployeeId)).ToList();

Console.WriteLine($"BO rows in window: {boRows.Count}; for agents with an ACTIVE WIC assignment: {scoped.Count}");
Console.WriteLine($"  manual RTM BO (untouched)      : {scoped.Count(WicBackOfficePriority.IsManualBackOffice)}");
Console.WriteLine($"  manual leave/OFF (untouched)   : {scoped.Count(r => r.IsManualChange && !WicBackOfficePriority.IsManualBackOffice(r))}");
Console.WriteLine($"  plan BO, no open priority centre: {scoped.Count(r => WicBackOfficePriority.IsPlanBackOffice(r) && WicBackOfficePriority.PriorityCentre(index, r.EmployeeId, r.ShiftDate, hours) == null)}");

// ── build the plan ────────────────────────────────────────────────────────────────────
var existingWic = db.WicShiftEntries
    .Where(w => w.ShiftDate >= from && w.ShiftDate <= to)
    .ToList();

var plan = new List<PlanRow>();
foreach (var row in scoped.Where(WicBackOfficePriority.IsPlanBackOffice))
{
    var centre = WicBackOfficePriority.PriorityCentre(index, row.EmployeeId, row.ShiftDate, hours);
    if (centre == null) continue;

    var (centreStart, centreEnd) = WicWorkingShift.CentreWindow(hours, centre, row.ShiftDate);
    var working = WicWorkingShift.Slice(row.ShiftStart, row.ShiftEnd, centreStart, centreEnd)
                  ?? WicWorkingShift.FallbackWindow(centreStart, centreEnd);
    if (string.IsNullOrWhiteSpace(working)) continue;

    var target = existingWic.FirstOrDefault(w =>
        string.Equals(w.EmployeeId, row.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
        w.ShiftDate == row.ShiftDate &&
        WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, centre));

    plan.Add(new PlanRow(row, centre, working, target));
}

Console.WriteLine($"PLANNED: {plan.Count} ShiftEntries rows -> {plan.Count} on-site WIC rows " +
                  $"({plan.Count(p => p.Target == null)} insert, {plan.Count(p => p.Target != null)} update)");

// plan.csv (receipt)
var csv = new StringBuilder();
csv.AppendLine("ShiftEntryId,EmployeeId,FullName,ShiftDate,ShiftType,RawValue,ShiftStart,ShiftEnd,ManualChangeBy," +
               "CentreCode,CentreName,WorkingShift,WicRowId,WicBefore_WorkingShift,WicBefore_IsOnSite");
foreach (var p in plan.OrderBy(p => p.Row.EmployeeId).ThenBy(p => p.Row.ShiftDate))
{
    var name = employees.FirstOrDefault(e => e.EmployeeId == p.Row.EmployeeId)?.FullName ?? "";
    csv.AppendLine(string.Join(',',
        p.Row.Id, p.Row.EmployeeId, Csv(name), p.Row.ShiftDate.ToString("yyyy-MM-dd"),
        p.Row.ShiftType, p.Row.RawValue, p.Row.ShiftStart, p.Row.ShiftEnd, p.Row.ManualChangeBy ?? "",
        Csv(p.Centre.LocationCode), Csv(p.Centre.DisplayName), p.WorkingShift,
        p.Target?.Id.ToString() ?? "", p.Target?.WorkingShift ?? "",
        p.Target == null ? "" : (p.Target.IsOnSite ? "1" : "0")));
}
File.WriteAllText(Path.Combine(outDir, "plan.csv"), csv.ToString(), new UTF8Encoding(true));

if (mode != "apply")
{
    Console.WriteLine($"DRY RUN — nothing written to the database. plan.csv: {Path.Combine(outDir, "plan.csv")}");
    return 0;
}

// ── APPLY ─────────────────────────────────────────────────────────────────────────────
if (expected >= 0 && plan.Count != expected)
{
    Console.WriteLine($"ABORT: expected {expected} rows, computed {plan.Count}. Nothing written.");
    return 2;
}

var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
var backupDir = Path.Combine(@"C:\ProgramData\PulseRollback", $"wicbo-{stamp}");
Directory.CreateDirectory(backupDir);
Console.WriteLine($"Backup: {backupDir}");

File.WriteAllText(Path.Combine(backupDir, "plan.csv"), csv.ToString(), new UTF8Encoding(true));
File.WriteAllText(Path.Combine(backupDir, "ShiftEntries-pre.csv"), PreImage(plan), new UTF8Encoding(true));

// rollback.sql — restores SourceModule and the WicShiftEntries state captured above.
var rollback = new StringBuilder();
rollback.AppendLine("-- ROLLBACK for the WIC>BO rule migration (owner rule 2026-10-15).");
rollback.AppendLine("-- NOT executed. Run only as a deliberate incident operation.");
rollback.AppendLine("SET XACT_ABORT ON; BEGIN TRANSACTION;");
foreach (var p in plan)
{
    rollback.AppendLine(
        $"UPDATE ShiftEntries SET SourceModule = {Sql(p.Row.SourceModule)} " +
        $"WHERE Id = {p.Row.Id} AND SourceModule = 'RTM_RULE 2026-10-15 WIC>BO';");
    if (p.Target == null)
        rollback.AppendLine(
            $"DELETE FROM WicShiftEntries WHERE EmployeeId = {Sql(p.Row.EmployeeId)} " +
            $"AND ShiftDate = '{p.Row.ShiftDate:yyyy-MM-dd}' AND SupportLocation = {Sql(p.Centre.DisplayName)};");
    else
        rollback.AppendLine(
            $"UPDATE WicShiftEntries SET WorkingShift = {Sql(p.Target.WorkingShift)}, " +
            $"LocationCode = {Sql(p.Target.LocationCode)}, IsOnSite = {(p.Target.IsOnSite ? 1 : 0)}, " +
            $"IsGSDDay = {(p.Target.IsGSDDay ? 1 : 0)}, IsOffDay = {(p.Target.IsOffDay ? 1 : 0)}, " +
            $"Task = {Sql(p.Target.Task)}, SupportLocation = {Sql(p.Target.SupportLocation)} WHERE Id = {p.Target.Id};");
}
rollback.AppendLine("COMMIT TRANSACTION;");
File.WriteAllText(Path.Combine(backupDir, "rollback.sql"), rollback.ToString(), new UTF8Encoding(true));

using var tx = db.Database.BeginTransaction();
var updated = 0;
var inserted = 0;
foreach (var p in plan)
{
    // Only a plan BO row of this exact marker state is touched; ManualChangeBy is never written.
    updated += db.Database.ExecuteSqlRaw(
        "UPDATE ShiftEntries SET SourceModule = {0} WHERE Id = {1} AND ManualChangeBy IS NULL AND RawValue = 'BO'",
        WicBackOfficePriority.RuleMarker, p.Row.Id);

    if (p.Target == null)
    {
        inserted += db.Database.ExecuteSqlRaw(
            "INSERT INTO WicShiftEntries (EmployeeId, ShiftDate, DayOfWeek, SupportLocation, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task, LocationCode) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, 1, 0, 0, 'WIC', {5})",
            p.Row.EmployeeId, p.Row.ShiftDate, p.Row.ShiftDate.DayOfWeek.ToString(),
            p.Centre.DisplayName, p.WorkingShift, p.Centre.LocationCode);
    }
    else
    {
        db.Database.ExecuteSqlRaw(
            "UPDATE WicShiftEntries SET WorkingShift = {0}, LocationCode = {1}, SupportLocation = {2}, " +
            "IsOnSite = 1, IsGSDDay = 0, IsOffDay = 0, Task = 'WIC' WHERE Id = {3}",
            p.WorkingShift, p.Centre.LocationCode, p.Centre.DisplayName, p.Target.Id);
    }
}
tx.Commit();

File.WriteAllText(Path.Combine(backupDir, "APPLIED.txt"),
    $"Applied {stamp}: {plan.Count} plan rows; ShiftEntries SourceModule updates={updated}; " +
    $"WicShiftEntries inserts={inserted}; updates={plan.Count - inserted}\r\n");

Console.WriteLine($"APPLIED: ShiftEntries updates={updated}, WicShiftEntries inserts={inserted}, " +
                  $"updates={plan.Count - inserted}, total plan={plan.Count}");
Console.WriteLine($"Backup + rollback.sql: {backupDir}");
return 0;

static string Csv(string? v) => v == null ? "" : (v.Contains(',') || v.Contains('"')
    ? "\"" + v.Replace("\"", "\"\"") + "\"" : v);

static string Sql(string? v) => v == null ? "NULL" : "N'" + v.Replace("'", "''") + "'";

static string PreImage(List<PlanRow> plan)
{
    var sb = new StringBuilder();
    sb.AppendLine("Id,EmployeeId,ShiftDate,ShiftType,RawValue,ShiftStart,ShiftEnd,ManualChangeBy,ManualChangeAt,SourceModule,SourceId");
    foreach (var p in plan)
    {
        var r = p.Row;
        sb.AppendLine(string.Join(',',
            r.Id, r.EmployeeId, r.ShiftDate.ToString("yyyy-MM-dd"), r.ShiftType, Csv(r.RawValue),
            r.ShiftStart, r.ShiftEnd, r.ManualChangeBy ?? "",
            r.ManualChangeAt?.ToString("O") ?? "", Csv(r.SourceModule), r.SourceId?.ToString() ?? ""));
    }
    return sb.ToString();
}

internal sealed record PlanRow(ShiftEntry Row, WicLocation Centre, string WorkingShift, WicShiftEntry? Target);
