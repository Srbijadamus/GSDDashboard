using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts; // CoverageStatus
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

// ─── Response DTOs ────────────────────────────────────────────────────────────

public record UpcomingAbsence(string From, string To, string AbsenceType);

public record SubstituteCandidate(
    string EmployeeId,
    string FullName,
    string SourceType,          // BACKUP | SSP | WIC_DONOR | CALL_IN
    string? BaseLocation,       // WIC LocationCode, Bundesland label, or null
    string? BaseCoordinates,    // "lat,lon" or null
    double? DistanceKm,
    string? ReachabilityTier,   // VERY_EASY | EASY | MODERATE | HARD | IMPRACTICAL | null
    double? TravelMinutes,      // null — straight-line only
    bool? HasDirectTrain,       // null — straight-line only
    string AvailabilityType,    // WORKING | HALF_AL | DAY_OFF
    double AvailabilityScore,   // 1.0 = full, 0.5 = HALF_AL
    string? DonorImpact,        // null for BACKUP/SSP; "Home WIC: 3->2, min=2 -> PARTIAL after removal"
    string CoverageResult,      // "UNCOVERED->COVERED" or "PARTIAL->COVERED" etc.
    string OneLineReason,
    string? ContactEmail,       // null — no email field on Employees
    int LoadScore,              // 30-day rolling substitution count from SubstitutionHistory; 0 if table empty
    List<UpcomingAbsence> UpcomingAbsences
);

public record DaySubstitutionResult(
    string Date,
    string DayOfWeek,
    string LocationCode,
    bool IsOpen,
    string? ClosedReason,
    double Present,             // decimal because HALF_AL = 0.5
    int Required,
    double Gap,                 // may be fractional, e.g. 0.5 if one HALF_AL
    int GapCeiling,             // ceil(Gap) = number of full substitutes needed
    string CurrentStatus,       // COVERED | PARTIAL | UNCOVERED | CLOSED
    string? BestPickId,
    List<SubstituteCandidate> Candidates,
    string? Warning
);

public record SubstitutionResponse(
    string LocationCode,
    string DisplayName,
    int Horizon,
    List<DaySubstitutionResult> Days,
    List<string> KnownNulls    // fields absent from DB, always null in this response
);

// ─── Service ─────────────────────────────────────────────────────────────────

public class SubstitutionService
{
    private readonly GSDContext _db;
    private readonly ReachabilityService _reach;
    private readonly CoverageEvaluator _eval;
    private const int DefaultMin = 1;

