using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Breaks;
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
    bool         HasMainAgent,   // true when MainAgents.Count >= MinRequired
    bool         HasAnyAgent,
    int          MinRequired
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
    string? PrimaryRole,
    string? ShiftType,
    string? ShiftStart,
    string? ShiftEnd,
    bool    IsWorkingToday
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

// ─── Week Plan DTOs ───────────────────────────────────────────────────────────

public record VwicWeekRequest(
    string  WeekStartDate,
    string? StartTime,
    string  EndTime,
    int     IntervalHours,
    int     MaxContinuousHours,
    int     HandoverMinutes
);

public record VwicWeekDayPlan(
    string                        Date,
    string                        DayName,
    string                        DayShort,
    List<string>                  SlotLabels,
    List<VwicRotationScheduleRow> Schedule,
    List<VwicCoverageProofItem>   CoverageProof,
    List<VwicFairnessItem>        DailyFairness,
    string                        Recommendation,
    string?                       FallbackWarning,
    int                           AvailableAgents,
    double                        RequiredAgentHours,
    double                        AvailableAgentHours
);

public record VwicWeekFairnessItem(
    string EmployeeId,
    string FullName,
    double TotalVwicHours,
    int    TotalSlotCount,
    int    DaysWorked
);

public record VwicWeekResponse(
    string                     WeekStartDate,
    List<VwicWeekDayPlan>      Days,
    List<VwicWeekFairnessItem> WeeklyFairness
);

// ─── Save Rotation DTOs ───────────────────────────────────────────────────────

public record SaveRotationSlotRow(string EmployeeId, List<string> SlotStatus);
public record SaveRotationRequest(string Date, List<string> SlotLabels, List<SaveRotationSlotRow> Schedule);

// ─── Service ─────────────────────────────────────────────────────────────────

public class VwicService
{
    private readonly GSDContext _db;
    public VwicService(GSDContext db) => _db = db;

    // 00:00–07:00 → 1, 07:00–17:00 → 3, 17:00–24:00 → 1
    private static int MinRequired(int hour) => hour < 7 ? 1 : hour < 17 ? 3 : 1;

    public async Task<VwicDailyResponseDto> GetDailyAsync(string? dateStr)
    {
        var date = dateStr != null && DateOnly.TryParse(dateStr, out var d)
            ? d : DateOnly.FromDateTime(DateTime.Today);

        // All active Voice + VWIC-role employees
        // (VWIC-role agents auto-count toward coverage when working, so they must be included)
        var voiceEmps = await _db.Employees
            .Where(e => e.IsActive && e.PrimaryRole != "2nd Level" && (
                e.PrimaryRole == "Voice"  || e.SecondaryRole == "Voice" ||
                e.PrimaryRole == "VWIC"   || e.SecondaryRole == "VWIC"))
            .ToListAsync();
        var empIds = voiceEmps.Select(e => e.EmployeeId).ToList();

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

        // Break slots for today that are SCHEDULED or ON_BREAK (reduce coverage for those hours)
        var breakSlots = await _db.BreakSlots
            .Where(b => b.BreakDate == date
                     && (b.Status == BreakStatus.SCHEDULED || b.Status == BreakStatus.ON_BREAK))
            .ToListAsync();

        // VWIC assignments today: any Voice employee with SupportLocation=VWIC
        var vwicAssignments = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date
                     && empIds.Contains(w.EmployeeId)
                     && w.SupportLocation == "VWIC")
            .ToListAsync();

