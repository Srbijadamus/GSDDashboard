using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Breaks;

// ─── DTOs ────────────────────────────────────────────────────────────────────

public record BreakSlotDto(
    int      Id,
    string   EmployeeId,
    string?  FullName,
    string?  TeamLeadName,
    string   BreakDate,
    string   BreakStart,
    string   BreakEnd,
    string?  ActualStart,
    string?  ActualEnd,
    int      DurationMinutes,
    string   Status,
    string?  AgentRole
);

public record AutoDistributeRequest(
    string Date,
    string WindowStart,   // "11:00"
    string WindowEnd,     // "14:00"
    double VoiceMinPct    // 0.70 → keep 70% on line; max 30% on break simultaneously
);

public record ManualBreakRequest(
    string EmployeeId,
    string Date,
    string BreakStart,
    int    DurationMinutes
);

public record BreakDistributeResult(
    string         Date,
    int            Scheduled,
    int            Unscheduled,
    int            MaxVwicConcurrent,
    int            MaxVoiceConcurrent,
    int            TotalVwic,
    int            TotalVoice,
    List<string>   UnscheduledAgents,
    List<BreakSlotDto> Slots
);

// ─── Service ─────────────────────────────────────────────────────────────────

public class BreakService
{
    private readonly GSDContext _db;
    public BreakService(GSDContext db) => _db = db;

    private static bool ParseHHMM(string? s, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var p = s.Split(':');
        if (!int.TryParse(p[0], out int h)) return false;
        int m = p.Length > 1 && int.TryParse(p[1], out int mm) ? mm : 0;
        minutes = h * 60 + m;
        return true;
    }

    private static string MinToHHMM(int minutes)
    {
        int h = Math.Min(minutes / 60, 23);
        int m = minutes % 60;
        return $"{h:D2}:{m:D2}";
    }

    private static int ParseFallback(string? s, int fallback) =>
        ParseHHMM(s, out int v) ? v : fallback;

    // True when [aStart,aEnd) and [bStart,bEnd) share any minute
    private static bool Overlaps(int aStart, int aEnd, int bStart, int bEnd) =>
        aStart < bEnd && bStart < aEnd;

    // Does a break slot (stored as "HH:mm") overlap hour slot h (h:00 → h+1:00)?
    public static bool BreakCoverHour(string breakStart, string breakEnd, int h)
    {
        if (!ParseHHMM(breakStart, out int bs) || !ParseHHMM(breakEnd, out int be)) return false;
        return Overlaps(bs, be, h * 60, (h + 1) * 60);
    }

    public async Task<List<BreakSlotDto>> GetBreaksAsync(string? dateStr)
    {
        var date = dateStr != null && DateOnly.TryParse(dateStr, out var d)
            ? d : DateOnly.FromDateTime(DateTime.Today);

        var slots = await _db.BreakSlots
            .Where(b => b.BreakDate == date)
            .OrderBy(b => b.BreakStart)
            .ToListAsync();

        if (slots.Count == 0) return [];

        var slotEmpIds = slots.Select(s => s.EmployeeId).Distinct().ToList();

        // Determine which agents are absent today so stale slots are hidden
        var sickToday = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && slotEmpIds.Contains(sl.EmployeeId!)
                      && sl.FirstDay <= date && sl.LastDay >= date)
            .Select(sl => sl.EmployeeId!)
            .ToListAsync();

        var vacToday = await _db.Vacations
            .Where(v => v.EmployeeId != null && slotEmpIds.Contains(v.EmployeeId!)
                     && v.FirstDay <= date && v.LastDay >= date)
            .Select(v => v.EmployeeId!)
            .ToListAsync();

