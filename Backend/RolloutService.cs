using System.Text.RegularExpressions;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Rollout;

// ─────────────────────────────────────────────────────────────────────────────────────
// ROLLOUT — see documentation/ROLLOUT.md and Backend/RolloutModels.cs.
//
// A rollout day belongs to ONE WIC centre and ONE date. RTM types it by hand and it is
// the authority: it replaces the centre's standard hours for that date and adds the
// selected workers to that centre for those hours. Every automatic writer treats the
// rollout day and everything created from it as manual and leaves it alone.
//
// Design note (why the entry is projected onto two established mechanisms): an
// WicOpeningHours EXCEPTION row (ExceptionDate = the date) is already given precedence
// over the weekday pattern by WicHoursResolver for every surface (forecast, briefing,
// coverage gaps, cards, overview, WIC shifts, roster generator, conflict detector, ...),
// and WicShiftEntries + ShiftEntries are already what WicPresenceResolver and
// WicConflictDetector read. Projecting the one manual entry onto those two mechanisms
// means coverage, cards, forecast, schedule, attendance and conflicts all see the
// rollout without duplicating the resolution rules — while non-rollout days and centres
// behave exactly as before. The two rollout tables stay the source of truth and record
// the ids of the rows they created, so an edit or a delete touches only what came from
// them, and never a row somebody else wrote.
// ─────────────────────────────────────────────────────────────────────────────────────

public record RolloutWorkerDto(
    string  EmployeeId,
    string? EmployeeName,
    bool    FromPipeline,
    string? PipelineNote,
    int?    ShiftEntryId,
    int?    WicShiftEntryId);

public record RolloutWarningDto(
    string  EmployeeId,
    string? EmployeeName,
    string  Date,
    string  Type,              // OVERLAP | UNKNOWN_EMPLOYEE
    string? OtherLocation,
    string? OtherStart,
    string? OtherEnd);

public record RolloutDayDto(
    int     Id,
    string  LocationCode,
    string  LocationName,
    string  Date,
    string  StartTime,
    string  EndTime,
    string? Note,
    string? StandardStart,
    string? StandardEnd,
    bool    StandardClosed,
    bool    IsDifferentFromStandard,
    bool    IsManual,
    string? CreatedBy,
    string? CreatedAt,
    string? UpdatedBy,
    string? UpdatedAt,
    string? ManualChangeNote,
    List<RolloutWorkerDto> Workers,
    List<RolloutWarningDto> Warnings);

public record RolloutWorkerRequest(string EmployeeId, bool FromPipeline, string? PipelineNote);

public record SaveRolloutRequest(
    string  LocationCode,
    string  Date,
    string  StartTime,
    string  EndTime,
    string? Note,
    List<RolloutWorkerRequest>? Workers);

public record RolloutCandidateDto(
    string  EmployeeId,
    string? EmployeeName,
    string? TeamLeadName,
    bool    IsTrainee,
    string? PlannedRole,
    bool    FromPipeline,
    string? PipelineRole);

public record RolloutCandidatesDto(
    bool   PipelineAvailable,
    List<string> PipelineNames,
    List<RolloutCandidateDto> Workers);

public record RolloutSaveResultDto(bool Success, bool Skipped, string? Reason, RolloutDayDto? Day);

public sealed class RolloutService(GSDContext db)
{
    // Rows created in this unit of work only get their database id after SaveChanges;
    // these bridge that gap so the rollout can record what it made.
    private readonly Dictionary<WicRolloutWorker, WicShiftEntry> _pendingWic   = [];
    private readonly Dictionary<WicRolloutWorker, ShiftEntry>    _pendingShift = [];
    private WicOpeningHour? _pendingHours;

    // ── Read ─────────────────────────────────────────────────────────────────────────

    public async Task<List<RolloutDayDto>> GetAsync(DateOnly from, DateOnly to, string? locationCode)
    {
        var query = db.WicRolloutDays
            .Include(d => d.Workers)
            .Where(d => d.RolloutDate >= from && d.RolloutDate <= to);
        if (!string.IsNullOrWhiteSpace(locationCode))
            query = query.Where(d => d.LocationCode == locationCode);

        var days = await query
            .OrderBy(d => d.RolloutDate).ThenBy(d => d.LocationName)
            .ToListAsync();

        var result = new List<RolloutDayDto>(days.Count);
        foreach (var d in days) result.Add(await ToDtoAsync(d));
        return result;
    }

    public async Task<RolloutDayDto?> GetOneAsync(int id)
    {
        var day = await db.WicRolloutDays.Include(d => d.Workers).FirstOrDefaultAsync(d => d.Id == id);
        return day == null ? null : await ToDtoAsync(day);
    }