        // Main = VWIC-assigned today; Backup = Voice but not assigned today
        var agents = voiceEmps.Select(emp =>
        {
            var shift     = shifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
            var sl        = sickLeaves.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
            var vwicEntry = vwicAssignments.FirstOrDefault(w => w.EmployeeId == emp.EmployeeId);
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

            // Role determination:
            //   "Main"   = manual WicShiftEntry assignment
            //           OR agent has PrimaryRole/SecondaryRole=VWIC and is working today
            //   "Backup" = all other Voice agents (visible in agent list but don't count
            //              toward the minimum coverage requirement)
            bool hasVwicRole    = emp.PrimaryRole == "VWIC" || emp.SecondaryRole == "VWIC";
            bool isWorkingToday = !isAbsent && shift?.ShiftType == "WORKING";
            var  role           = (vwicEntry != null || (hasVwicRole && isWorkingToday))
                                  ? "Main" : "Backup";

            // Prefer WicShiftEntry times; fall back to ShiftEntry times
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
        .OrderBy(a => a.Role)
        .ThenBy(a => a.FullName)
        .ToList();

        // Build 24-hour coverage timeline
        var timeline     = new List<VwicTimelineSlotDto>();
        var gaps         = new List<int>();
        var activeAgents = agents
            .Where(a => !a.IsAbsent && a.ShiftStart != null && a.ShiftEnd != null)
            .ToList();

        for (int h = 0; h < 24; h++)
        {
            int min           = MinRequired(h);
            var mainPresent   = new List<string>();
            var backupPresent = new List<string>();

            foreach (var ag in activeAgents)
            {
                if (!TryDecimalHour(ag.ShiftStart, out double start)) continue;
                if (!TryDecimalHour(ag.ShiftEnd,   out double end))   continue;

                // Overnight shifts cross midnight when end < start (e.g. 22:00–07:00).
                // Normal:          cover hour h if h in [start, end)
                // Cross-midnight:  cover hour h if h >= start OR h < end
                bool covers = end >= start
                    ? (h >= start && h < end)
                    : (h >= start || h < end);

                if (covers)
                {
                    // Exclude agent if they have an active break slot covering this hour
                    bool onBreak = breakSlots.Any(b =>
                        b.EmployeeId == ag.EmployeeId &&
                        BreakService.BreakCoverHour(b.BreakStart, b.BreakEnd, h));
                    if (onBreak) continue;

                    var name = ag.FullName ?? ag.EmployeeId;
                    if (ag.Role == "Main") mainPresent.Add(name);
                    else                   backupPresent.Add(name);
                }
            }

            // All working Voice+VWIC agents cover VWIC during their shift hours.
            // Main = manually assigned or VWIC-role (display distinction only).
            // Coverage minimum counts everyone present, regardless of display role.
            bool hasMin = (mainPresent.Count + backupPresent.Count) >= min;
            if (!hasMin) gaps.Add(h);

            timeline.Add(new VwicTimelineSlotDto(
                h, $"{h:D2}:00",
                mainPresent, backupPresent,
                hasMin, mainPresent.Count > 0 || backupPresent.Count > 0,
                min
            ));
        }

        return new VwicDailyResponseDto(
            date.ToString("yyyy-MM-dd"),
            agents,
            timeline,
            gaps,
            timeline.Count(t => t.HasMainAgent),
            24
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

    public async Task<List<VwicCandidateDto>> GetCandidatesAsync(string? dateStr)
    {
        var voiceEmps = await _db.Employees
            .Where(e => e.IsActive && e.PrimaryRole != "2nd Level" && (e.PrimaryRole == "Voice" || e.SecondaryRole == "Voice"))
            .ToListAsync();

        // No date → return all Voice employees for Add-Agent modal (no shift filter)
        if (dateStr == null || !DateOnly.TryParse(dateStr, out var date))
        {
            return voiceEmps
                .OrderBy(e => e.FullName)
                .Select(e => new VwicCandidateDto(e.EmployeeId, e.FullName, e.TeamLeadName, e.PrimaryRole, null, null, null, false))
                .ToList();
        }

        var empIds = voiceEmps.Select(e => e.EmployeeId).ToList();

        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date && empIds.Contains(s.EmployeeId))
            .ToListAsync();

        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null
                      && empIds.Contains(sl.EmployeeId)
                      && sl.FirstDay <= date
                      && sl.LastDay  >= date)
            .ToListAsync();

        return voiceEmps
            .Select(emp =>
            {
                var shift    = shifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
                var sl       = sickLeaves.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
                bool isAbsent  = sl != null
                    || shift?.ShiftType is "AL" or "SL" or "UL" or "OFF" or "PH" or "OFF_WEEKEND" or "HALF_AL";
                bool isWorking = !isAbsent && shift?.ShiftType == "WORKING";
                return (emp, shift, isAbsent, isWorking);
            })
            .Where(x => !x.isAbsent)
            .OrderByDescending(x => x.isWorking)
            .ThenBy(x => x.emp.FullName)
            .Select(x => new VwicCandidateDto(
                x.emp.EmployeeId,
                x.emp.FullName,
                x.emp.TeamLeadName,
                x.emp.PrimaryRole,
                x.shift?.ShiftType,
                x.shift?.ShiftStart,
                x.shift?.ShiftEnd,
                x.isWorking
            ))
            .ToList();
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
        var voiceEmps = await _db.Employees
            .Where(e => e.IsActive && e.PrimaryRole != "2nd Level" && (e.PrimaryRole == "Voice" || e.SecondaryRole == "Voice"))
            .ToListAsync();
        var empIds = voiceEmps.Select(e => e.EmployeeId).ToList();

        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date && empIds.Contains(s.EmployeeId))
            .ToListAsync();
        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && empIds.Contains(sl.EmployeeId)
                      && sl.FirstDay <= date && sl.LastDay >= date)
            .ToListAsync();

