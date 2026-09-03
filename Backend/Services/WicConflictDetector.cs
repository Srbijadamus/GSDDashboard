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
                return new { Entry = x.Entry, Loc = loc, Role = role, Hours = h, IsClosed = h == null || h.IsClosed };
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

            // OVERLAP or SPLIT_SHIFT: only when at least two open entries remain
            if (openEntries.Count > 1)
            {
                bool anyOverlap = false;
                for (int i = 0; i < openEntries.Count && !anyOverlap; i++)
                    for (int j = i + 1; j < openEntries.Count && !anyOverlap; j++)
                        anyOverlap = HoursOverlap(openEntries[i].Hours, openEntries[j].Hours);

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
                            x.Hours?.OpenTime, x.Hours?.CloseTime)).ToArray()));
                }
            }
        }

        return results
            .OrderBy(c => c.ConflictType == "OVERLAP" ? 0 : c.ConflictType == "CLOSED_LOCATION" ? 1 : 2)
            .ThenBy(c => c.ShiftDate)
            .ThenBy(c => c.FullName)
            .ToList();
    }

    // Two windows overlap when s1 < e2 AND s2 < e1 (HH:mm lexicographic order is correct for 00:00–23:59)
    private static bool HoursOverlap(WicOpeningHour? a, WicOpeningHour? b)
    {
        if (a is null || b is null || a.IsClosed || b.IsClosed) return false;
        if (a.OpenTime is null || a.CloseTime is null || b.OpenTime is null || b.CloseTime is null) return false;
        return string.Compare(a.OpenTime, b.CloseTime, StringComparison.Ordinal) < 0
            && string.Compare(b.OpenTime, a.CloseTime, StringComparison.Ordinal) < 0;
    }
}
