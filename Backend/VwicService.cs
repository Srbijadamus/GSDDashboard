using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Vwic;

// ─── DTOs ────────────────────────────────────────────────────────────────────

public record VwicAgentDto(
    string  EmployeeId,
    string? FullName,
    string? TeamLeadName,
    string  Role,            // "Main" | "Backup"
    string? ShiftType,
    string? ShiftStart,
    string? ShiftEnd,
    bool    IsAbsent,
    string? AbsenceType,     // "SL" | "AL" | "UL" | "OFF" | "PH" | null
    bool    IsVwicAssigned   // has explicit WicShiftEntry with SupportLocation="VWIC"
);

public record VwicTimelineSlotDto(
    int          Hour,
    string       Label,
    List<string> MainAgents,
    List<string> BackupAgents,
    bool         HasMainAgent,
    bool         HasAnyAgent
);

public record VwicDailyResponseDto(
    string                    Date,
    List<VwicAgentDto>        Agents,
    List<VwicTimelineSlotDto> Timeline,
    List<int>                 Gaps,
    int                       CoveredHours,
    int                       TotalHours
);

public record VwicAssignRequest(
    string  EmployeeId,
    string  Date,
    int     Hour,
    string? ShiftStart,
    string? ShiftEnd
);

public record VwicCandidateDto(
    string  EmployeeId,
    string? FullName,
    string? TeamLeadName,
    string? PrimaryRole
);

public record VwicManageRequest(string EmployeeId);

// ─── Rotation Plan DTOs ───────────────────────────────────────────────────────

public record VwicRotationRequest(
    string Date,
    string StartTime,
    string EndTime,
    int    IntervalHours,
    int    MaxContinuousHours,
    int    HandoverMinutes
);

public record VwicRotationScheduleRow(
    string       EmployeeId,
    string       FullName,
    List<string> SlotStatus   // "ON" | "HANDOVER" | "OFF" per slot
);

public record VwicCoverageProofItem(
    string StartTime,
    string EndTime,
    int    AgentCount,
    int    Required,
    bool   Covered
);

public record VwicFairnessItem(
    string EmployeeId,
    string FullName,
    double VwicHours,
    int    SlotCount
);

public record VwicRotationResponse(
    string                        Date,
    List<string>                  SlotLabels,
    List<VwicRotationScheduleRow> Schedule,
    List<VwicCoverageProofItem>   CoverageProof,
    List<VwicFairnessItem>        Fairness,
    string                        Recommendation,
    string?                       FallbackWarning,
    int                           AvailableAgents,
    double                        RequiredAgentHours,
    double                        AvailableAgentHours
);

// ─── Service ─────────────────────────────────────────────────────────────────

public class VwicService
{
    private readonly GSDContext _db;
    public VwicService(GSDContext db) => _db = db;

    private const int StartHour = 7;
    private const int EndHour   = 18;

    public async Task<VwicDailyResponseDto> GetDailyAsync(string? dateStr)
    {
        var date = dateStr != null && DateOnly.TryParse(dateStr, out var d)
            ? d : DateOnly.FromDateTime(DateTime.Today);

        // All active VWIC employees
        var vwicEmps = await _db.Employees
            .Where(e => e.IsActive &&
                        (e.PrimaryRole == "VWIC" || e.SecondaryRole == "VWIC"))
            .ToListAsync();

        var empIds = vwicEmps.Select(e => e.EmployeeId).ToList();

        // Shift entries for the requested date
        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date && empIds.Contains(s.EmployeeId))
            .ToListAsync();