        var available = voiceEmps
            .Where(emp =>
            {
                if (sickLeaves.Any(sl => sl.EmployeeId == emp.EmployeeId)) return false;
                var shift = shifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
                if (shift is null) return false; // no shift record → not scheduled that day
                return shift.ShiftType == "WORKING";
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

    public async Task<VwicWeekResponse> GenerateWeekRotationPlanAsync(VwicWeekRequest req)
    {
        if (!DateOnly.TryParse(req.WeekStartDate, out var monday))
            return new VwicWeekResponse(req.WeekStartDate, [], []);

        int startMin = ParseTimeToMinutes(req.StartTime ?? "07:00");
        int endMin   = ParseTimeToMinutes(req.EndTime);
        if (startMin < 0 || endMin <= startMin)
            return new VwicWeekResponse(req.WeekStartDate, [], []);

        int intervalMin      = req.IntervalHours * 60;
        int maxSlotsPerAgent = Math.Max(1, req.MaxContinuousHours / req.IntervalHours);
        int earlyThreshold   = 8 * 60;

        var allVoiceEmps = await _db.Employees
            .Where(e => e.IsActive && (e.PrimaryRole == "Voice" || e.SecondaryRole == "Voice"))
            .ToListAsync();
        var allEmpIds = allVoiceEmps.Select(e => e.EmployeeId).ToList();

        var weekDates = Enumerable.Range(0, 5).Select(i => monday.AddDays(i)).ToList();
        var weekStart = weekDates[0];
        var weekEnd   = weekDates[4];

        var allShifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= weekStart && s.ShiftDate <= weekEnd
                     && allEmpIds.Contains(s.EmployeeId))
            .ToListAsync();

        var allSickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && allEmpIds.Contains(sl.EmployeeId)
                      && sl.FirstDay <= weekEnd && sl.LastDay >= weekStart)
            .ToListAsync();

        var cumulativeHours = allVoiceEmps.ToDictionary(e => e.EmployeeId, _ => 0.0);

        var dayNames  = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
        var dayShorts = new[] { "Mo", "Di", "Mi", "Do", "Fr" };

        var dayPlans = new List<VwicWeekDayPlan>();

        for (int dayIdx = 0; dayIdx < 5; dayIdx++)
        {
            var date          = weekDates[dayIdx];
            var dayShifts     = allShifts.Where(s => s.ShiftDate == date).ToList();
            var daySickLeaves = allSickLeaves
                .Where(sl => sl.FirstDay <= date && sl.LastDay >= date).ToList();

            var available = allVoiceEmps
                .Where(emp =>
                {
                    if (daySickLeaves.Any(sl => sl.EmployeeId == emp.EmployeeId)) return false;
                    var shift = dayShifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
                    if (shift is null) return false;
                    return shift.ShiftType == "WORKING";
                })
                .OrderBy(e => cumulativeHours[e.EmployeeId])
                .ThenBy(e => e.FullName)
                .ToList();

            int n = available.Count;

            var slots = new List<(int Start, int End)>();
            for (int t = startMin; t < endMin; t += intervalMin)
                slots.Add((t, Math.Min(t + intervalMin, endMin)));
            int S = slots.Count;

            var consecutiveCount = new int[n];
            var slotAssignments  = new List<List<int>>();
            int rrPointer        = 0;

            for (int s = 0; s < S; s++)
            {
                int slotStart   = slots[s].Start;
                int minRequired = Math.Min(slotStart < earlyThreshold ? 1 : 3, n);
                var assigned    = new List<int>();
                var prevOn      = s > 0 ? slotAssignments[s - 1] : new List<int>();

                foreach (var idx in prevOn)
                {
                    if (assigned.Count >= minRequired) break;
                    if (consecutiveCount[idx] < maxSlotsPerAgent)
                        assigned.Add(idx);
                }

                int seen = 0;
                while (assigned.Count < minRequired && seen < n)
                {
                    int idx = rrPointer % n;
                    rrPointer++;
                    seen++;
                    if (!assigned.Contains(idx))
                        assigned.Add(idx);
                }

                for (int i = 0; i < n; i++)
                    consecutiveCount[i] = assigned.Contains(i) ? consecutiveCount[i] + 1 : 0;

                slotAssignments.Add(assigned);
            }

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

            var dailyFairness = available.Select((emp, idx) =>
            {
                int slotCount = slotAssignments.Count(a => a.Contains(idx));
                return new VwicFairnessItem(emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                    slotCount * req.IntervalHours, slotCount);
            }).OrderByDescending(f => f.VwicHours).ThenBy(f => f.FullName).ToList();

            foreach (var fi in dailyFairness)
                cumulativeHours[fi.EmployeeId] += fi.VwicHours;

            int    earlySlotCount = slots.Count(s => s.Start < earlyThreshold);
            int    mainSlotCount  = S - earlySlotCount;
            double reqHours       = earlySlotCount * 1.0 * req.IntervalHours
                                  + mainSlotCount  * 3.0 * req.IntervalHours;
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
                    ? "1 absence leaves exactly 3 agents — minimum met, no margin. Identify a standby."
                    : $"{n} agents scheduled. 1 absence absorbed ({n - 1} remain, above min 3).";

            dayPlans.Add(new VwicWeekDayPlan(
                date.ToString("yyyy-MM-dd"), dayNames[dayIdx], dayShorts[dayIdx],
                slotLabels, schedule, coverageProof, dailyFairness,
                recommendation, fallback, n, reqHours, availHours));
        }

        var weeklyFairness = allVoiceEmps
            .Where(emp => cumulativeHours[emp.EmployeeId] > 0
                       || dayPlans.Any(d => d.Schedule.Any(s => s.EmployeeId == emp.EmployeeId)))
            .Select(emp => new VwicWeekFairnessItem(
                emp.EmployeeId,
                emp.FullName ?? emp.EmployeeId,
                cumulativeHours[emp.EmployeeId],
                dayPlans.Sum(d =>
                    d.DailyFairness.FirstOrDefault(f => f.EmployeeId == emp.EmployeeId)?.SlotCount ?? 0),
                dayPlans.Count(d => d.Schedule.Any(s => s.EmployeeId == emp.EmployeeId))
            ))
            .OrderBy(w => w.TotalVwicHours)
            .ThenBy(w => w.FullName)
            .ToList();

        return new VwicWeekResponse(req.WeekStartDate, dayPlans, weeklyFairness);
    }

