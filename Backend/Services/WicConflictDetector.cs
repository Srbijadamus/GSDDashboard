using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public record WicConflictEntryDto(
    int     EntryId,
    string  SupportLocation,
    string  LocationCode,
    string  LocationDisplayName,
    string  AssignmentRole,
    string? OpenTime,
    string? CloseTime);

public record WicConflictDto(
    string                 EmployeeId,
    string?                FullName,
    string                 ShiftDate,
    string                 ConflictType,     // "OVERLAP" | "SPLIT_SHIFT" | "CLOSED_LOCATION"
    WicConflictEntryDto[]  Locations);

public sealed class WicConflictDetector(GSDContext db)
{
    public async Task<List<WicConflictDto>> GetConflictsAsync(
        DateOnly from, DateOnly to, bool includeSplitShifts = false)
    {
        var wicLocations = await db.WicLocations
            .Where(l => l.IsActive)
            .ToListAsync();

        // Same load pattern as ForecastService line 62 — full table, then filter in memory
        var allHours = await db.WicOpeningHours.ToListAsync();

        var entries = await db.WicShiftEntries
            .Where(w => w.IsOnSite && w.SupportLocation != null
                     && w.ShiftDate >= from && w.ShiftDate <= to)
            .ToListAsync();

        var employees = await db.Employees
            .Where(e => e.IsActive)
            .Select(e => new { e.EmployeeId, e.FullName })
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);

        var resolved = entries
            .Select(w => new
            {
                Entry       = w,
                ResolvedLoc = wicLocations.FirstOrDefault(l =>
                    WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, l))
            })
            .Where(x => x.ResolvedLoc != null)
            .ToList();

        var conflictGroups = resolved
            .GroupBy(x => (x.Entry.EmployeeId, x.Entry.ShiftDate))
            .Where(g => g.Select(x => x.ResolvedLoc!.LocationCode).Distinct().Count() > 1)
            .ToList();

        if (conflictGroups.Count == 0)
            return [];

        // Load WicAgentAssignments for AssignmentRole enrichment
        var agentNames = conflictGroups
            .Select(g => { employees.TryGetValue(g.Key.EmployeeId, out var n); return n; })
            .Where(n => n != null).Select(n => n!).Distinct().ToList();

        var conflictCodes = conflictGroups
            .SelectMany(g => g.Select(x => x.ResolvedLoc!.LocationCode))
            .Distinct().ToList();

        var assignments = agentNames.Count > 0
            ? await db.WicAgentAssignments
                .Where(a => a.IsActive
                         && agentNames.Contains(a.EmployeeName)
                         && conflictCodes.Contains(a.LocationCode))
                .ToListAsync()
            : [];

        var roleMap = assignments
            .GroupBy(a => (a.EmployeeName, a.LocationCode))
            .ToDictionary(g => g.Key, g => g.First().AssignmentType);

        var results = new List<WicConflictDto>();

        foreach (var g in conflictGroups)
        {
            employees.TryGetValue(g.Key.EmployeeId, out var name);
            int dow = (int)g.Key.ShiftDate.DayOfWeek; // 0=Sun ... 6=Sat, same as ForecastService

            var classified = g.Select(x =>
            {
                var loc  = x.ResolvedLoc!;
                var role = name != null && roleMap.TryGetValue((name, loc.LocationCode), out var r) ? r : "NONE";
                var h    = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, dow, g.Key.ShiftDate);
                var (aStart, aEnd) = AssignmentWindow(x.Entry, h);
                return new { Entry = x.Entry, Loc = loc, Role = role,
                             AStart = aStart, AEnd = aEnd,
                             IsClosed = h == null || h.IsClosed };
            }).ToList();

            var closedEntries = classified.Where(x => x.IsClosed).ToList();
            var openEntries   = classified.Where(x => !x.IsClosed).ToList();

            // CLOSED_LOCATION: agent has IsOnSite=1 on a day the location is closed
            if (closedEntries.Count > 0)
            {
                results.Add(new WicConflictDto(
                    g.Key.EmployeeId, name,
                    g.Key.ShiftDate.ToString("yyyy-MM-dd"),
                    "CLOSED_LOCATION",
                    closedEntries.Select(x => new WicConflictEntryDto(
                        x.Entry.Id, x.Entry.SupportLocation!,
                        x.Loc.LocationCode, x.Loc.DisplayName, x.Role,
                        null, null)).ToArray()));
            }

            // OVERLAP or SPLIT_SHIFT: only when at least two open entries remain.
            // Decided on the AGENT'S OWN assignment windows, not on the centres' opening
            // hours: two assignments that really clash must be reported even when the two
            // centres' opening hours do not overlap, and two assignments at centres whose
            // hours do overlap are only a real clash when the agent's windows clash too.
            if (openEntries.Count > 1)
            {
                bool anyOverlap = false;
                for (int i = 0; i < openEntries.Count && !anyOverlap; i++)
                    for (int j = i + 1; j < openEntries.Count && !anyOverlap; j++)
                        anyOverlap = WindowsOverlap(
                            openEntries[i].AStart, openEntries[i].AEnd,
                            openEntries[j].AStart, openEntries[j].AEnd);

                string conflictType = anyOverlap ? "OVERLAP" : "SPLIT_SHIFT";
                if (conflictType != "SPLIT_SHIFT" || includeSplitShifts)
                {
                    results.Add(new WicConflictDto(
                        g.Key.EmployeeId, name,
                        g.Key.ShiftDate.ToString("yyyy-MM-dd"),
                        conflictType,
                        openEntries.Select(x => new WicConflictEntryDto(
                            x.Entry.Id, x.Entry.SupportLocation!,
                            x.Loc.LocationCode, x.Loc.DisplayName, x.Role,
                            x.AStart, x.AEnd)).ToArray()));
                }
            }
        }

        return results
            .OrderBy(c => c.ConflictType == "OVERLAP" ? 0 : c.ConflictType == "CLOSED_LOCATION" ? 1 : 2)
            .ThenBy(c => c.ShiftDate)
            .ThenBy(c => c.FullName)
            .ToList();
    }

    /// <summary>
    /// The window the agent is actually committed to AT THIS LOCATION: the row's own
    /// WorkingShift CUT TO the centre's resolved window for the date (the RTM rule,
    /// see <see cref="WicWorkingShift.EffectiveWindow"/>). <c>WicShiftEntries</c> is unique
    /// per employee+date+location, so each row describes one centre. Two assignments are
    /// therefore compared on their trimmed times: a shift that starts before the centre
    /// opens, or ends after it closes, must not look like a clash with a second centre.
    /// Falls back to the centre's opening window when the row carries no usable HH:MM pair.
    /// </summary>
    private static (string? Start, string? End) AssignmentWindow(WicShiftEntry entry, WicOpeningHour? hours)
        => WicWorkingShift.EffectiveWindow(entry.WorkingShift, hours);

    // Two windows overlap when start1 < end2 AND start2 < end1 — the same rule
    // WicShiftService.CreateAssignmentAsync uses to refuse a second same-day assignment.
    // Unknown/unparseable times never count as a clash (unknown is not a conflict).
    private static bool WindowsOverlap(string? s1, string? e1, string? s2, string? e2)
    {
        if (!TimeOnly.TryParse(s1, out var a1) || !TimeOnly.TryParse(e1, out var b1) ||
            !TimeOnly.TryParse(s2, out var a2) || !TimeOnly.TryParse(e2, out var b2)) return false;
        return a1 < b2 && a2 < b1;
    }
}