        // Active sick leaves on this date
        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null
                      && empIds.Contains(sl.EmployeeId)
                      && sl.FirstDay <= date
                      && sl.LastDay  >= date)
            .ToListAsync();

        // Explicit VWIC assignments stored as WicShiftEntries
        var vwicAssignments = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date
                     && empIds.Contains(w.EmployeeId)
                     && w.SupportLocation == "VWIC")
            .ToListAsync();

        // Build agent DTOs
        var agents = vwicEmps.Select(emp =>
        {
            var shift     = shifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
            var sl        = sickLeaves.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
            var vwicEntry = vwicAssignments.FirstOrDefault(w => w.EmployeeId == emp.EmployeeId);
            var role      = emp.PrimaryRole == "VWIC" ? "Main" : "Backup";

            bool    isAbsent    = false;
            string? absenceType = null;

            if (sl != null)
            {
                isAbsent = true; absenceType = "SL";
            }
            else if (shift?.ShiftType is "AL" or "SL" or "UL" or "OFF" or "PH" or "OFF_WEEKEND")
            {
                isAbsent = true; absenceType = shift.ShiftType;
            }

            // Prefer WicShiftEntry times for explicitly assigned agents
            string? shiftStart = shift?.ShiftStart;
            string? shiftEnd   = shift?.ShiftEnd;
            if (vwicEntry?.WorkingShift != null)
            {
                var parts = vwicEntry.WorkingShift.Split(" - ");
                if (parts.Length == 2) { shiftStart = parts[0].Trim(); shiftEnd = parts[1].Trim(); }
            }

            return new VwicAgentDto(
                emp.EmployeeId,
                emp.FullName,
                emp.TeamLeadName,
                role,
                shift?.ShiftType,
                shiftStart,
                shiftEnd,
                isAbsent,
                absenceType,
                vwicEntry != null
            );
        })
        .OrderBy(a => a.Role)        // Main first
        .ThenBy(a => a.FullName)
        .ToList();

        // Build coverage timeline (one slot per hour 07–17)
        var timeline     = new List<VwicTimelineSlotDto>();
        var gaps         = new List<int>();
        var activeAgents = agents
            .Where(a => !a.IsAbsent && a.ShiftStart != null && a.ShiftEnd != null)
            .ToList();

        for (int h = StartHour; h < EndHour; h++)
        {
            var mainPresent   = new List<string>();
            var backupPresent = new List<string>();

            foreach (var ag in activeAgents)
            {
                if (!TryDecimalHour(ag.ShiftStart, out double start)) continue;
                if (!TryDecimalHour(ag.ShiftEnd,   out double end))   continue;
                if (start < h + 1 && end > h)
                {
                    var name = ag.FullName ?? ag.EmployeeId;
                    if (ag.Role == "Main") mainPresent.Add(name);
                    else                   backupPresent.Add(name);
                }
            }

            bool hasMain = mainPresent.Count > 0;
            if (!hasMain) gaps.Add(h);

            timeline.Add(new VwicTimelineSlotDto(
                h, $"{h:D2}:00",
                mainPresent, backupPresent,
                hasMain, hasMain || backupPresent.Count > 0
            ));
        }

        return new VwicDailyResponseDto(
            date.ToString("yyyy-MM-dd"),
            agents,
            timeline,
            gaps,
            timeline.Count(t => t.HasMainAgent),
            EndHour - StartHour
        );
    }

    public async Task<object> AssignAgentAsync(VwicAssignRequest req)
    {
        if (!DateOnly.TryParse(req.Date, out var date))
            return new { error = "Invalid date" };

        var emp = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.IsActive);
        if (emp == null)
            return new { error = "Employee not found" };

        var shiftStart   = req.ShiftStart ?? "07:00";
        var shiftEnd     = req.ShiftEnd   ?? "18:00";
        var workingShift = $"{shiftStart} - {shiftEnd}";

        var existing = await _db.WicShiftEntries
            .FirstOrDefaultAsync(w => w.EmployeeId == req.EmployeeId && w.ShiftDate == date);

        if (existing != null)
        {
            existing.SupportLocation = "VWIC";
            existing.WorkingShift    = workingShift;
            existing.IsOnSite        = true;
            existing.Task            = "VWIC";
        }
        else
        {
            _db.WicShiftEntries.Add(new WicShiftEntry
            {
                EmployeeId      = req.EmployeeId,
                ShiftDate       = date,
                DayOfWeek       = date.DayOfWeek.ToString(),
                SupportLocation = "VWIC",
                WorkingShift    = workingShift,
                IsOnSite        = true,
                Task            = "VWIC"
            });
        }

        await _db.SaveChangesAsync();

        return new { success = true, employeeId = req.EmployeeId, date = req.Date, workingShift };
    }

    public async Task<List<VwicCandidateDto>> GetCandidatesAsync()
    {
        return await _db.Employees
            .Where(e => e.IsActive
                     && e.PrimaryRole != "VWIC"
                     && (e.SecondaryRole == null || e.SecondaryRole != "VWIC"))
            .OrderBy(e => e.FullName)
            .Select(e => new VwicCandidateDto(e.EmployeeId, e.FullName, e.TeamLeadName, e.PrimaryRole))
            .ToListAsync();
    }

    public async Task<object> AddToVwicAsync(VwicManageRequest req)
    {
        var emp = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.IsActive);
        if (emp == null) return new { error = "Employee not found" };
        if (emp.PrimaryRole == "VWIC") return new { error = "Already a Main VWIC agent" };

        emp.SecondaryRole = "VWIC";
        await _db.SaveChangesAsync();
        return new { success = true, employeeId = req.EmployeeId };
    }

    public async Task<object> RemoveFromVwicAsync(VwicManageRequest req)
    {
        var emp = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.IsActive);
        if (emp == null) return new { error = "Employee not found" };
        if (emp.PrimaryRole == "VWIC") return new { error = "Cannot remove a Main VWIC agent" };

        emp.SecondaryRole = null;
        await _db.SaveChangesAsync();
        return new { success = true, employeeId = req.EmployeeId };
    }

    public async Task<VwicRotationResponse> GenerateRotationPlanAsync(VwicRotationRequest req)
    {
        if (!DateOnly.TryParse(req.Date, out var date))
            return EmptyPlan(req.Date, "Invalid date");

        int startMin  = ParseTimeToMinutes(req.StartTime);
        int endMin    = ParseTimeToMinutes(req.EndTime);
        if (startMin < 0 || endMin <= startMin)
            return EmptyPlan(req.Date, "Invalid time range");

        int intervalMin      = req.IntervalHours * 60;
        int maxSlotsPerAgent = Math.Max(1, req.MaxContinuousHours / req.IntervalHours);
        int earlyThreshold   = 8 * 60; // 08:00

        // ── Load agents ──────────────────────────────────────────────────────
        var vwicEmps = await _db.Employees
            .Where(e => e.IsActive && (e.PrimaryRole == "VWIC" || e.SecondaryRole == "VWIC"))
            .ToListAsync();
        var empIds = vwicEmps.Select(e => e.EmployeeId).ToList();

        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date && empIds.Contains(s.EmployeeId))
            .ToListAsync();
        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && empIds.Contains(sl.EmployeeId)
                      && sl.FirstDay <= date && sl.LastDay >= date)
            .ToListAsync();

        var available = vwicEmps
            .Where(emp =>
            {
                if (sickLeaves.Any(sl => sl.EmployeeId == emp.EmployeeId)) return false;
                var shift = shifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
                if (shift is null) return true; // no shift record → assume present
                return shift.ShiftType is not ("AL" or "SL" or "UL" or "OFF" or "PH" or "OFF_WEEKEND");
            })
            .OrderBy(e => e.FullName)
            .ToList();

        int n = available.Count;

        // ── Generate time slots ──────────────────────────────────────────────
        var slots = new List<(int Start, int End)>();
        for (int t = startMin; t < endMin; t += intervalMin)
            slots.Add((t, Math.Min(t + intervalMin, endMin)));
        int S = slots.Count;

        // ── Round-robin rotation ─────────────────────────────────────────────
        var consecutiveCount = new int[n];
        var slotAssignments  = new List<List<int>>();
        int rrPointer        = 0;

        for (int s = 0; s < S; s++)
        {
            int slotStart   = slots[s].Start;
            int minRequired = Math.Min(slotStart < earlyThreshold ? 1 : 3, n);
            var assigned    = new List<int>();
            var prevOn      = s > 0 ? slotAssignments[s - 1] : new List<int>();

            // 1. Carry over eligible agents from previous slot
            foreach (var idx in prevOn)
            {
                if (assigned.Count >= minRequired) break;
                if (consecutiveCount[idx] < maxSlotsPerAgent)
                    assigned.Add(idx);
            }

            // 2. Fill remaining via round-robin
            int seen = 0;
            while (assigned.Count < minRequired && seen < n)
            {
                int idx = rrPointer % n;
                rrPointer++;
                seen++;
                if (!assigned.Contains(idx))
                    assigned.Add(idx);
            }

            // 3. Update consecutive counters
            for (int i = 0; i < n; i++)
                consecutiveCount[i] = assigned.Contains(i) ? consecutiveCount[i] + 1 : 0;

            slotAssignments.Add(assigned);
        }

        // ── Build outputs ────────────────────────────────────────────────────
        var slotLabels = slots
            .Select(sl => $"{MinutesToTime(sl.Start)}–{MinutesToTime(sl.End)}")
            .ToList();

        var schedule = available.Select((emp, idx) =>
        {
            var statuses = slotAssignments.Select((assigned, s) =>
            {
                if (!assigned.Contains(idx)) return "OFF";
                bool leavingNext = s + 1 < S && !slotAssignments[s + 1].Contains(idx);
                return leavingNext && req.HandoverMinutes > 0 ? "HANDOVER" : "ON";
            }).ToList();
            return new VwicRotationScheduleRow(emp.EmployeeId, emp.FullName ?? emp.EmployeeId, statuses);
        }).ToList();

        var coverageProof = slotAssignments.Select((assigned, s) =>
        {
            int slotStart = slots[s].Start;
            int required  = Math.Min(slotStart < earlyThreshold ? 1 : 3, n);
            return new VwicCoverageProofItem(
                MinutesToTime(slots[s].Start), MinutesToTime(slots[s].End),
                assigned.Count, required, assigned.Count >= required);
        }).ToList();

        var fairness = available.Select((emp, idx) =>
        {
            int slotCount = slotAssignments.Count(a => a.Contains(idx));
            return new VwicFairnessItem(emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                slotCount * req.IntervalHours, slotCount);
        }).OrderByDescending(f => f.VwicHours).ThenBy(f => f.FullName).ToList();

        int    earlySlotCount = slots.Count(s => s.Start < earlyThreshold);
        int    mainSlotCount  = S - earlySlotCount;
        double reqHours       = earlySlotCount * 1.0 * req.IntervalHours + mainSlotCount * 3.0 * req.IntervalHours;
        double availHours     = n * (endMin - startMin) / 60.0;
        double utilRatio      = availHours > 0 ? reqHours / availHours : 0;

        string recommendation = n == 0
            ? "No available agents found for this date."
            : n < 3
                ? $"Only {n} agent(s) available — minimum 3 required from 08:00."
                : $"{req.IntervalHours}h rotation · Required: {reqHours:F0}h · Available: {availHours:F0}h · " +
                  $"Utilization: {utilRatio:P0} · Avg VWIC/agent: {reqHours / n:F1}h";

        string? fallback = n <= 3
            ? $"CRITICAL: {n} agents scheduled. 1 absence breaks minimum coverage from 08:00."
            : n == 4
                ? $"1 absence leaves exactly 3 agents — minimum met, no margin. Identify a standby."
                : $"{n} agents scheduled. 1 absence absorbed ({n - 1} remain, above min 3).";

        return new VwicRotationResponse(
            req.Date, slotLabels, schedule, coverageProof, fairness,
            recommendation, fallback, n, reqHours, availHours);
    }

    private static VwicRotationResponse EmptyPlan(string date, string msg) =>
        new(date, [], [], [], [], msg, null, 0, 0, 0);

    private static int ParseTimeToMinutes(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return -1;
        var p = time.Split(':');
        if (p.Length < 2 || !int.TryParse(p[0], out int h) || !int.TryParse(p[1], out int m))
            return -1;
        return h * 60 + m;
    }

    private static string MinutesToTime(int minutes)
    {
        int h = minutes / 60, min = minutes % 60;
        return $"{h:D2}:{min:D2}";
    }

    private static bool TryDecimalHour(string? s, out double hours)
    {
        hours = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(':');
        if (!int.TryParse(parts[0], out int h)) return false;
        int m = parts.Length > 1 && int.TryParse(parts[1], out int min) ? min : 0;
        hours = h + m / 60.0;
        return true;
    }
}