    /// <summary>
    /// The people RTM can put on this rollout day. "From Pipeline" means the worker is one
    /// of the agents the Pipeline page recorded for this centre and date. Pipeline stores
    /// free-text agent names (PrimaryAgent / BackupAgent), not employee ids, so the match
    /// is by name — names that match no employee are returned separately so the RTM can
    /// see them. Everyone else is offered too, but the UI must mark that as "not in
    /// Pipeline" and require the choice to be deliberate.
    /// </summary>
    public async Task<RolloutCandidatesDto> GetCandidatesAsync(string locationCode, DateOnly date)
    {
        var pipelineNames = await PipelineAgentNamesAsync(locationCode, date);

        var employees = await db.Employees
            .Where(e => e.IsActive && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.FullName, e.TeamLeadName, e.IsTrainee, e.PlannedRole })
            .ToListAsync();

        var workers = employees
            .Select(e =>
            {
                var role = PipelineRoleFor(e.FullName, pipelineNames);
                return new RolloutCandidateDto(
                    e.EmployeeId, e.FullName, e.TeamLeadName, e.IsTrainee, e.PlannedRole,
                    role != null, role);
            })
            .OrderByDescending(w => w.FromPipeline)
            .ThenBy(w => w.EmployeeName)
            .ToList();