    public async Task<byte[]> ExportWeekPlanAsync(VwicWeekRequest req)
    {
        var plan = await GenerateWeekRotationPlanAsync(req);

        using var wb = new XLWorkbook();

        foreach (var day in plan.Days)
        {
            var ws = wb.AddWorksheet(day.DayShort);

            ws.Cell(1, 1).Value = "Slot";
            for (int c = 0; c < day.Schedule.Count; c++)
                ws.Cell(1, c + 2).Value = day.Schedule[c].FullName;

            var hdr = ws.Row(1);
            hdr.Style.Font.Bold            = true;
            hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
            hdr.Style.Font.FontColor       = XLColor.White;

            for (int si = 0; si < day.SlotLabels.Count; si++)
            {
                int row = si + 2;
                ws.Cell(row, 1).Value           = day.SlotLabels[si];
                ws.Cell(row, 1).Style.Font.Bold = true;

                for (int c = 0; c < day.Schedule.Count; c++)
                {
                    var status = day.Schedule[c].SlotStatus[si];
                    var cell   = ws.Cell(row, c + 2);
                    cell.Value = status;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    if (status == "ON")
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#166534");
                        cell.Style.Font.FontColor       = XLColor.FromHtml("#86efac");
                    }
                    else if (status == "HANDOVER")
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#854d0e");
                        cell.Style.Font.FontColor       = XLColor.FromHtml("#fde68a");
                    }
                    else
                    {
                        cell.Style.Font.FontColor = XLColor.FromHtml("#64748b");
                    }
                }
            }