// ─── Endpoint mapper ─────────────────────────────────────────────────────────

public static class VwicEndpointMapper
{
    public static void MapVwicDailyEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/vwic").WithTags("VWIC");

        // GET /api/vwic/daily?date=2026-06-16
        grp.MapGet("/daily", async (string? date, VwicService svc) =>
            Results.Ok(await svc.GetDailyAsync(date)));

        // POST /api/vwic/assign
        grp.MapPost("/assign", async (VwicAssignRequest req, VwicService svc) =>
            Results.Ok(await svc.AssignAgentAsync(req)));

        // GET /api/vwic/candidates
        grp.MapGet("/candidates", async (VwicService svc) =>
            Results.Ok(await svc.GetCandidatesAsync()));

        // PUT /api/vwic/agents/add
        grp.MapPut("/agents/add", async (VwicManageRequest req, VwicService svc) =>
            Results.Ok(await svc.AddToVwicAsync(req)));

        // PUT /api/vwic/agents/remove
        grp.MapPut("/agents/remove", async (VwicManageRequest req, VwicService svc) =>
            Results.Ok(await svc.RemoveFromVwicAsync(req)));

        // POST /api/vwic/rotation-plan
        grp.MapPost("/rotation-plan", async (VwicRotationRequest req, VwicService svc) =>
            Results.Ok(await svc.GenerateRotationPlanAsync(req)));
    }
}
