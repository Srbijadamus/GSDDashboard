using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Backup;

public record BackupCandidateDto(
    string EmployeeId,
    string FullName,
    string? TeamLead,
    string Source,              // "BACKUP" | "BACKLOG" | "WIC:<code>"
    double? DistanceKm,
    string? DonorLocationCode,
    string? DonorDisplayName,
    int? DonorStaffingAfter,
    int? DonorMinRequired,
    double Score,
    string Justification
);

public record DayBackupDto(
    string Date,
    string DayOfWeek,
    string LocationCode,
    string DisplayName,
    bool IsOpen,
    string? ClosedReason,
    int MinRequired,
    bool MinRequiredIsDefault,
    int ScheduledCount,
    int EffectiveCoverage,
    bool IsAtRisk,
    List<string> AbsentAgentIds,
    List<string> AbsentAgentNames,
    List<BackupCandidateDto> Candidates
);

public record BackupResponseDto(
    string LocationCode,
    string DisplayName,
    List<DayBackupDto> Days,
    string? Warning
);

public class BackupService
{
    private readonly GSDContext _db;
    private const int DefaultMinAgents = 1;

    public BackupService(GSDContext db) => _db = db;

    // Full-absence types only — HALF_AL handled separately as 0.5 coverage
    private static readonly HashSet<string> FullAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "SL", "AL", "UL", "PH", "LPH", "RESIGNED" };

    private static readonly HashSet<string> OccupiedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "WIC_DUTY", "TRAINING" };

    private static double? Haversine(string? coordA, string? coordB)
    {
        if (string.IsNullOrEmpty(coordA) || string.IsNullOrEmpty(coordB)) return null;
        var a = coordA.Split(','); var b = coordB.Split(',');
        if (a.Length < 2 || b.Length < 2) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(a[0].Trim(), System.Globalization.NumberStyles.Float, ci, out var lat1)) return null;
        if (!double.TryParse(a[1].Trim(), System.Globalization.NumberStyles.Float, ci, out var lon1)) return null;
        if (!double.TryParse(b[0].Trim(), System.Globalization.NumberStyles.Float, ci, out var lat2)) return null;
        if (!double.TryParse(b[1].Trim(), System.Globalization.NumberStyles.Float, ci, out var lon2)) return null;
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var sinLat = Math.Sin(dLat / 2); var sinLon = Math.Sin(dLon / 2);
        var aVal = sinLat * sinLat +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) * sinLon * sinLon;
        return R * 2 * Math.Atan2(Math.Sqrt(aVal), Math.Sqrt(1 - aVal));
    }

    public async Task<BackupResponseDto?> GetBackupAsync(string locationCode, string? dateStr, int horizon = 3)
    {
        horizon = Math.Clamp(horizon, 1, 7);
        var startDate = dateStr != null && DateOnly.TryParse(dateStr, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
        var endDate = startDate.AddDays(horizon - 1);

        var location = await _db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode && l.IsActive);
        if (location == null) return null;

        var allLocations    = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var allEmployees    = await _db.Employees.Where(e => e.IsActive).ToListAsync();
        var allAssignments  = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var openingHours    = await _db.WicOpeningHours.ToListAsync();
        var publicHolidays  = await _db.PublicHolidays.ToListAsync();

        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => g.First());

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= startDate && w.ShiftDate <= endDate)
            .ToListAsync();

        var empById = allEmployees.ToDictionary(e => e.EmployeeId);

        int minRequired  = location.MinAgentsRequired ?? DefaultMinAgents;
        bool minIsDefault = location.MinAgentsRequired == null;
        bool hasCoords    = !string.IsNullOrEmpty(location.Coordinates);
        bool anyCoords    = allLocations.Any(l => !string.IsNullOrEmpty(l.Coordinates));

        var warningParts = new List<string>();
        if (minIsDefault)
            warningParts.Add($"MinAgentsRequired not set for {location.DisplayName} — using default {DefaultMinAgents}.");
        if (!anyCoords)
            warningParts.Add("No coordinates in DB yet. Run PS1_3_Geocoder.ps1 to enable distance-based candidates.");

        var days = new List<DayBackupDto>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            int dow = (int)date.DayOfWeek; // 0=Sun, 1=Mon ... 6=Sat

            var hours = openingHours.FirstOrDefault(h =>
                (h.LocationCode == locationCode ||
                 (location.LocationCodeLegacy != null && h.LocationCode == location.LocationCodeLegacy)) &&
                h.DayOfWeek == dow);

            bool isNationalHoliday = publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational);
            string? bundesland = location.Bundesland
                ?? PlzBundesland.Get(location.LocationCode, location.PostalCode, location.Country);
            bool isRegionalHoliday = bundesland != null &&
                publicHolidays.Any(ph => ph.HolidayDate == date &&
                    string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));

            bool isClosed = hours == null || hours.IsClosed || isNationalHoliday || isRegionalHoliday;
            string? closedReason = isClosed
                ? (isNationalHoliday || isRegionalHoliday ? "PUBLIC_HOLIDAY" : "CLOSED_DAY")
                : null;

            if (isClosed)
            {
                days.Add(new DayBackupDto(date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                    locationCode, location.DisplayName, false, closedReason,
                    minRequired, minIsDefault, 0, 0, false, new(), new(), new()));
                continue;
            }

            var dayWic = wicEntries
                .Where(w => w.ShiftDate == date && w.IsOnSite && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, location))
                .ToList();

            var absentIds   = new List<string>();
            var absentNames = new List<string>();
            double presentDouble = 0;

            foreach (var we in dayWic)
            {
                shiftByEmpDate.TryGetValue((we.EmployeeId, date), out var shift);
                if (shift != null && FullAbsenceTypes.Contains(shift.ShiftType))
                {
                    absentIds.Add(we.EmployeeId);
                    absentNames.Add(empById.TryGetValue(we.EmployeeId, out var ae) ? (ae.FullName ?? we.EmployeeId) : we.EmployeeId);
                }
                else if (shift != null && string.Equals(shift.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
                    presentDouble += 0.5;
                else
                    presentDouble += 1.0;
            }

            int effectiveCoverage = (int)Math.Floor(presentDouble);
            bool isAtRisk = effectiveCoverage < minRequired;

            var candidates = new List<BackupCandidateDto>();

            if (isAtRisk)
            {
                var alreadyOnWic = wicEntries
                    .Where(w => w.ShiftDate == date && w.IsOnSite && !WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, location))
                    .Select(w => w.EmployeeId).ToHashSet();

                bool IsAvailable(string empId)
                {
                    if (absentIds.Contains(empId)) return false;
                    if (alreadyOnWic.Contains(empId)) return false;
                    if (!shiftByEmpDate.TryGetValue((empId, date), out var sh)) return false;
                    return !FullAbsenceTypes.Contains(sh.ShiftType) && !OccupiedTypes.Contains(sh.ShiftType);
                }

                // (A) Designated BACKUP for this location
                var backupNames = allAssignments
                    .Where(a => (a.LocationCode == locationCode ||
                                 a.LocationCode == location.LocationCodeLegacy) &&
                                a.AssignmentType == "BACKUP")
                    .Select(a => a.EmployeeName).ToList();

                foreach (var bName in backupNames)
                {
                    var emp = allEmployees.FirstOrDefault(e =>
                        string.Equals(e.FullName, bName, StringComparison.OrdinalIgnoreCase));
                    if (emp == null || !IsAvailable(emp.EmployeeId)) continue;
                    candidates.Add(new BackupCandidateDto(
                        emp.EmployeeId, emp.FullName ?? bName, emp.TeamLeadName,
                        "BACKUP", null, null, null, null, null,
                        1000.0 - candidates.Count * 0.1,
                        "Designated backup for this WIC"));
                }

                // (B) Backlog/L2 agents (PrimaryRole == "SSP")
                foreach (var emp in allEmployees.Where(e => e.PrimaryRole == "SSP"))
                {
                    if (!IsAvailable(emp.EmployeeId)) continue;
                    if (candidates.Any(c => c.EmployeeId == emp.EmployeeId)) continue;
                    int backlogRank = candidates.Count(c => c.Source == "BACKLOG");
                    candidates.Add(new BackupCandidateDto(
                        emp.EmployeeId, emp.FullName ?? emp.EmployeeId, emp.TeamLeadName,
                        "BACKLOG", null, null, null, null, null,
                        500.0 - backlogRank * 0.1,
                        "Backlog/L2 (SSP) agent — reassigning does not risk closing another WIC"));
                }

                // (C) Agents from nearest WICs (requires coordinates)
                if (hasCoords)
                {
                    var nearbyLocs = allLocations
                        .Where(l => l.LocationCode != locationCode && !string.IsNullOrEmpty(l.Coordinates))
                        .Select(l => (Loc: l, Dist: Haversine(location.Coordinates, l.Coordinates)))
                        .Where(x => x.Dist.HasValue)
                        .OrderBy(x => x.Dist!.Value)
                        .ToList();

                    foreach (var (nearLoc, dist) in nearbyLocs)
                    {
                        int nearMin = nearLoc.MinAgentsRequired ?? DefaultMinAgents;

                        var nearWic = wicEntries
                            .Where(w => w.ShiftDate == date && w.IsOnSite && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, nearLoc))
                            .ToList();

                        var nearAvailIds = nearWic
                            .Where(w => {
                                shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                                return sh == null || !FullAbsenceTypes.Contains(sh.ShiftType);
                            })
                            .Select(w => w.EmployeeId).ToList();

                        int donorCoverage = nearAvailIds.Count;
                        int surplus = donorCoverage - nearMin;
                        if (surplus <= 0) continue;

                        int donated = 0;
                        foreach (var empId in nearAvailIds.Take(surplus))
                        {
                            if (!IsAvailable(empId)) continue;
                            if (candidates.Any(c => c.EmployeeId == empId)) continue;
                            if (!empById.TryGetValue(empId, out var emp)) continue;

                            double km = Math.Round(dist!.Value, 1);
                            int donorAfter = donorCoverage - 1 - donated;
                            candidates.Add(new BackupCandidateDto(
                                emp.EmployeeId, emp.FullName ?? empId, emp.TeamLeadName,
                                $"WIC:{nearLoc.LocationCode}", km,
                                nearLoc.LocationCode, nearLoc.DisplayName,
                                donorAfter, nearMin,
                                200.0 - km,
                                $"From {nearLoc.DisplayName} ({km} km straight-line). " +
                                $"Donor stays {donorAfter}/{nearMin} after donation."));
                            donated++;
                        }
                    }
                }
                else if (anyCoords)
                {
                    warningParts.Add($"Coordinates missing for {location.DisplayName} — nearest-WIC candidates skipped.");
                }

                candidates = candidates.OrderByDescending(c => c.Score).ToList();
            }

            days.Add(new DayBackupDto(
                date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                locationCode, location.DisplayName, true, null,
                minRequired, minIsDefault, dayWic.Count, effectiveCoverage, isAtRisk,
                absentIds, absentNames, candidates));
        }

        return new BackupResponseDto(
            locationCode, location.DisplayName, days,
            warningParts.Count > 0 ? string.Join(" | ", warningParts) : null);
    }
}

public static class BackupEndpointMapper
{
    public static void MapBackupEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wic/backup", async (string locationCode, string? date, int? horizon, BackupService svc) =>
        {
            var result = await svc.GetBackupAsync(locationCode, date, horizon ?? 3);
            return result == null ? Results.NotFound(new { error = $"Location '{locationCode}' not found." }) : Results.Ok(result);
        }).WithTags("WIC");
    }
}