        return new RolloutCandidatesDto(pipelineNames.Count > 0, pipelineNames, workers);
    }

    /// <summary>
    /// The centre's STANDARD hours for that date (weekday pattern only — the rollout's own
    /// exception row is deliberately ignored), so the UI can default to them and say when
    /// RTM chose something different.
    /// </summary>
    public async Task<(string? Start, string? End, bool IsClosed)> GetStandardHoursAsync(string locationCode, DateOnly date)
    {
        var loc = await db.WicLocations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationCode == locationCode);
        var hours = await PatternHoursAsync(locationCode, loc?.LocationCodeLegacy, date);
        var (start, end) = WicHoursResolver.DutyWindow(hours);
        var closed = hours == null || hours.IsClosed || start == null || end == null;
        return (start, end, closed);
    }

    /// <summary>Non-blocking overlap preview for the entry form (nothing is written).</summary>
    public async Task<List<RolloutWarningDto>> PreviewAsync(SaveRolloutRequest req)
    {
        var parsed = await ValidateAsync(req);
        if (parsed.Error != null || parsed.Location == null) return [];

        var probe = new WicRolloutDay
        {
            Id           = 0,
            LocationCode = parsed.Location.LocationCode,
            LocationName = parsed.Location.DisplayName,
            RolloutDate  = parsed.Date,
            StartTime    = parsed.Start!.Value.ToString("HH:mm"),
            EndTime      = parsed.End!.Value.ToString("HH:mm"),
        };

        var ids = (parsed.Workers ?? [])
            .Select(w => w.EmployeeId?.Trim() ?? "")
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var names = await db.Employees
            .Where(e => ids.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName, StringComparer.OrdinalIgnoreCase);

        var warnings = new List<RolloutWarningDto>();
        foreach (var id in ids)
        {
            var probeWorker = new WicRolloutWorker { EmployeeId = id, EmployeeName = names.GetValueOrDefault(id) };
            warnings.AddRange(await OverlapWarningsForAsync(probe, probeWorker));
        }
        return warnings;
    }

    // ── Write ────────────────────────────────────────────────────────────────────────

    public async Task<RolloutSaveResultDto> CreateAsync(SaveRolloutRequest req, string actor)
    {
        var parsed = await ValidateAsync(req);
        if (parsed.Error != null) return new RolloutSaveResultDto(false, true, parsed.Error, null);

        var clash = await db.WicRolloutDays.FirstOrDefaultAsync(d =>
            d.LocationCode == parsed.Location!.LocationCode && d.RolloutDate == parsed.Date);
        if (clash != null)
            return new RolloutSaveResultDto(false, true,
                $"A rollout day already exists for {parsed.Location!.DisplayName} on {parsed.Date:yyyy-MM-dd}.", null);

        var day = new WicRolloutDay
        {
            LocationCode = parsed.Location!.LocationCode,
            LocationName = parsed.Location.DisplayName,
            RolloutDate  = parsed.Date,
            StartTime    = parsed.Start!.Value.ToString("HH:mm"),
            EndTime      = parsed.End!.Value.ToString("HH:mm"),
            Note         = Normalize(req.Note),
            IsManual     = true,
            CreatedAt    = DateTime.UtcNow,
        };
        StampManual(day, actor);

        db.WicRolloutDays.Add(day);
        await db.SaveChangesAsync();

        var warnings = await ApplyAsync(day, parsed.Location, parsed.Workers!, actor);
        return new RolloutSaveResultDto(true, false, null, await ToDtoAsync(day, warnings));
    }

    public async Task<RolloutSaveResultDto> UpdateAsync(int id, SaveRolloutRequest req, string actor)
    {
        var day = await db.WicRolloutDays.Include(d => d.Workers).FirstOrDefaultAsync(d => d.Id == id);
        if (day == null) return new RolloutSaveResultDto(false, true, "Rollout day not found.", null);

        var parsed = await ValidateAsync(req);
        if (parsed.Error != null) return new RolloutSaveResultDto(false, true, parsed.Error, null);

        var clash = await db.WicRolloutDays.FirstOrDefaultAsync(d =>
            d.Id != id && d.LocationCode == parsed.Location!.LocationCode && d.RolloutDate == parsed.Date);
        if (clash != null)
            return new RolloutSaveResultDto(false, true,
                $"Another rollout day already exists for {parsed.Location!.DisplayName} on {parsed.Date:yyyy-MM-dd}.", null);

        // Moving a rollout day to another centre: the rows it created belong to the old
        // centre, so they are removed and rebuilt for the new one. Only rollout-owned rows.
        if (!string.Equals(day.LocationCode, parsed.Location!.LocationCode, StringComparison.Ordinal))
        {
            await RemoveProjectionAsync(day);
            await DropWorkerProjectionsAsync(day);
        }

        day.LocationCode = parsed.Location!.LocationCode;
        day.LocationName = parsed.Location.DisplayName;
        day.RolloutDate  = parsed.Date;
        day.StartTime    = parsed.Start!.Value.ToString("HH:mm");
        day.EndTime      = parsed.End!.Value.ToString("HH:mm");
        day.Note         = Normalize(req.Note);
        StampManual(day, actor);
        await db.SaveChangesAsync();

        var warnings = await ApplyAsync(day, parsed.Location, parsed.Workers!, actor);
        return new RolloutSaveResultDto(true, false, null, await ToDtoAsync(day, warnings));
    }

    /// <summary>
    /// Deletes the rollout day and exactly the rows it created: its WicOpeningHours
    /// exception projection, its WicShiftEntries rows and its own ShiftEntries rows.
    /// Rows the workers already had for that day are never touched.
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var day = await db.WicRolloutDays.Include(d => d.Workers).FirstOrDefaultAsync(d => d.Id == id);
        if (day == null) return false;

        await RemoveProjectionAsync(day);
        await DropWorkerProjectionsAsync(day);

        db.WicRolloutWorkers.RemoveRange(day.Workers);
        db.WicRolloutDays.Remove(day);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Projects hours + workers, saves, then records the ids of the created rows.</summary>
    private async Task<List<RolloutWarningDto>> ApplyAsync(
        WicRolloutDay day, WicLocation loc, List<RolloutWorkerRequest> requested, string actor)
    {
        await ProjectHoursAsync(day, loc, actor);
        var warnings = await SyncWorkersAsync(day, requested, actor);

        await db.SaveChangesAsync();
        FixupCreatedIds(day);
        await db.SaveChangesAsync();
        return warnings;
    }

    // ── Projection: rollout hours → WicOpeningHours exception row ────────────────────

    private async Task ProjectHoursAsync(WicRolloutDay day, WicLocation loc, string actor)
    {
        WicOpeningHour? row = null;
        if (day.HoursRowId is > 0)
        {
            row = await db.WicOpeningHours.FirstOrDefaultAsync(h => h.Id == day.HoursRowId);
            // Only ever rewrite a row this rollout created. A pre-existing exception day
            // for the same centre and date is left untouched; it simply loses precedence
            // while the rollout exists (WicHoursResolver takes the most recently changed
            // exception row) and is visible again after the rollout is deleted.
            if (row != null && !IsRolloutHoursRow(row)) row = null;
        }

        var minRequired = (await PatternHoursAsync(day.LocationCode, loc.LocationCodeLegacy, day.RolloutDate))?.MinRequired;

        if (row == null)
        {
            row = new WicOpeningHour { LocationCode = day.LocationCode };
            db.WicOpeningHours.Add(row);
            _pendingHours = row;
        }

        row.LocationCode   = day.LocationCode;
        row.DayOfWeek      = (int)day.RolloutDate.DayOfWeek;  // kept in step: several readers pre-filter by weekday
        row.OpenTime       = day.StartTime;
        row.CloseTime      = day.EndTime;
        row.OpenTime2      = null;
        row.CloseTime2     = null;
        row.IsClosed       = false;
        row.RawSchedule    = $"Rollout {day.StartTime} - {day.EndTime}";
        row.EffectiveFrom  = null;                            // an exception row is never a pattern
        row.ExceptionDate  = day.RolloutDate;
        row.ChangeNote     = RolloutSource.Module;
        row.MinRequired    = minRequired;
        row.ManualChangeBy = actor;
        row.ManualChangeAt = DateTime.UtcNow;
    }

    private static bool IsRolloutHoursRow(WicOpeningHour row) =>
        string.Equals(row.ChangeNote, RolloutSource.Module, StringComparison.Ordinal);

    private async Task RemoveProjectionAsync(WicRolloutDay day)
    {
        if (day.HoursRowId is not > 0) { day.HoursRowId = null; return; }
        var row = await db.WicOpeningHours.FirstOrDefaultAsync(h => h.Id == day.HoursRowId);
        if (row != null && IsRolloutHoursRow(row) && row.ExceptionDate == day.RolloutDate)
            db.WicOpeningHours.Remove(row);
        day.HoursRowId = null;
    }

    // ── Projection: rollout workers → assignment rows ────────────────────────────────

    /// <summary>
    /// Brings the assignment rows in line with RTM's worker list.
    ///  * a row the rollout created is kept in step with the centre and hours on the day;
    ///  * a row somebody else already wrote for that worker, centre and date is left
    ///    exactly as it is (the rollout never rewrites a pre-existing row);
    ///  * the day-level ShiftEntries row (one per employee and date — UQ_ShiftEntries_EmpDate)
    ///    is created only when the worker has none. It is the manual, rollout-marked row
    ///    that makes the worker count as WIC cover at the centre.
    /// </summary>
    private async Task<List<RolloutWarningDto>> SyncWorkersAsync(
        WicRolloutDay day, List<RolloutWorkerRequest> requested, string actor)
    {
        var wanted = requested
            .Where(w => !string.IsNullOrWhiteSpace(w.EmployeeId))
            .GroupBy(w => w.EmployeeId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var wantedIds = wanted.Select(w => w.EmployeeId.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Workers RTM removed: drop only the rows the rollout created.
        foreach (var gone in day.Workers.Where(w => !wantedIds.Contains(w.EmployeeId)).ToList())
        {
            await DropWorkerProjectionAsync(gone, day.RolloutDate);
            day.Workers.Remove(gone);
            db.WicRolloutWorkers.Remove(gone);
        }

        var employees = await db.Employees
            .Where(e => wantedIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase);

        var warnings = new List<RolloutWarningDto>();

        foreach (var w in wanted)
        {
            var empId = w.EmployeeId.Trim();
            if (!employees.TryGetValue(empId, out var emp))
            {
                warnings.Add(new RolloutWarningDto(empId, null, day.RolloutDate.ToString("yyyy-MM-dd"),
                    "UNKNOWN_EMPLOYEE", null, null, null));
                continue;
            }

            var worker = day.Workers.FirstOrDefault(x =>
                string.Equals(x.EmployeeId, empId, StringComparison.OrdinalIgnoreCase));
            if (worker == null)
            {
                worker = new WicRolloutWorker { EmployeeId = empId };
                day.Workers.Add(worker);
            }
            worker.EmployeeName = emp.FullName ?? empId;
            worker.FromPipeline = w.FromPipeline;
            worker.PipelineNote = Normalize(w.PipelineNote);

            await SyncWicShiftRowAsync(day, worker);
            await SyncDayShiftRowAsync(day, worker, actor);

            warnings.AddRange(await OverlapWarningsForAsync(day, worker));
        }

        return warnings;
    }

    private async Task SyncWicShiftRowAsync(WicRolloutDay day, WicRolloutWorker worker)
    {
        WicShiftEntry? row = null;
        if (worker.WicShiftEntryId is > 0)
        {
            row = await db.WicShiftEntries.FirstOrDefaultAsync(x => x.Id == worker.WicShiftEntryId);
            // Only keep a row that is still the rollout's own assignment for this worker.
            if (row != null && (row.EmployeeId != worker.EmployeeId || row.ShiftDate != day.RolloutDate ||
                                !row.IsOnSite || !WorkerProjectionMatchesLocation(row, day)))
                row = null;
            if (row == null) worker.WicShiftEntryId = null;
        }

        if (row == null)
        {
            // Keep an existing assignment somebody else made for this worker at this
            // centre — never overwrite it, but do not create a duplicate either
            // (UQ_WicShift_EmpDateLoc caps one row per employee, date and centre).
            var existing = await db.WicShiftEntries.FirstOrDefaultAsync(x =>
                x.EmployeeId == worker.EmployeeId && x.ShiftDate == day.RolloutDate &&
                x.SupportLocation == day.LocationName);
            if (existing != null) return;

            row = new WicShiftEntry { EmployeeId = worker.EmployeeId, ShiftDate = day.RolloutDate };
            db.WicShiftEntries.Add(row);
            _pendingWic[worker] = row;
        }

        row.DayOfWeek       = day.RolloutDate.DayOfWeek.ToString();
        row.SupportLocation = day.LocationName;
        row.LocationCode    = day.LocationCode;
        row.WicOpeningHours = $"{day.StartTime}-{day.EndTime}";
        row.WorkingShift    = $"{day.StartTime} - {day.EndTime}";
        row.IsOnSite        = true;
        row.IsGSDDay        = false;
        row.IsOffDay        = false;
        row.Task            = "WIC";
    }

    private async Task SyncDayShiftRowAsync(WicRolloutDay day, WicRolloutWorker worker, string actor)
    {
        ShiftEntry? row = null;
        if (worker.ShiftEntryId is > 0)
        {
            row = await db.ShiftEntries.FirstOrDefaultAsync(x => x.Id == worker.ShiftEntryId);
            if (row != null && (row.EmployeeId != worker.EmployeeId || row.ShiftDate != day.RolloutDate ||
                                !string.Equals(row.SourceModule, RolloutSource.Module, StringComparison.Ordinal)))
                row = null;
            if (row == null) worker.ShiftEntryId = null;
        }

        if (row == null)
        {
            var existing = await db.ShiftEntries.FirstOrDefaultAsync(x =>
                x.EmployeeId == worker.EmployeeId && x.ShiftDate == day.RolloutDate);
            if (existing != null) return;   // one row per employee and date; somebody already owns it

            row = new ShiftEntry
            {
                EmployeeId   = worker.EmployeeId,
                ShiftDate    = day.RolloutDate,
                SourceSheet  = RolloutSource.Module,
                SourceModule = RolloutSource.Module,
                SourceId     = day.Id,
            };
            db.ShiftEntries.Add(row);
            _pendingShift[worker] = row;
        }

        // Stamps ManualChangeBy/At, AutoGenerated=false and registers the edit so
        // GuardManualShifts lets this same unit of work keep the manual marker up to date.
        db.RecordManualShiftEdit(row, actor);

        row.ShiftType    = ShiftTypes.WicDuty;
        row.IsWicDuty    = true;
        row.AgentTask    = Truncate(day.LocationName ?? day.LocationCode, 20);
        row.LocationId   = day.LocationCode;
        row.ShiftStart   = day.StartTime;
        row.ShiftEnd     = day.EndTime;
        row.AutoGenerated = false;
        row.ManualChangeNote = RolloutSource.EntryNote;
    }

    private static bool WorkerProjectionMatchesLocation(WicShiftEntry row, WicRolloutDay day) =>
        string.Equals(row.SupportLocation, day.LocationName, StringComparison.OrdinalIgnoreCase) &&
        (row.LocationCode == null || row.LocationCode == day.LocationCode);

    private async Task DropWorkerProjectionsAsync(WicRolloutDay day)
    {
        foreach (var worker in day.Workers) await DropWorkerProjectionAsync(worker, day.RolloutDate);
        await db.SaveChangesAsync();
    }

    private async Task DropWorkerProjectionAsync(WicRolloutWorker worker, DateOnly date)
    {
        if (worker.WicShiftEntryId is > 0)
        {
            var row = await db.WicShiftEntries.FirstOrDefaultAsync(x => x.Id == worker.WicShiftEntryId);
            if (row != null && row.EmployeeId == worker.EmployeeId && row.ShiftDate == date && row.IsOnSite)
                db.WicShiftEntries.Remove(row);
            worker.WicShiftEntryId = null;
        }

        if (worker.ShiftEntryId is > 0)
        {
            var row = await db.ShiftEntries.FirstOrDefaultAsync(x => x.Id == worker.ShiftEntryId);
            if (row != null && row.EmployeeId == worker.EmployeeId && row.ShiftDate == date &&
                string.Equals(row.SourceModule, RolloutSource.Module, StringComparison.Ordinal))
            {
                db.AllowRolloutOwnedDelete(row);
                db.ShiftEntries.Remove(row);
            }
            worker.ShiftEntryId = null;
        }
    }

    private void FixupCreatedIds(WicRolloutDay day)
    {
        if (_pendingHours != null) { day.HoursRowId = _pendingHours.Id; _pendingHours = null; }
        foreach (var worker in day.Workers)
        {
            if (worker.WicShiftEntryId == null && _pendingWic.TryGetValue(worker, out var wic))
                worker.WicShiftEntryId = wic.Id;
            if (worker.ShiftEntryId == null && _pendingShift.TryGetValue(worker, out var shift))
                worker.ShiftEntryId = shift.Id;
        }
        _pendingWic.Clear();
        _pendingShift.Clear();
    }

    // ── Warnings (never a block) ─────────────────────────────────────────────────────

    /// <summary>
    /// Same-day, overlapping commitment somewhere else. Overlap is decided on the two
    /// assignments' own windows with the same rule WicConflictDetector uses
    /// (start1 &lt; end2 AND start2 &lt; end1); unknown times never count as a clash.
    /// </summary>
    private async Task<List<RolloutWarningDto>> OverlapWarningsForAsync(
        WicRolloutDay day, WicRolloutWorker worker)
    {
        var result = new List<RolloutWarningDto>();
        if (!TimeOnly.TryParse(day.StartTime, out var myStart) ||
            !TimeOnly.TryParse(day.EndTime, out var myEnd)) return result;

        // One warning per distinct other commitment: the on-site row and the day-level
        // ShiftEntries row describe the same clash, and reporting both would be noise.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? otherLocation, string? start, string? end)
        {
            if (seen.Add($"{otherLocation}|{start}|{end}"))
                result.Add(new RolloutWarningDto(worker.EmployeeId, worker.EmployeeName,
                    day.RolloutDate.ToString("yyyy-MM-dd"), "OVERLAP", otherLocation, start, end));
        }

        var otherRows = await db.WicShiftEntries
            .Where(w => w.EmployeeId == worker.EmployeeId && w.ShiftDate == day.RolloutDate && w.IsOnSite)
            .ToListAsync();

        var locations = await db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var allHours  = await db.WicOpeningHours.ToListAsync();

        foreach (var row in otherRows)
        {
            if (string.Equals(row.SupportLocation, day.LocationName, StringComparison.OrdinalIgnoreCase)) continue;
            var loc = locations.FirstOrDefault(l => WicLocationMatcher.MatchesSupportLocation(row.SupportLocation, l));
            var (start, end) = AssignmentWindow(row, loc, allHours, day.RolloutDate);
            if (start == null || end == null) continue;
            if (!TimeOnly.TryParse(start, out var s) || !TimeOnly.TryParse(end, out var e)) continue;
            if (!(myStart < e && s < myEnd)) continue;
            Add(row.SupportLocation, start, end);
        }

        // The day-level row describes the worker's commitment for the whole day, so it is
        // only consulted when no overlapping on-site row was found: otherwise it merely
        // repeats the same clash (its AgentTask holds a truncated centre name) or describes
        // the rollout's own row. A row this feature created for THIS centre is never a
        // competing commitment — an edit or a preview must not report the rollout against
        // itself. This is how a competing non-WIC shift is caught, since it has no on-site
        // row at all.
        if (result.Count == 0)
        {
            var dayRow = await db.ShiftEntries.FirstOrDefaultAsync(x =>
                x.EmployeeId == worker.EmployeeId && x.ShiftDate == day.RolloutDate);
            if (dayRow != null && !IsOwnRolloutDayRow(dayRow, day) &&
                TimeOnly.TryParse(dayRow.ShiftStart, out var ds) && TimeOnly.TryParse(dayRow.ShiftEnd, out var de) &&
                myStart < de && ds < myEnd)
            {
                var where = string.IsNullOrWhiteSpace(dayRow.AgentTask) ? dayRow.ShiftType : dayRow.AgentTask;
                Add(where, dayRow.ShiftStart, dayRow.ShiftEnd);
            }
        }

        return result;
    }

    /// <summary>
    /// True when a day-level ShiftEntries row is one this feature created for this rollout
    /// day's centre — matched by its rollout day id, its centre code, or its centre name
    /// (AgentTask holds the centre name, truncated to the column width, as written here).
    /// </summary>
    private static bool IsOwnRolloutDayRow(ShiftEntry dayRow, WicRolloutDay day)
    {
        if (!string.Equals(dayRow.SourceModule, RolloutSource.Module, StringComparison.Ordinal)) return false;
        if (day.Id > 0 && dayRow.SourceId == day.Id) return true;
        if (!string.IsNullOrWhiteSpace(day.LocationCode) &&
            string.Equals(dayRow.LocationId, day.LocationCode, StringComparison.Ordinal)) return true;
        var centre = day.LocationName ?? day.LocationCode ?? "";
        return !string.IsNullOrWhiteSpace(dayRow.AgentTask) &&
               centre.StartsWith(dayRow.AgentTask, StringComparison.OrdinalIgnoreCase);
    }

    private static (string? Start, string? End) AssignmentWindow(
        WicShiftEntry row, WicLocation? loc, List<WicOpeningHour> allHours, DateOnly date)
    {
        var times = Regex.Matches(row.WorkingShift ?? "", @"\d{1,2}:\d{2}");
        if (times.Count >= 2) return (times[0].Value, times[^1].Value);
        if (loc == null) return (null, null);
        var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy,
            (int)date.DayOfWeek, date);
        return WicHoursResolver.DutyWindow(hours);
    }

    // ── Read helpers ────────────────────────────────────────────────────────────────

    private async Task<RolloutDayDto> ToDtoAsync(WicRolloutDay day, List<RolloutWarningDto>? warnings = null)
    {
        var (stdStart, stdEnd, stdClosed) = await GetStandardHoursAsync(day.LocationCode, day.RolloutDate);
        warnings ??= await AllWarningsAsync(day);

        var workers = day.Workers
            .OrderBy(w => w.EmployeeName ?? w.EmployeeId)
            .Select(w => new RolloutWorkerDto(w.EmployeeId, w.EmployeeName, w.FromPipeline,
                w.PipelineNote, w.ShiftEntryId, w.WicShiftEntryId))
            .ToList();

        return new RolloutDayDto(
            day.Id, day.LocationCode, day.LocationName ?? day.LocationCode,
            day.RolloutDate.ToString("yyyy-MM-dd"), day.StartTime, day.EndTime, day.Note,
            stdStart, stdEnd, stdClosed,
            stdClosed || !string.Equals(stdStart, day.StartTime, StringComparison.Ordinal) ||
                         !string.Equals(stdEnd, day.EndTime, StringComparison.Ordinal),
            day.IsManual,
            day.ManualChangeBy, day.ManualChangeAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            day.ManualChangeBy, day.ManualChangeAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            day.ManualChangeNote,
            workers, warnings);
    }

    private async Task<List<RolloutWarningDto>> AllWarningsAsync(WicRolloutDay day)
    {
        var result = new List<RolloutWarningDto>();
        foreach (var w in day.Workers)
            result.AddRange(await OverlapWarningsForAsync(day, w));
        return result;
    }

    private async Task<List<string>> PipelineAgentNamesAsync(string locationCode, DateOnly date)
    {
        var loc = await db.WicLocations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationCode == locationCode);
        var codes = new List<string> { locationCode };
        if (!string.IsNullOrWhiteSpace(loc?.LocationCodeLegacy)) codes.Add(loc!.LocationCodeLegacy!);

        var rows = await db.WicPipeline.AsNoTracking()
            .Where(p => p.LocationCode != null && codes.Contains(p.LocationCode) &&
                        p.PipelineDate <= date && (p.PipelineDateEnd ?? p.PipelineDate) >= date)
            .ToListAsync();

        return rows
            .SelectMany(p => new[] { p.PrimaryAgent, p.BackupAgent })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
    }

    private static string? PipelineRoleFor(string? fullName, List<string> pipelineNames)
    {
        if (string.IsNullOrWhiteSpace(fullName) || pipelineNames.Count == 0) return null;
        return pipelineNames.FirstOrDefault(n =>
            string.Equals(n.Trim(), fullName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<WicOpeningHour?> PatternHoursAsync(string locationCode, string? legacy, DateOnly date)
    {
        var dow = (int)date.DayOfWeek;
        var rows = await db.WicOpeningHours.AsNoTracking()
            .Where(h => h.ExceptionDate == null && h.DayOfWeek == dow &&
                        (h.LocationCode == locationCode || (legacy != null && h.LocationCode == legacy)) &&
                        (h.EffectiveFrom == null || h.EffectiveFrom <= date))
            .ToListAsync();
        return rows.OrderByDescending(h => h.EffectiveFrom ?? DateOnly.MinValue).FirstOrDefault();
    }

    // ── Validation / small helpers ──────────────────────────────────────────────────

    private sealed record ParsedRequest(
        WicLocation? Location, DateOnly Date, TimeOnly? Start, TimeOnly? End,
        List<RolloutWorkerRequest> Workers, string? Error);

    private async Task<ParsedRequest> ValidateAsync(SaveRolloutRequest req)
    {
        var empty = new List<RolloutWorkerRequest>();
        if (string.IsNullOrWhiteSpace(req.LocationCode))
            return new ParsedRequest(null, default, null, null, empty, "A WIC centre is required.");
        if (!DateOnly.TryParse(req.Date, out var date))
            return new ParsedRequest(null, default, null, null, empty, "Invalid date. Expected yyyy-MM-dd.");

        var loc = await db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == req.LocationCode && l.IsActive);
        if (loc == null)
            return new ParsedRequest(null, date, null, null, empty, $"WIC centre '{req.LocationCode}' not found.");

        if (!TimeOnly.TryParse(req.StartTime, out var start))
            return new ParsedRequest(loc, date, null, null, empty, "Invalid start time. Expected HH:MM.");
        if (!TimeOnly.TryParse(req.EndTime, out var end))
            return new ParsedRequest(loc, date, null, null, empty, "Invalid end time. Expected HH:MM.");
        if (end <= start)
            return new ParsedRequest(loc, date, start, end, empty, "The end time must be after the start time.");

        var workers = req.Workers ?? [];
        var ids = workers
            .Select(w => w.EmployeeId?.Trim() ?? "")
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count > 0)
        {
            var known = await db.Employees
                .Where(e => ids.Contains(e.EmployeeId))
                .Select(e => e.EmployeeId)
                .ToListAsync();
            var unknown = ids.Where(id => !known.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
                return new ParsedRequest(loc, date, start, end, workers,
                    $"Unknown employee id(s): {string.Join(", ", unknown)}.");
        }

        return new ParsedRequest(loc, date, start, end, workers, null);
    }

    private static void StampManual(WicRolloutDay day, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 100)
            throw new ArgumentException("A session actor is required.", nameof(actor));
        day.IsManual         = true;
        day.ManualChangeBy   = actor;
        day.ManualChangeAt   = DateTime.UtcNow;
        day.ManualChangeNote = RolloutSource.EntryNote;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public static class RolloutEndpointMapper
{
    public static void MapRolloutEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/rollout").WithTags("Rollout");

        // Same actor contract as the other hand-entered rows (see WicShiftService): the
        // signed-in user's KID, or the fixed "UI-MANUAL" tag while Auth:EnforceAuthorization
        // is still off. Never null — an RTM entry is a deliberate human decision.
        static string ResolveManualActor(HttpContext ctx) =>
            ctx.User.FindFirst(AuthClaims.Kid)?.Value is { Length: > 0 and <= 100 } kid ? kid : "UI-MANUAL";

        grp.MapGet("/", async (string? from, string? to, string? locationCode, RolloutService svc) =>
        {
            var today    = DateOnly.FromDateTime(DateTime.Today);
            var fromDate = DateOnly.TryParse(from, out var f) ? f : today;
            var toDate   = DateOnly.TryParse(to,   out var t) ? t : fromDate.AddDays(60);
            return Results.Ok(await svc.GetAsync(fromDate, toDate, locationCode));
        });

        grp.MapGet("/candidates", async (string locationCode, string date, RolloutService svc) =>
        {
            if (!DateOnly.TryParse(date, out var d))
                return Results.BadRequest(new { error = "Invalid date. Expected yyyy-MM-dd." });
            return Results.Ok(await svc.GetCandidatesAsync(locationCode, d));
        });

        grp.MapGet("/standard-hours", async (string locationCode, string date, RolloutService svc) =>
        {
            if (!DateOnly.TryParse(date, out var d))
                return Results.BadRequest(new { error = "Invalid date. Expected yyyy-MM-dd." });
            var (start, end, closed) = await svc.GetStandardHoursAsync(locationCode, d);
            return Results.Ok(new { locationCode, date = d.ToString("yyyy-MM-dd"), startTime = start, endTime = end, isClosed = closed });
        });

        grp.MapGet("/{id:int}", async (int id, RolloutService svc) =>
        {
            var day = await svc.GetOneAsync(id);
            return day == null ? Results.NotFound(new { error = "Rollout day not found." }) : Results.Ok(day);
        });

        // Non-blocking overlap preview for the entry form. Writes nothing.
        grp.MapPost("/preview", async (SaveRolloutRequest req, RolloutService svc) =>
            Results.Ok(await svc.PreviewAsync(req)));

        grp.MapPost("/", async (SaveRolloutRequest req, RolloutService svc, HttpContext ctx) =>
        {
            var result = await svc.CreateAsync(req, ResolveManualActor(ctx));
            if (!result.Success) return Results.BadRequest(new { error = result.Reason });
            return Results.Created($"/api/rollout/{result.Day!.Id}", result.Day);
        });

        grp.MapPut("/{id:int}", async (int id, SaveRolloutRequest req, RolloutService svc, HttpContext ctx) =>
        {
            var result = await svc.UpdateAsync(id, req, ResolveManualActor(ctx));
            if (!result.Success) return Results.BadRequest(new { error = result.Reason });
            return Results.Ok(result.Day);
        });

        grp.MapDelete("/{id:int}", async (int id, RolloutService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.Ok(new { deleted = true }) : Results.NotFound(new { error = "Rollout day not found." });
        });
    }
}