            ws.Columns().AdjustToContents();
        }

        var summary      = wb.AddWorksheet("Summary");
        var dayShortList = plan.Days.Select(d => d.DayShort).ToList();

        summary.Cell(1, 1).Value = "Agent";
        for (int d = 0; d < dayShortList.Count; d++)
            summary.Cell(1, d + 2).Value = dayShortList[d];
        summary.Cell(1, dayShortList.Count + 2).Value = "Total h";
        summary.Cell(1, dayShortList.Count + 3).Value = "Slots";
        summary.Cell(1, dayShortList.Count + 4).Value = "Days";

        var sh = summary.Row(1);
        sh.Style.Font.Bold            = true;
        sh.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
        sh.Style.Font.FontColor       = XLColor.White;

        for (int r = 0; r < plan.WeeklyFairness.Count; r++)
        {
            var item = plan.WeeklyFairness[r];
            int row  = r + 2;

            summary.Cell(row, 1).Value = item.FullName;

            for (int d = 0; d < plan.Days.Count; d++)
            {
                var df   = plan.Days[d].DailyFairness.FirstOrDefault(f => f.EmployeeId == item.EmployeeId);
                var cell = summary.Cell(row, d + 2);
                cell.Value = df?.VwicHours ?? 0;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            summary.Cell(row, plan.Days.Count + 2).Value           = item.TotalVwicHours;
            summary.Cell(row, plan.Days.Count + 2).Style.Font.Bold = true;
            summary.Cell(row, plan.Days.Count + 3).Value           = item.TotalSlotCount;
            summary.Cell(row, plan.Days.Count + 4).Value           = item.DaysWorked;
        }

        summary.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<object> SaveRotationSlotsAsync(SaveRotationRequest req)
    {
        if (!DateOnly.TryParse(req.Date, out var date))
            return new { error = "Invalid date" };

        var existing = await _db.VwicRotationSlots
            .Where(r => r.RotationDate == date)
            .ToListAsync();
        _db.VwicRotationSlots.RemoveRange(existing);

        var toInsert = new List<VwicRotationSlot>();
        for (int si = 0; si < req.SlotLabels.Count; si++)
        {
            var label = req.SlotLabels[si];
            // Format is always "HH:mm–HH:mm" (en-dash, 11 chars)
            if (label.Length < 11) continue;
            var slotStart = label[..5];
            var slotEnd   = label[^5..];

            foreach (var row in req.Schedule)
            {
                if (si >= row.SlotStatus.Count) continue;
                var status = row.SlotStatus[si];
                if (status == "ON" || status == "HANDOVER")
                    toInsert.Add(new VwicRotationSlot {
                        EmployeeId   = row.EmployeeId,
                        RotationDate = date,
                        SlotStart    = slotStart,
                        SlotEnd      = slotEnd,
                    });
            }
        }

        _db.VwicRotationSlots.AddRange(toInsert);
        await _db.SaveChangesAsync();
        return new { saved = toInsert.Count, date = req.Date };
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

        // GET /api/vwic/candidates?date=yyyy-MM-dd  (date optional; omit for Add-Agent modal)
        grp.MapGet("/candidates", async (string? date, VwicService svc) =>
            Results.Ok(await svc.GetCandidatesAsync(date)));

        // PUT /api/vwic/agents/add
        grp.MapPut("/agents/add", async (VwicManageRequest req, VwicService svc) =>
            Results.Ok(await svc.AddToVwicAsync(req)));

        // PUT /api/vwic/agents/remove
        grp.MapPut("/agents/remove", async (VwicManageRequest req, VwicService svc) =>
            Results.Ok(await svc.RemoveFromVwicAsync(req)));

        // POST /api/vwic/rotation-plan
        grp.MapPost("/rotation-plan", async (VwicRotationRequest req, VwicService svc) =>
            Results.Ok(await svc.GenerateRotationPlanAsync(req)));

        // POST /api/vwic/rotation-plan/save
        grp.MapPost("/rotation-plan/save", async (SaveRotationRequest req, VwicService svc) =>
            Results.Ok(await svc.SaveRotationSlotsAsync(req)));

        // POST /api/vwic/rotation-plan-week
        grp.MapPost("/rotation-plan-week", async (VwicWeekRequest req, VwicService svc) =>
            Results.Ok(await svc.GenerateWeekRotationPlanAsync(req)));

        // POST /api/vwic/rotation-plan-week/export
        grp.MapPost("/rotation-plan-week/export", async (VwicWeekRequest req, VwicService svc) =>
        {
            var bytes = await svc.ExportWeekPlanAsync(req);
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"VWIC_Week_{req.WeekStartDate}.xlsx");
        });
    }
}
