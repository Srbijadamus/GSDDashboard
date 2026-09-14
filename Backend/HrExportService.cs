using ClosedXML.Excel;
using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text;

namespace GSDDashboard.API.Modules.HrExport;

public class HrExportService
{
    private readonly GSDContext _db;
    public HrExportService(GSDContext db) => _db = db;

    // ── Column headers ────────────────────────────────────────────────────────

    private static readonly string[] ShiftPlanHeaders =
        ["EmployeeId", "Name", "Date", "ShiftType", "ShiftStart", "ShiftEnd", "AgentTask", "LocationId"];

    private static readonly string[] SickLeaveHeaders =
        ["EmployeeId", "Name", "TeamLead", "FirstDay", "LastDay", "Duration", "LeaveType", "Comments"];

    private static readonly string[] AnnualLeaveHeaders =
        ["EmployeeId", "Name", "FirstDay", "LastDay", "WorkDaysNet", "Comments"];

    private static readonly string[] ALBalanceHeaders =
        ["EmployeeId", "Name", "EligibleDays", "PlannedTakenAL", "RemainingAL"];

    private static readonly string[] AttendanceHeaders =
        ["EmployeeId", "Name", "TeamLead", "Date", "ShiftType", "WicLocation", "PlannedStart", "PlannedEnd"];

    private static readonly string[] TrainingHeaders =
        ["Topic", "SessionDate", "SessionTime", "Participants"];

    // ── ShiftPlan ─────────────────────────────────────────────────────────────

    private async Task<List<string[]>> GetShiftPlanRowsAsync(
        DateOnly from, DateOnly to, string? employeeId, string? teamLead)
    {
        var q = _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
            .Join(_db.Employees, s => s.EmployeeId, e => e.EmployeeId, (s, e) => new { s, e });

        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(x => x.s.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(teamLead))
            q = q.Where(x => x.e.TeamLeadName == teamLead);

        var data = await q
            .OrderBy(x => x.s.ShiftDate)
            .ThenBy(x => x.e.FullName)
            .ToListAsync();

        return data.Select(x => new string[]
        {
            x.s.EmployeeId,
            x.e.FullName ?? $"{x.e.FirstName} {x.e.LastName}".Trim(),
            x.s.ShiftDate.ToString("yyyy-MM-dd"),
            x.s.ShiftType,
            x.s.ShiftStart ?? "",
            x.s.ShiftEnd ?? "",
            x.s.AgentTask ?? "",
            x.s.LocationId ?? ""
        }).ToList();
    }

    // ── SickLeave ─────────────────────────────────────────────────────────────

    private async Task<List<string[]>> GetSickLeaveRowsAsync(
        DateOnly from, DateOnly to, string? employeeId)
    {
        var q = _db.SickLeaves
            .Where(s => s.FirstDay <= to && s.LastDay >= from);

        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(s => s.EmployeeId == employeeId);

        var data = await q.OrderBy(s => s.FirstDay).ToListAsync();

        return data.Select(s => new string[]
        {
            s.EmployeeId ?? "",
            $"{s.FirstName} {s.LastName}".Trim(),
            s.TeamLeadName ?? "",
            s.FirstDay.ToString("yyyy-MM-dd"),
            s.LastDay.Year >= 2099 ? "open" : s.LastDay.ToString("yyyy-MM-dd"),
            s.DurationDays?.ToString() ?? "",
            s.LeaveType ?? "",
            s.Comments ?? ""
        }).ToList();
    }

    // ── AnnualLeave ───────────────────────────────────────────────────────────

    private async Task<List<string[]>> GetAnnualLeaveRowsAsync(
        DateOnly from, DateOnly to, string? employeeId)
    {
        var q = _db.Vacations
            .Where(v => v.FirstDay <= to && v.LastDay >= from);

        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(v => v.EmployeeId == employeeId);

        var data = await q.OrderBy(v => v.FirstDay).ToListAsync();

        return data.Select(v => new string[]
        {
            v.EmployeeId ?? "",
            $"{v.FirstName} {v.LastName}".Trim(),
            v.FirstDay.ToString("yyyy-MM-dd"),
            v.LastDay.ToString("yyyy-MM-dd"),
            v.WorkDaysNet?.ToString("F1") ?? "",
            v.Comments ?? ""
        }).ToList();
    }

    // ── ALBalance ─────────────────────────────────────────────────────────────

