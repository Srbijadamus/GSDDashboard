using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public enum AbsenceStatus
{
    WORKING,
    HALF_AL,
    SL,
    AL,
    OFF,
    OFF_WEEKEND,
    PH,
    TRAINING,
    RESIGNED,
    UNKNOWN
}

public class AvailabilityResolver
{
    private readonly GSDContext _db;

    // Canonical full-absence set (sourced from BackupService, OverviewService, BriefingService,
    // ForecastService, WicShiftService -- all five define this identical set).
    // SubstitutionService intentionally adds TRAINING for its own scheduling logic; that
    // local addition stays in SubstitutionService and is not reflected here.
    public static readonly HashSet<string> FullAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "SL", "AL", "UL", "OL", "PH", "LPH", "RESIGNED" };

    // Reusable as a List<string> for EF Core IN-clause translation.
    private static readonly List<string> FullAbsenceTypesList = FullAbsenceTypes.ToList();

    public AvailabilityResolver(GSDContext db) => _db = db;

    // Returns the absence status for a single employee on a given date.
    // SickLeave records take priority over ShiftEntries.
    public async Task<AbsenceStatus> GetStatusAsync(string employeeId, DateOnly date)
    {
        var hasSickLeave = await _db.SickLeaves.AnyAsync(s =>
            s.EmployeeId == employeeId && s.FirstDay <= date && s.LastDay >= date);
        if (hasSickLeave) return AbsenceStatus.SL;

        var entry = await _db.ShiftEntries.FirstOrDefaultAsync(s =>
            s.EmployeeId == employeeId && s.ShiftDate == date);
        if (entry == null) return AbsenceStatus.UNKNOWN;

        return (entry.ShiftType ?? "").ToUpperInvariant() switch
        {
            "SL"          => AbsenceStatus.SL,
            "AL"          => AbsenceStatus.AL,
            "UL"          => AbsenceStatus.AL,
            "OL"          => AbsenceStatus.AL,
            "HALF_AL"     => AbsenceStatus.HALF_AL,
            "OFF"         => AbsenceStatus.OFF,
            "OFF_WEEKEND" => AbsenceStatus.OFF_WEEKEND,
            "PH"          => AbsenceStatus.PH,
            "LPH"         => AbsenceStatus.PH,
            "TRAINING"    => AbsenceStatus.TRAINING,
            "RESIGNED"    => AbsenceStatus.RESIGNED,
            _             => AbsenceStatus.WORKING
        };
    }

    // Returns the subset of the supplied employee IDs that are absent on the given date.
    // An employee is absent if they have an active SickLeave record OR a ShiftEntry
    // whose ShiftType is in FullAbsenceTypes.
    public async Task<HashSet<string>> GetAbsentIdsAsync(IEnumerable<string> employeeIds, DateOnly date)
    {
        var ids = employeeIds.ToList();
        if (ids.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sickIds = await _db.SickLeaves
            .Where(s => s.EmployeeId != null
                     && ids.Contains(s.EmployeeId)
                     && s.FirstDay <= date
                     && s.LastDay >= date)
            .Select(s => s.EmployeeId!)
            .ToListAsync();

        var shiftAbsentIds = await _db.ShiftEntries
            .Where(s => s.EmployeeId != null
                     && ids.Contains(s.EmployeeId)
                     && s.ShiftDate == date
                     && FullAbsenceTypesList.Contains(s.ShiftType))
            .Select(s => s.EmployeeId!)
            .ToListAsync();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in sickIds)        result.Add(id);
        foreach (var id in shiftAbsentIds) result.Add(id);
        return result;
    }
}