        var nonWorkingToday = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date && slotEmpIds.Contains(s.EmployeeId)
                     && s.ShiftType != "WORKING")
            .Select(s => s.EmployeeId)
            .ToListAsync();

        var absentIds = new HashSet<string>(sickToday.Concat(vacToday).Concat(nonWorkingToday));

        var activeSlots = slots.Where(s => !absentIds.Contains(s.EmployeeId)).ToList();
        var emps = await LoadEmpsAsync(activeSlots.Select(s => s.EmployeeId));

        return activeSlots
            .OrderBy(b => b.BreakStart)
            .ThenBy(b => emps.GetValueOrDefault(b.EmployeeId)?.FullName)
            .Select(b => ToDto(b, emps.GetValueOrDefault(b.EmployeeId)))
            .ToList();
    }

    public async Task<BreakDistributeResult> AutoDistributeAsync(AutoDistributeRequest req)
    {
        if (!DateOnly.TryParse(req.Date, out var date))
            return EmptyResult(req.Date, "Invalid date");

        int windowStartMin = ParseFallback(req.WindowStart, 690);  // default 11:30
        int windowEndMin   = ParseFallback(req.WindowEnd,   870);  // default 14:30
        const int breakDur = 30;
        const int step     = 15;

        if (windowEndMin - windowStartMin < breakDur)
            return EmptyResult(req.Date, "Window too narrow for a break");

        // ── Pool: ALL working Voice agents (excludes 2nd Level and WIC) ───────
        var allEmps = await _db.Employees
            .Where(e => e.IsActive
                     && e.PrimaryRole != "2nd Level"
                     && e.PrimaryRole != "WIC"
                     && (e.PrimaryRole == "Voice" || e.SecondaryRole == "Voice"))
            .ToListAsync();

        var empIds = allEmps.Select(e => e.EmployeeId).ToList();

        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date
                     && empIds.Contains(s.EmployeeId)
                     && s.ShiftType == "WORKING")
            .ToListAsync();

        // ── Absences: sick leave + vacation (HashSet for O(1) lookup) ──────────
        var sickIds = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && empIds.Contains(sl.EmployeeId!)
                      && sl.FirstDay <= date && sl.LastDay >= date)
            .Select(sl => sl.EmployeeId!)
            .ToListAsync();

        var vacIds = await _db.Vacations
            .Where(v => v.EmployeeId != null && empIds.Contains(v.EmployeeId!)
                     && v.FirstDay <= date && v.LastDay >= date)
            .Select(v => v.EmployeeId!)
            .ToListAsync();

        var absentIds = new HashSet<string>(sickIds.Concat(vacIds));

        // ── VWIC rotation slots for the date (persisted via Save Rotation) ────
        var rotationSlots = await _db.VwicRotationSlots
            .Where(r => r.RotationDate == date && empIds.Contains(r.EmployeeId))
            .ToListAsync();

        // Group by agent: pre-parse slot times to minutes for fast overlap checks
        var rotationByEmp = rotationSlots
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g
                .Select(r => (Start: ParseFallback(r.SlotStart, -1),
                              End:   ParseFallback(r.SlotEnd,   -1)))
                .Where(r => r.Start >= 0 && r.End > r.Start)
                .ToList());

        // ── Eligible = working today (WORKING shift) AND not absent ─────────
        // shifts is pre-filtered to ShiftType=="WORKING", so SL/AL/UL/OFF shifts
        // are already excluded implicitly; absentIds is defense-in-depth for
        // cases where the ShiftEntry wasn't updated to reflect the absence.
        var eligible = allEmps.Where(emp =>
        {
            if (absentIds.Contains(emp.EmployeeId)) return false;
            var shift = shifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
            if (shift == null) return false;   // no WORKING shift today
            if (!ParseHHMM(shift.ShiftStart, out int sStart)) return false;
            if (!ParseHHMM(shift.ShiftEnd,   out int sEnd))   return false;
            if (sEnd <= sStart) sEnd += 24 * 60;
            return Overlaps(sStart, sEnd, windowStartMin, windowEndMin);
        }).ToList();

        int totalPool = eligible.Count;
        int vwicToday = eligible.Count(e => rotationByEmp.ContainsKey(e.EmployeeId));

        // ── Concurrent limit: keep voiceMinPct% on line at every 15-min step ─
        double voiceMinPct = req.VoiceMinPct > 1.0 ? req.VoiceMinPct / 100.0 : req.VoiceMinPct;
        voiceMinPct = Math.Clamp(voiceMinPct, 0.0, 1.0);
        int maxConcurrent = totalPool > 0
            ? Math.Max(1, (int)Math.Floor(totalPool * (1.0 - voiceMinPct)))
            : 0;

        // ── Candidate slots (15-min steps; break must end ≤ windowEnd) ────────
        var candidates = new List<int>();
        for (int t = windowStartMin; t <= windowEndMin - breakDur; t += step)
            candidates.Add(t);

        // ── Seeded shuffle for daily fairness ─────────────────────────────────
        int seed = date.DayOfYear * 100 + date.Year % 100;
        var rng  = new System.Random(seed);
        var orderedAgents = eligible.OrderBy(_ => rng.Next()).ToList();

        // ── Least-loaded slot assignment ──────────────────────────────────────
        // For each agent:
        //   1. Skip slots that overlap their VWIC rotation (guaranteed coverage)
        //   2. Skip slots that would breach the maxConcurrent line-staffing limit
        //   3. Among eligible slots, pick the one with fewest starts (even spread)
        var assigned    = new List<(string EmpId, int StartMin, int EndMin)>();
        var startCount  = candidates.ToDictionary(c => c, _ => 0);
        var unscheduled = new List<string>();

        foreach (var emp in orderedAgents)
        {
            if (maxConcurrent == 0)
            {
                unscheduled.Add(emp.FullName ?? emp.EmployeeId);
                continue;
            }

            var shift = shifts.FirstOrDefault(s => s.EmployeeId == emp.EmployeeId);
            if (!ParseHHMM(shift?.ShiftStart, out int shiftStart) ||
                !ParseHHMM(shift?.ShiftEnd,   out int shiftEnd))
            {
                unscheduled.Add(emp.FullName ?? emp.EmployeeId);
                continue;
            }
            if (shiftEnd <= shiftStart) shiftEnd += 24 * 60;

            var vwicSlots = rotationByEmp.GetValueOrDefault(emp.EmployeeId) ?? [];

            int bestSlot = -1, bestLoad = int.MaxValue;

            foreach (int cStart in candidates)
            {
                int cEnd = cStart + breakDur;

                // Break must fall within the agent's working shift
                if (!Overlaps(shiftStart, shiftEnd, cStart, cEnd)) continue;

                // Break must NOT overlap any VWIC rotation slot for this agent
                if (vwicSlots.Any(v => Overlaps(v.Start, v.End, cStart, cEnd))) continue;

                // Global concurrent limit across all agents
                int concurrent = assigned.Count(a => Overlaps(a.StartMin, a.EndMin, cStart, cEnd));
                if (concurrent >= maxConcurrent) continue;

                // Pick the slot with the fewest starts so far (even distribution)
                int load = startCount[cStart];
                if (load < bestLoad) { bestLoad = load; bestSlot = cStart; }
            }

            if (bestSlot >= 0)
            {
                assigned.Add((emp.EmployeeId, bestSlot, bestSlot + breakDur));
                startCount[bestSlot]++;
            }
            else
            {
                unscheduled.Add(emp.FullName ?? emp.EmployeeId);
            }
        }

        // ── Persist ───────────────────────────────────────────────────────────
        var toDelete = await _db.BreakSlots
            .Where(b => b.BreakDate == date && b.Status == BreakStatus.SCHEDULED)
            .ToListAsync();
        _db.BreakSlots.RemoveRange(toDelete);

        _db.BreakSlots.AddRange(assigned.Select(a => new BreakSlot
        {
            EmployeeId      = a.EmpId,
            BreakDate       = date,
            BreakStart      = MinToHHMM(a.StartMin),
            BreakEnd        = MinToHHMM(a.EndMin),
            DurationMinutes = breakDur,
            Status          = BreakStatus.SCHEDULED,
            // Tag agents who are on VWIC rotation today for display clarity
            AgentRole       = rotationByEmp.ContainsKey(a.EmpId) ? "VWIC" : "Voice"
        }));

        await _db.SaveChangesAsync();

        var savedSlots = await _db.BreakSlots
            .Where(b => b.BreakDate == date)
            .OrderBy(b => b.BreakStart)
            .ToListAsync();

        var savedEmps = await LoadEmpsAsync(savedSlots.Select(s => s.EmployeeId));

        return new BreakDistributeResult(
            req.Date,
            assigned.Count,
            unscheduled.Count,
            0,             // MaxVwicConcurrent — no separate VWIC pool; rotation covers it
            maxConcurrent,
            vwicToday,     // TotalVwic — agents with a saved VWIC rotation slot today
            totalPool,     // TotalVoice — total eligible Voice pool
            unscheduled,
            savedSlots.Select(b => ToDto(b, savedEmps.GetValueOrDefault(b.EmployeeId))).ToList()
        );
    }

    public async Task<object> StartBreakAsync(int id)
    {
        var slot = await _db.BreakSlots.FindAsync(id);
        if (slot == null) return new { error = "Break slot not found" };
        if (slot.Status is BreakStatus.DONE or BreakStatus.CANCELLED)
            return new { error = $"Cannot start a {slot.Status} break" };

        var nowStr  = DateTime.Now.ToString("HH:mm");
        ParseHHMM(nowStr, out int nowMin);
        slot.ActualStart = nowStr;
        slot.BreakEnd    = MinToHHMM(nowMin + slot.DurationMinutes);
        slot.Status      = BreakStatus.ON_BREAK;

        await _db.SaveChangesAsync();
        return new { success = true };
    }

    public async Task<object> EndBreakAsync(int id)
    {
        var slot = await _db.BreakSlots.FindAsync(id);
        if (slot == null) return new { error = "Break slot not found" };
        if (slot.Status != BreakStatus.ON_BREAK)
            return new { error = "Break is not currently in progress" };

        slot.ActualEnd = DateTime.Now.ToString("HH:mm");
        slot.Status    = BreakStatus.DONE;

        await _db.SaveChangesAsync();
        return new { success = true };
    }

    public async Task<object> CancelBreakAsync(int id)
    {
        var slot = await _db.BreakSlots.FindAsync(id);
        if (slot == null) return new { error = "Break slot not found" };
        if (slot.Status == BreakStatus.DONE)
            return new { error = "Cannot cancel a completed break" };

        slot.Status = BreakStatus.CANCELLED;
        await _db.SaveChangesAsync();
        return new { success = true };
    }

    public async Task<object> CreateManualBreakAsync(ManualBreakRequest req)
    {
        if (!DateOnly.TryParse(req.Date, out var date))
            return new { error = "Invalid date" };
        if (!ParseHHMM(req.BreakStart, out int startMin))
            return new { error = "Invalid break start time" };

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId);
        if (emp == null) return new { error = "Employee not found" };

        int dur  = req.DurationMinutes > 0 ? req.DurationMinutes : 30;
        string role = (emp.PrimaryRole == "VWIC" || emp.SecondaryRole == "VWIC") ? "VWIC" : "Voice";

        // Remove any existing SCHEDULED slot for this agent on this date
        var existing = await _db.BreakSlots
            .Where(b => b.EmployeeId == req.EmployeeId
                     && b.BreakDate  == date
                     && b.Status     == BreakStatus.SCHEDULED)
            .ToListAsync();
        _db.BreakSlots.RemoveRange(existing);

        _db.BreakSlots.Add(new BreakSlot
        {
            EmployeeId      = req.EmployeeId,
            BreakDate       = date,
            BreakStart      = MinToHHMM(startMin),
            BreakEnd        = MinToHHMM(startMin + dur),
            DurationMinutes = dur,
            Status          = BreakStatus.SCHEDULED,
            AgentRole       = role
        });

        await _db.SaveChangesAsync();
        return new { success = true };
    }

    private async Task<Dictionary<string, Employee>> LoadEmpsAsync(IEnumerable<string> employeeIds)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, Employee>();
        return await _db.Employees
            .Where(e => ids.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId);
    }

    private static BreakSlotDto ToDto(BreakSlot b, Employee? emp) => new(
        b.Id,
        b.EmployeeId,
        emp?.FullName,
        emp?.TeamLeadName,
        b.BreakDate.ToString("yyyy-MM-dd"),
        b.BreakStart,
        b.BreakEnd,
        b.ActualStart,
        b.ActualEnd,
        b.DurationMinutes,
        b.Status.ToString(),
        b.AgentRole
    );

    private static BreakDistributeResult EmptyResult(string date, string reason) =>
        new(date, 0, 0, 0, 0, 0, 0, [reason], []);
}