    private async Task<List<string[]>> GetALBalanceRowsAsync(string? employeeId)
    {
        var q = _db.ALBalances.AsQueryable();

        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(a => a.EmployeeId == employeeId);

        var data = await q.OrderBy(a => a.EmployeeName).ToListAsync();

        return data.Select(a => new string[]
        {
            a.EmployeeId ?? "",
            a.EmployeeName ?? "",
            a.EligibleDays.ToString(),
            a.PlannedTakenAL.ToString("F1"),
            a.RemainingAL.ToString("F1")
        }).ToList();
    }

    // ── Attendance ────────────────────────────────────────────────────────────

    private async Task<List<string[]>> GetAttendanceRowsAsync(
        DateOnly from, DateOnly to, string? employeeId, string? teamLead)
    {
        var q = _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
            .Join(_db.Employees, s => s.EmployeeId, e => e.EmployeeId, (s, e) => new { s, e });

        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(x => x.s.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(teamLead))
            q = q.Where(x => x.e.TeamLeadName == teamLead);

        var shifts = await q
            .OrderBy(x => x.s.ShiftDate)
            .ThenBy(x => x.e.FullName)
            .ToListAsync();

        // Load on-site WIC entries for the same date range for location lookup
        var wicRaw = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= from && w.ShiftDate <= to && w.IsOnSite)
            .ToListAsync();

        var wicLookup = wicRaw
            .GroupBy(w => (w.EmployeeId, w.ShiftDate))
            .ToDictionary(g => g.Key, g => g.First().SupportLocation ?? "");

        return shifts.Select(x =>
        {
            wicLookup.TryGetValue((x.s.EmployeeId, x.s.ShiftDate), out var wicLoc);
            return new string[]
            {
                x.s.EmployeeId,
                x.e.FullName ?? $"{x.e.FirstName} {x.e.LastName}".Trim(),
                x.e.TeamLeadName ?? "",
                x.s.ShiftDate.ToString("yyyy-MM-dd"),
                x.s.ShiftType,
                wicLoc ?? "",
                x.s.ShiftStart ?? "",
                x.s.ShiftEnd ?? ""
            };
        }).ToList();
    }

    // ── Training ──────────────────────────────────────────────────────────────

    private async Task<List<string[]>> GetTrainingRowsAsync(DateOnly from, DateOnly to)
    {
        var sessions = await _db.TrainingSessions
            .Where(s => s.ScheduledDate >= from && s.ScheduledDate <= to)
            .Join(_db.TrainingTopics, s => s.TopicId, t => t.Id, (s, t) => new { s, t })
            .OrderBy(x => x.s.ScheduledDate)
            .ToListAsync();

        // Collect all unique agent IDs referenced across all sessions
        var allIds = sessions
            .Where(x => !string.IsNullOrEmpty(x.s.AgentIds))
            .SelectMany(x => x.s.AgentIds!
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim()))
            .Distinct()
            .ToList();

        var empLookup = new Dictionary<string, string>();
        if (allIds.Count > 0)
        {
            var emps = await _db.Employees
                .Where(e => allIds.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.FullName, e.FirstName, e.LastName })
                .ToListAsync();

            foreach (var e in emps)
                empLookup[e.EmployeeId] = e.FullName ?? $"{e.FirstName} {e.LastName}".Trim();
        }

        return sessions.Select(x =>
        {
            var participants = string.IsNullOrEmpty(x.s.AgentIds)
                ? ""
                : string.Join(", ", x.s.AgentIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id =>
                    {
                        var tid = id.Trim();
                        return empLookup.TryGetValue(tid, out var name) ? name : tid;
                    }));

            return new string[]
            {
                x.t.Name,
                x.s.ScheduledDate.ToString("yyyy-MM-dd"),
                $"{x.s.StartTime} - {x.s.EndTime}",
                participants
            };
        }).ToList();
    }

    // ── Data collection ───────────────────────────────────────────────────────

    private async Task<Dictionary<string, (string[] headers, List<string[]> rows)>> CollectAsync(
        DateOnly from, DateOnly to, string? employeeId, string? teamLead, List<string> types)
    {
        var result = new Dictionary<string, (string[] headers, List<string[]> rows)>();

        if (types.Contains("ShiftPlan"))
            result["ShiftPlan"] = (ShiftPlanHeaders, await GetShiftPlanRowsAsync(from, to, employeeId, teamLead));
        if (types.Contains("SickLeave"))
            result["SickLeave"] = (SickLeaveHeaders, await GetSickLeaveRowsAsync(from, to, employeeId));
        if (types.Contains("AnnualLeave"))
            result["AnnualLeave"] = (AnnualLeaveHeaders, await GetAnnualLeaveRowsAsync(from, to, employeeId));
        if (types.Contains("ALBalance"))
            result["ALBalance"] = (ALBalanceHeaders, await GetALBalanceRowsAsync(employeeId));
        if (types.Contains("Attendance"))
            result["Attendance"] = (AttendanceHeaders, await GetAttendanceRowsAsync(from, to, employeeId, teamLead));
        if (types.Contains("Training"))
            result["Training"] = (TrainingHeaders, await GetTrainingRowsAsync(from, to));

        return result;
    }

    // ── XLSX ──────────────────────────────────────────────────────────────────

    public async Task<byte[]> GenerateXlsxAsync(
        DateOnly from, DateOnly to, string? employeeId, string? teamLead, List<string> types)
    {
        var data = await CollectAsync(from, to, employeeId, teamLead, types);

        using var wb = new XLWorkbook();

        foreach (var (typeName, (headers, rows)) in data)
        {
            var sheetName = typeName switch
            {
                "ShiftPlan"   => "Shift Plan",
                "SickLeave"   => "Sick Leave",
                "AnnualLeave" => "Annual Leave",
                "ALBalance"   => "AL Balance",
                "Attendance"  => "Attendance",
                "Training"    => "Training",
                _             => typeName
            };

            var ws = wb.Worksheets.Add(sheetName);
            WriteSheet(ws, headers, rows);
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── CSV ZIP ───────────────────────────────────────────────────────────────

    public async Task<byte[]> GenerateCsvZipAsync(
        DateOnly from, DateOnly to, string? employeeId, string? teamLead, List<string> types)
    {
        var data = await CollectAsync(from, to, employeeId, teamLead, types);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (typeName, (headers, rows)) in data)
            {
                var entry = zip.CreateEntry($"{typeName}.csv");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                await writer.WriteLineAsync(ToCsvLine(headers));
                foreach (var row in rows)
                    await writer.WriteLineAsync(ToCsvLine(row));
            }
        }

        return ms.ToArray();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void WriteSheet(IXLWorksheet ws, string[] headers, List<string[]> rows)
    {
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Row(1).Style.Font.Bold = true;

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.Length; c++)
                ws.Cell(r + 2, c + 1).Value = row[c];
        }

        ws.Columns().AdjustToContents();
    }

    private static string ToCsvLine(string[] fields) =>
        string.Join(",", fields.Select(f =>
            f.Contains('"') || f.Contains(',') || f.Contains('\n') || f.Contains('\r')
                ? $"\"{f.Replace("\"", "\"\"")}\""
                : f));
}