    // Bundesland centroid coordinates for SSP agents who have no WIC assignment.
    private static readonly Dictionary<string, string> _bundeslandCentroids =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "Bayern",                  "48.7904,11.4979" },
        { "Berlin",                  "52.5200,13.4050" },
        { "Brandenburg",             "52.4125,12.5316" },
        { "Hamburg",                 "53.5753,10.0153" },
        { "Hessen",                  "50.6521,9.1624"  },
        { "Mecklenburg-Vorpommern",  "53.6127,12.4296" },
        { "Niedersachsen",           "52.6367,9.8451"  },
        { "Nordrhein-Westfalen",     "51.4332,7.6616"  },
        { "Rheinland-Pfalz",         "49.9129,7.4531"  },
        { "Saarland",                "49.3964,7.0228"  },
        { "Sachsen",                 "51.1045,13.2017" },
        { "Sachsen-Anhalt",          "51.9503,11.6923" },
        { "Schleswig-Holstein",      "54.2194,9.6961"  },
        { "Thueringen",              "50.8786,11.0296" },
    };

    // Absence types that fully remove an agent from coverage (HALF_AL handled separately).
    private static readonly HashSet<string> _fullAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase)
        { "SL", "AL", "UL", "PH", "LPH", "RESIGNED", "TRAINING" };

    public SubstitutionService(GSDContext db, ReachabilityService reach, CoverageEvaluator eval)
    {
        _db    = db;
        _reach = reach;
        _eval  = eval;
    }

    public async Task<SubstitutionResponse?> GetSubstitutesAsync(
        string locationCode,
        string? dateStr,
        int horizon = 1,
        IReadOnlyList<string>? explicitAbsentIds = null)
    {
        horizon = Math.Clamp(horizon, 1, 7);
        var startDate = dateStr != null && DateOnly.TryParse(dateStr, out var pd)
            ? pd : DateOnly.FromDateTime(DateTime.Today);
        var endDate = startDate.AddDays(horizon - 1);

        var loc = await _db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode && l.IsActive);
        if (loc == null) return null;

        // ── Bulk-load reference data ──────────────────────────────────────────
        var allLocations   = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var allEmployees   = await _db.Employees.Where(e => e.IsActive && e.PrimaryRole != "2nd Level").ToListAsync();
        var allAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var allHours       = await _db.WicOpeningHours.ToListAsync();
        var publicHolidays = await _db.PublicHolidays.ToListAsync();

        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => g.First());

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= startDate && w.ShiftDate <= endDate)
            .ToListAsync();

        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.FirstDay <= endDate && sl.LastDay >= startDate && sl.EmployeeId != null)
            .ToListAsync();

        // 30-day lookahead data for UpcomingAbsences on each candidate
        var lookaheadEnd = startDate.AddDays(30);
        var futureSickLeaves = await _db.SickLeaves
            .Where(sl => sl.LastDay >= startDate && sl.FirstDay <= lookaheadEnd && sl.EmployeeId != null)
            .ToListAsync();
        var futureShiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= lookaheadEnd)
            .ToListAsync();

        // 30-day fairness history — graceful fallback if SubstitutionHistory table not yet created
        var thirtyDaysAgo = startDate.AddDays(-30);
        Dictionary<string, int> loadCounts;
        try
        {
            var recentHistory = await _db.SubstitutionHistory
                .Where(h => h.Date >= thirtyDaysAgo)
                .ToListAsync();
            loadCounts = recentHistory
                .GroupBy(h => h.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            loadCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var empByName = allEmployees
            .Where(e => e.FullName != null)
            .GroupBy(e => e.FullName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var empById = allEmployees.ToDictionary(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase);

        var locByCode = allLocations.ToDictionary(l => l.LocationCode, StringComparer.OrdinalIgnoreCase);
        var locByLegacyCode = allLocations
            .Where(l => l.LocationCodeLegacy != null)
            .ToDictionary(l => l.LocationCodeLegacy!, StringComparer.OrdinalIgnoreCase);

        // MAIN agents for this WIC (look up by EmployeeName -> Employee)
        var mainAssignments = allAssignments
            .Where(a => (a.LocationCode == locationCode ||
                         a.LocationCode == loc.LocationCodeLegacy) && a.AssignmentType == "MAIN")
            .ToList();

        var mainEmployees = mainAssignments
            .Select(a => empByName.TryGetValue(a.EmployeeName, out var e) ? e : null)
            .Where(e => e != null).Select(e => e!).ToList();

        var reachabilityMatrix = await _reach.GetMatrixAsync();

        List<UpcomingAbsence> GetAbsences(string empId)
        {
            var result = new List<UpcomingAbsence>();
            foreach (var sl in futureSickLeaves.Where(s => s.EmployeeId == empId).OrderBy(s => s.FirstDay))
                result.Add(new UpcomingAbsence(sl.FirstDay.ToString("yyyy-MM-dd"), sl.LastDay.ToString("yyyy-MM-dd"), "SL"));
            var plannedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AL", "UL", "HALF_AL", "TRAINING" };
            foreach (var sh in futureShiftEntries
                .Where(s => s.EmployeeId == empId && plannedTypes.Contains(s.ShiftType))
                .OrderBy(s => s.ShiftDate))
            {
                var d = sh.ShiftDate;
                if (!futureSickLeaves.Any(sl => sl.EmployeeId == empId && sl.FirstDay <= d && sl.LastDay >= d))
                    result.Add(new UpcomingAbsence(d.ToString("yyyy-MM-dd"), d.ToString("yyyy-MM-dd"), sh.ShiftType));
            }
            return [.. result.OrderBy(a => a.From)];
        }

        var days = new List<DaySubstitutionResult>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            int dow = (int)date.DayOfWeek;
            var hours = allHours.FirstOrDefault(h =>
                (h.LocationCode == locationCode ||
                 (loc.LocationCodeLegacy != null && h.LocationCode == loc.LocationCodeLegacy)) &&
                h.DayOfWeek == dow);

            bool isNational = publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational);
            string? bundesland = loc.Bundesland
                ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
            bool isRegional = bundesland != null && publicHolidays.Any(ph =>
                ph.HolidayDate == date &&
                string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));

            bool isClosed = hours == null || hours.IsClosed || isNational || isRegional;
            string? closedReason = isClosed
                ? (isNational || isRegional ? "PUBLIC_HOLIDAY" : "CLOSED_DAY")
                : null;

            if (isClosed)
            {
                days.Add(new DaySubstitutionResult(
                    date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(), locationCode,
                    false, closedReason, 0, loc.MinAgentsRequired ?? DefaultMin, 0, 0,
                    "CLOSED", null, [], null));
                continue;
            }

            int minRequired = loc.MinAgentsRequired ?? DefaultMin;

            // ── Determine absent + half-AL among MAIN agents ─────────────────

            HashSet<string> explicitAbsentSet = explicitAbsentIds != null
                ? new(explicitAbsentIds, StringComparer.OrdinalIgnoreCase)
                : [];

            // Count present only via WicShiftEntries at THIS location — same source
            // as ForecastService. MAIN agents physically at another WIC today are absent here.
            var dayWicHere = wicEntries
                .Where(w => w.ShiftDate == date && w.IsOnSite &&
                            WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
                .Select(w => w.EmployeeId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            double presentCount = 0;
            var absentMainIds   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var emp in mainEmployees)
            {
                bool explicitlyAbsent = explicitAbsentSet.Contains(emp.EmployeeId);

                bool sickToday2 = sickLeaves.Any(sl =>
                    sl.EmployeeId == emp.EmployeeId &&
                    sl.FirstDay <= date && sl.LastDay >= date);

                shiftByEmpDate.TryGetValue((emp.EmployeeId, date), out var sh);
                bool fullAbsent = explicitlyAbsent || sickToday2 ||
                    (sh != null && _fullAbsenceTypes.Contains(sh.ShiftType));

                if (fullAbsent) { absentMainIds.Add(emp.EmployeeId); continue; }

                // Must have a WicShiftEntry at this location to count as present here
                if (!dayWicHere.Contains(emp.EmployeeId)) { absentMainIds.Add(emp.EmployeeId); continue; }

                bool halfAL = sh != null &&
                    string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase);
                presentCount += halfAL ? 0.5 : 1.0;
            }

            double gap    = Math.Max(0, minRequired - presentCount);
            int gapCeil   = (int)Math.Ceiling(gap);

            var before    = CoverageEvaluator.Classify(false, (int)Math.Floor(presentCount), minRequired);
            string currentStatus = before.Status.ToString();

            if (gapCeil == 0)
            {
                days.Add(new DaySubstitutionResult(
                    date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(), locationCode,
                    true, null, presentCount, minRequired, 0, 0,
                    currentStatus, null, [], null));
                continue;
            }

            // ── Build candidate pool ─────────────────────────────────────────

            var onWicToday = wicEntries
                .Where(w => w.ShiftDate == date && w.IsOnSite)
                .Select(w => w.EmployeeId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool IsUnavailable(Employee emp, string? shiftType)
            {
                if (!emp.IsActive) return true;
                if (absentMainIds.Contains(emp.EmployeeId)) return true;
                if (sickLeaves.Any(sl => sl.EmployeeId == emp.EmployeeId
                        && sl.FirstDay <= date && sl.LastDay >= date)) return true;
                if (shiftType != null && _fullAbsenceTypes.Contains(shiftType)) return true;
                return false;
            }

            var candidates = new List<SubstituteCandidate>();

            // ── (A) Designated BACKUP agents ─────────────────────────────────
            var backupAssignments = allAssignments
                .Where(a => (a.LocationCode == locationCode ||
                             a.LocationCode == loc.LocationCodeLegacy) && a.AssignmentType == "BACKUP")
                .ToList();

            foreach (var ba in backupAssignments)
            {
                if (!empByName.TryGetValue(ba.EmployeeName, out var emp)) continue;
                shiftByEmpDate.TryGetValue((emp.EmployeeId, date), out var sh);
                if (IsUnavailable(emp, sh?.ShiftType)) continue;

                bool halfAL = string.Equals(sh?.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase);
                double avail = halfAL ? 0.5 : 1.0;
                string availType = halfAL ? "HALF_AL" : "WORKING";

                var (baseCode, baseCoords, baseLbl) = ResolveBase(emp, locationCode, loc.LocationCodeLegacy, allAssignments, locByCode, locByLegacyCode);
                var reach = baseCoords != null
                    ? reachabilityMatrix.FirstOrDefault(r =>
                        r.FromCode == baseCode && r.ToCode == locationCode)
                    : null;
                double? distKm  = reach?.DistanceKm;
                string? tierStr = reach?.Tier.ToString();
                if (distKm == null && !string.IsNullOrEmpty(baseCoords) && !string.IsNullOrEmpty(loc.Coordinates))
                { distKm = HaversineKm(baseCoords, loc.Coordinates); tierStr = TierFromKm(distKm.Value); }

                var afterStatus = CoverageEvaluator.Classify(false,
                    (int)Math.Floor(presentCount + avail), minRequired).Status.ToString();

                candidates.Add(new SubstituteCandidate(
                    emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                    "BACKUP", baseLbl, baseCoords,
                    distKm, tierStr,
                    null, null,
                    availType, avail, null,
                    $"{currentStatus}->{afterStatus}",
                    "Designated backup agent for this WIC",
                    null,
                    loadCounts.GetValueOrDefault(emp.EmployeeId, 0),
                    GetAbsences(emp.EmployeeId)));
            }

            // ── (B) SSP backlog agents ────────────────────────────────────────
            // FIX: sh==null means no shift row imported — treat as full working day.
            // Only skip if IsUnavailable (sick/absent) or an explicit absence ShiftType.
            var sspAgents = allEmployees.Where(e => e.PrimaryRole == "SSP").ToList();
            foreach (var emp in sspAgents)
            {
                if (candidates.Any(c => c.EmployeeId == emp.EmployeeId)) continue;
                if (onWicToday.Contains(emp.EmployeeId)) continue;
                shiftByEmpDate.TryGetValue((emp.EmployeeId, date), out var sh);
                if (IsUnavailable(emp, sh?.ShiftType)) continue;

                bool halfAL = sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase);
                double avail = halfAL ? 0.5 : 1.0;
                string availType = halfAL ? "HALF_AL" : "WORKING";

                var (baseCode, baseCoords, baseLbl) = ResolveBase(emp, locationCode, loc.LocationCodeLegacy, allAssignments, locByCode, locByLegacyCode);
                var reach = baseCode != null && baseCoords != null
                    ? reachabilityMatrix.FirstOrDefault(r =>
                        r.FromCode == baseCode && r.ToCode == locationCode)
                    : null;
                double? distKm  = reach?.DistanceKm;
                string? tierStr = reach?.Tier.ToString();
                if (distKm == null && !string.IsNullOrEmpty(baseCoords) && !string.IsNullOrEmpty(loc.Coordinates))
                { distKm = HaversineKm(baseCoords, loc.Coordinates); tierStr = TierFromKm(distKm.Value); }

                var afterStatus = CoverageEvaluator.Classify(false,
                    (int)Math.Floor(presentCount + avail), minRequired).Status.ToString();

                candidates.Add(new SubstituteCandidate(
                    emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                    "SSP", baseLbl, baseCoords,
                    distKm, tierStr,
                    null, null,
                    availType, avail, null,
                    $"{currentStatus}->{afterStatus}",
                    "SSP backlog agent — reassignment carries zero donor risk",
                    null,
                    loadCounts.GetValueOrDefault(emp.EmployeeId, 0),
                    GetAbsences(emp.EmployeeId)));
            }

            // ── (B2) Working Voice / VWIC agents (office-based, redeployable) ──
            // Voice agents working their normal shift are in the HQ office and can
            // be sent to a nearby WIC. Excluded: absent, already on WIC today, 2nd Level.
            var voiceAgents = allEmployees
                .Where(e => e.PrimaryRole == "Voice" || e.PrimaryRole == "VWIC")
                .ToList();
            foreach (var emp in voiceAgents)
            {
                if (candidates.Any(c => c.EmployeeId == emp.EmployeeId)) continue;
                if (onWicToday.Contains(emp.EmployeeId)) continue;
                shiftByEmpDate.TryGetValue((emp.EmployeeId, date), out var sh);
                if (IsUnavailable(emp, sh?.ShiftType)) continue;
                // Only include if working (or no shift row = assumed working)
                bool isWorking = sh == null ||
                    string.Equals(sh.ShiftType, "WORKING", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase);
                if (!isWorking) continue;

                bool halfAL = sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase);
                double avail = halfAL ? 0.5 : 1.0;
                string availType = halfAL ? "HALF_AL" : "WORKING";

                var (baseCode, baseCoords, baseLbl) = ResolveBase(emp, locationCode, loc.LocationCodeLegacy, allAssignments, locByCode, locByLegacyCode);
                var reach = baseCode != null && baseCoords != null
                    ? reachabilityMatrix.FirstOrDefault(r =>
                        r.FromCode == baseCode && r.ToCode == locationCode)
                    : null;
                double? distKm  = reach?.DistanceKm;
                string? tierStr = reach?.Tier.ToString();
                if (distKm == null && !string.IsNullOrEmpty(baseCoords) && !string.IsNullOrEmpty(loc.Coordinates))
                { distKm = HaversineKm(baseCoords, loc.Coordinates); tierStr = TierFromKm(distKm.Value); }

                var afterStatus = CoverageEvaluator.Classify(false,
                    (int)Math.Floor(presentCount + avail), minRequired).Status.ToString();

                candidates.Add(new SubstituteCandidate(
                    emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                    "SSP", baseLbl, baseCoords,
                    distKm, tierStr,
                    null, null,
                    availType, avail, null,
                    $"{currentStatus}->{afterStatus}",
                    $"Voice agent working today — office-based, redeployable to WIC",
                    null,
                    loadCounts.GetValueOrDefault(emp.EmployeeId, 0),
                    GetAbsences(emp.EmployeeId)));
            }

            // ── (C) MAIN agents from nearby WICs (with surplus) ──────────────
            var nearbyOrdered = reachabilityMatrix
                .Where(r => r.ToCode == locationCode)
                .OrderBy(r => r.DistanceKm)
                .ToList();

            foreach (var nearEntry in nearbyOrdered)
            {
                if (!locByCode.TryGetValue(nearEntry.FromCode, out var nearLoc)) continue;

                int nearMin = nearLoc.MinAgentsRequired ?? DefaultMin;

                var nearWicOnSite = wicEntries
                    .Where(w => w.ShiftDate == date && w.IsOnSite && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, nearLoc))
                    .ToList();

                int nearPresent = nearWicOnSite.Count(w =>
                {
                    shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                    return sh == null || (!_fullAbsenceTypes.Contains(sh.ShiftType)
                        && !string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase));
                });
                double nearHalfAL = nearWicOnSite.Count(w =>
                {
                    shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                    return sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase);
                }) * 0.5;
                double nearEffective = nearPresent + nearHalfAL;
                double surplus = nearEffective - nearMin;
                if (surplus <= 0) continue;

                int donated = 0;
                foreach (var we in nearWicOnSite.Take((int)Math.Floor(surplus)))
                {
                    if (candidates.Any(c => c.EmployeeId == we.EmployeeId)) continue;
                    if (onWicToday.Contains(we.EmployeeId)) continue;
                    shiftByEmpDate.TryGetValue((we.EmployeeId, date), out var sh);
                    if (sh != null && _fullAbsenceTypes.Contains(sh.ShiftType)) continue;
                    if (!empById.TryGetValue(we.EmployeeId, out var emp)) continue;

                    bool halfAL = sh != null &&
                        string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase);
                    double avail = halfAL ? 0.5 : 1.0;
                    string availType = halfAL ? "HALF_AL" : "WORKING";

                    double nearAfterCount = nearEffective - avail;
                    var nearAfterStatus = CoverageEvaluator.Classify(false, (int)Math.Floor(nearAfterCount), nearMin);
                    string donorImpact = $"Home WIC: {nearEffective:0.#}->{nearAfterCount:0.#}, min={nearMin} -> {nearAfterStatus.Status} after removal";

                    var afterStatus = CoverageEvaluator.Classify(false,
                        (int)Math.Floor(presentCount + avail), minRequired).Status.ToString();

                    candidates.Add(new SubstituteCandidate(
                        emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                        "WIC_DONOR", nearLoc.LocationCode, nearLoc.Coordinates,
                        nearEntry.DistanceKm, nearEntry.Tier.ToString(),
                        null, null,
                        availType, avail, donorImpact,
                        $"{currentStatus}->{afterStatus}",
                        $"From {nearLoc.DisplayName} ({nearEntry.DistanceKm} km straight-line). {donorImpact}",
                        null,
                        loadCounts.GetValueOrDefault(emp.EmployeeId, 0),
                        GetAbsences(emp.EmployeeId)));
                    donated++;
                }
            }

            // ── (D) Day-off call-ins (last resort) ────────────────────────────
            foreach (var emp in allEmployees)
            {
                if (candidates.Any(c => c.EmployeeId == emp.EmployeeId)) continue;
                if (onWicToday.Contains(emp.EmployeeId)) continue;
                shiftByEmpDate.TryGetValue((emp.EmployeeId, date), out var sh);

                bool isDayOff = sh == null ||
                    string.Equals(sh.ShiftType, "OFF", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sh.ShiftType, "OFF_WEEKEND", StringComparison.OrdinalIgnoreCase);
                if (!isDayOff) continue;
                if (sh != null && _fullAbsenceTypes.Contains(sh.ShiftType)) continue;

                var (baseCode, baseCoords, baseLbl) = ResolveBase(emp, locationCode, loc.LocationCodeLegacy, allAssignments, locByCode, locByLegacyCode);
                var reach = baseCode != null && baseCoords != null
                    ? reachabilityMatrix.FirstOrDefault(r =>
                        r.FromCode == baseCode && r.ToCode == locationCode)
                    : null;
                double? distKm  = reach?.DistanceKm;
                string? tierStr = reach?.Tier.ToString();
                if (distKm == null && !string.IsNullOrEmpty(baseCoords) && !string.IsNullOrEmpty(loc.Coordinates))
                { distKm = HaversineKm(baseCoords, loc.Coordinates); tierStr = TierFromKm(distKm.Value); }

                var afterStatus = CoverageEvaluator.Classify(false,
                    (int)Math.Floor(presentCount + 1.0), minRequired).Status.ToString();

                candidates.Add(new SubstituteCandidate(
                    emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                    "CALL_IN", baseLbl, baseCoords,
                    distKm, tierStr,
                    null, null,
                    "DAY_OFF", 1.0, null,
                    $"{currentStatus}->{afterStatus}",
                    "Day-off call-in (last resort) — requires explicit management approval",
                    null,
                    loadCounts.GetValueOrDefault(emp.EmployeeId, 0),
                    GetAbsences(emp.EmployeeId)));
            }

            // ── Score and rank ────────────────────────────────────────────────
            candidates = candidates
                .OrderByDescending(c => Score(c))
                .ToList();

            string? bestPickId = null;
            double filled = 0;
            foreach (var c in candidates)
            {
                filled += c.AvailabilityScore;
                if (filled >= gap) { bestPickId = c.EmployeeId; break; }
            }
            if (bestPickId == null && candidates.Count > 0)
                bestPickId = candidates[0].EmployeeId;

            string? warning = null;
            if (candidates.Count == 0)
                warning = "No candidates found — gap cannot be filled with available agents.";
            else if (filled < gap)
                warning = $"Best available fill: {filled:0.#}/{gapCeil} substitutes. Gap may not be fully covered.";

            days.Add(new DaySubstitutionResult(
                date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(), locationCode,
                true, null, presentCount, minRequired, gap, gapCeil,
                currentStatus, bestPickId, candidates, warning));
        }

        return new SubstitutionResponse(
            locationCode, loc.DisplayName, horizon, days,
            new List<string>
            {
                "contactEmail: null — no email field on Employees table",
                "travelMinutes: null — no routing API integrated (straight-line only)",
                "hasDirectTrain: null — no transit data source configured",
            });
    }

    private static double HaversineKm(string from, string to)
    {
        static (double lat, double lon) Parse(string s) {
            var p = s.Split(',');
            return (double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        const double R = 6371.0;
        var (lat1, lon1) = Parse(from);
        var (lat2, lon2) = Parse(to);
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return Math.Round(R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a)), 1);
    }

    private static string TierFromKm(double km) => km switch
    {
        < 30  => "VERY_EASY",
        < 75  => "EASY",
        < 150 => "MODERATE",
        < 300 => "HARD",
        _     => "IMPRACTICAL",
    };

    private static double Score(SubstituteCandidate c)
    {
        double tierScore = c.ReachabilityTier switch
        {
            "VERY_EASY"   => 500,
            "EASY"        => 400,
            "MODERATE"    => 300,
            "HARD"        => 200,
            "IMPRACTICAL" => 50,
            _             => 0,
        };
        double sourceBonus = c.SourceType switch
        {
            "BACKUP"    => 10000,
            "SSP"       => 5000,
            "WIC_DONOR" => 0,
            "CALL_IN"   => -100,
            _           => 0,
        };
        return sourceBonus + tierScore - c.LoadScore * 10.0;
    }

    private static (string? code, string? coords, string? label) ResolveBase(
        Employee emp,
        string excludeCode,
        string? excludeLegacyCode,
        List<WicAgentAssignment> allAssignments,
        Dictionary<string, WicLocation> locByCode,
        Dictionary<string, WicLocation> locByLegacyCode)
    {
        var assignment = allAssignments.FirstOrDefault(a =>
            !string.Equals(a.LocationCode, excludeCode, StringComparison.OrdinalIgnoreCase) &&
            (excludeLegacyCode == null || !string.Equals(a.LocationCode, excludeLegacyCode, StringComparison.OrdinalIgnoreCase)) &&
            a.IsActive &&
            string.Equals(a.EmployeeName, emp.FullName, StringComparison.OrdinalIgnoreCase));
        if (assignment != null)
        {
            if (!locByCode.TryGetValue(assignment.LocationCode, out var wic))
                locByLegacyCode.TryGetValue(assignment.LocationCode, out wic);
            if (wic != null && !string.IsNullOrEmpty(wic.Coordinates))
                return (wic.LocationCode, wic.Coordinates, wic.LocationCode);
        }

        // All office-based roles home from Düsseldorf HQ — check both Primary and Secondary
        static bool IsHqBased(string? r) =>
            r is "SSP" or "Voice" or "VWIC" or "Chat" or "Dispatcher" or "SME" or "Booking Tool";
        if (IsHqBased(emp.PrimaryRole) || IsHqBased(emp.SecondaryRole))
            return (null, "51.2154,6.7837", "Düsseldorf HQ");

        if (!string.IsNullOrEmpty(emp.Bundesland) &&
            _bundeslandCentroids.TryGetValue(emp.Bundesland, out var centroid))
            return (null, centroid, $"~{emp.Bundesland} (approximate — Bundesland centroid only)");

        return (null, null, null);
    }
}

public static class SubstitutionEndpointMapper
{
    public static void MapSubstitutionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wic/substitutes", async (
            string locationCode,
            string? date,
            int? horizon,
            string? absentIds,
            SubstitutionService svc) =>
        {
            List<string>? parsedIds = absentIds != null
                ? absentIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => s.Length > 0)
                           .ToList()
                : null;

            var result = await svc.GetSubstitutesAsync(locationCode, date, horizon ?? 1, parsedIds);
            return result == null
                ? Results.NotFound(new { error = $"Location '{locationCode}' not found or inactive." })
                : Results.Ok(result);
        }).WithTags("WIC");
    }
}