// ─── Endpoint mapper ─────────────────────────────────────────────────────────

public static class BreakEndpointMapper
{
    private static object Err(Exception ex) => new
    {
        error  = ex.Message,
        inner  = ex.InnerException?.Message,
        inner2 = ex.InnerException?.InnerException?.Message,
        type   = ex.GetType().FullName,
        stack  = ex.StackTrace
    };

    public static void MapBreakEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/breaks").WithTags("Breaks");

        // GET /api/breaks?date=yyyy-MM-dd
        grp.MapGet("/", async (string? date, BreakService svc) =>
        {
            try   { return Results.Ok(await svc.GetBreaksAsync(date)); }
            catch (Exception ex) { return Results.Json(Err(ex), statusCode: 500); }
        });

        // POST /api/breaks/auto-distribute
        grp.MapPost("/auto-distribute", async (AutoDistributeRequest req, BreakService svc) =>
        {
            try   { return Results.Ok(await svc.AutoDistributeAsync(req)); }
            catch (Exception ex) { return Results.Json(Err(ex), statusCode: 500); }
        });

        // POST /api/breaks/{id}/start
        grp.MapPost("/{id:int}/start", async (int id, BreakService svc) =>
        {
            try   { return Results.Ok(await svc.StartBreakAsync(id)); }
            catch (Exception ex) { return Results.Json(Err(ex), statusCode: 500); }
        });

        // POST /api/breaks/{id}/end
        grp.MapPost("/{id:int}/end", async (int id, BreakService svc) =>
        {
            try   { return Results.Ok(await svc.EndBreakAsync(id)); }
            catch (Exception ex) { return Results.Json(Err(ex), statusCode: 500); }
        });

        // POST /api/breaks/{id}/cancel
        grp.MapPost("/{id:int}/cancel", async (int id, BreakService svc) =>
        {
            try   { return Results.Ok(await svc.CancelBreakAsync(id)); }
            catch (Exception ex) { return Results.Json(Err(ex), statusCode: 500); }
        });

        // POST /api/breaks/manual
        grp.MapPost("/manual", async (ManualBreakRequest req, BreakService svc) =>
        {
            try   { return Results.Ok(await svc.CreateManualBreakAsync(req)); }
            catch (Exception ex) { return Results.Json(Err(ex), statusCode: 500); }
        });
    }
}