// ── Endpoint mapper ───────────────────────────────────────────────────────────

public static class HrExportEndpointMapper
{
    private static readonly string[] AllTypes =
        ["ShiftPlan", "SickLeave", "AnnualLeave", "Attendance", "ALBalance", "Training"];

    public static void MapHrExportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/hr-export", async (
            string? from,
            string? to,
            string? employeeId,
            string? teamLead,
            string? types,
            string? format,
            HrExportService svc) =>
        {
            if (string.IsNullOrWhiteSpace(from) || !DateOnly.TryParse(from, out var fromDate))
                return Results.BadRequest(new { error = "'from' is required (yyyy-MM-dd)" });
            if (string.IsNullOrWhiteSpace(to) || !DateOnly.TryParse(to, out var toDate))
                return Results.BadRequest(new { error = "'to' is required (yyyy-MM-dd)" });

            var selectedTypes = string.IsNullOrWhiteSpace(types)
                ? AllTypes.ToList()
                : types
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => AllTypes.Contains(t))
                    .ToList();

            if (selectedTypes.Count == 0)
                return Results.BadRequest(new { error = "No valid data types specified" });

            var fmt = string.IsNullOrWhiteSpace(format) ? "xlsx" : format.ToLowerInvariant();
            var dateStamp = DateTime.Today.ToString("yyyy-MM-dd");

            if (fmt == "csv")
            {
                var bytes = await svc.GenerateCsvZipAsync(fromDate, toDate, employeeId, teamLead, selectedTypes);
                return Results.File(bytes, "application/zip", $"HrExport_{dateStamp}.zip");
            }
            else
            {
                var bytes = await svc.GenerateXlsxAsync(fromDate, toDate, employeeId, teamLead, selectedTypes);
                return Results.File(
                    bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"HrExport_{dateStamp}.xlsx");
            }
        }).WithTags("HrExport");
    }
}
