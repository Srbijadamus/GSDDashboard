using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Shifts;

public record ValidationViolation(string Rule, string Description, string Law, bool IsHardBlock);
public record ValidationResult(bool Valid, List<ValidationViolation> Violations);

public class ShiftValidationService
{
    private readonly GSDContext _db;
    public ShiftValidationService(GSDContext db) => _db = db;

    public async Task<ValidationResult> ValidateAsync(int shiftId, string newShiftType, string? newStart, string? newEnd)
    {
        var violations = new List<ValidationViolation>();

        var shift = await _db.ShiftEntries.FindAsync(shiftId);
        if (shift == null) return new ValidationResult(true, violations);

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == shift.EmployeeId);
        if (emp == null) return new ValidationResult(true, violations);

        var date = shift.ShiftDate;
        bool isStudent = emp.Engagement == "Student";
        string? bundesland = emp.Bundesland;

        bool isWorkingType = newShiftType == ShiftTypes.Working || newShiftType == ShiftTypes.WicDuty;

        // R7 — Public Holiday
        if (newShiftType != ShiftTypes.Off && newShiftType != ShiftTypes.OffWeekend && newShiftType != ShiftTypes.Empty)
        {
            var ph = await _db.PublicHolidays.FirstOrDefaultAsync(h =>
                h.HolidayDate == date &&
                (h.IsNational || (!string.IsNullOrEmpty(bundesland) && h.Bundesland == bundesland)));

            if (ph != null && newShiftType != ShiftTypes.PublicHol && newShiftType != ShiftTypes.LocalPH)
            {
                violations.Add(new ValidationViolation("R7",
                    $"Agent has public holiday on {date}: {ph.Name}{(ph.IsNational ? " (National)" : $" ({ph.Bundesland})")}. Cannot schedule work on this day.",
                    "§ Public Holiday Law", true));
            }
        }

        if (!isWorkingType) return new ValidationResult(!violations.Any(v => v.IsHardBlock), violations);

        if (!TimeSpan.TryParse(newStart, out var startTs) || !TimeSpan.TryParse(newEnd, out var endTs))
            return new ValidationResult(!violations.Any(v => v.IsHardBlock), violations);

        double durationHours = endTs > startTs
            ? (endTs - startTs).TotalHours
            : (TimeSpan.FromHours(24) - startTs + endTs).TotalHours;

        // R2 — Max 10h per day
        if (durationHours > 10)
            violations.Add(new ValidationViolation("R2",
                $"Shift duration is {durationHours:F1}h. Maximum allowed is 10h per day.",
                "§3 ArbZG", true));

        // R4 — No work on Sunday
        if (date.DayOfWeek == DayOfWeek.Sunday)
            violations.Add(new ValidationViolation("R4",
                $"Cannot schedule work on Sunday ({date}). Sunday must be a rest day.",
                "§9 ArbZG", true));

        // Load nearby shifts for R1, R3, R5, R6
        var windowFrom = date.AddDays(-7);
        var windowTo   = date.AddDays(7);
        var nearbyShifts = await _db.ShiftEntries
            .Where(s => s.EmployeeId == shift.EmployeeId &&
                        s.ShiftDate >= windowFrom &&
                        s.ShiftDate <= windowTo &&
                        s.Id != shiftId &&
                        (s.ShiftType == ShiftTypes.Working || s.ShiftType == ShiftTypes.WicDuty))
            .OrderBy(s => s.ShiftDate)
            .ToListAsync();

        // R1 — Min 11h rest between shifts
        foreach (var nearby in nearbyShifts)
        {
            if (!TimeSpan.TryParse(nearby.ShiftStart, out var nStart) ||
                !TimeSpan.TryParse(nearby.ShiftEnd, out var nEnd)) continue;

            var nearbyDate = nearby.ShiftDate;
            double nearbyDuration = nEnd > nStart
                ? (nEnd - nStart).TotalHours
                : (TimeSpan.FromHours(24) - nStart + nEnd).TotalHours;

            if (nearby.ShiftDate == date.AddDays(-1))
            {
                var prevEnd = nearbyDate.ToDateTime(TimeOnly.FromTimeSpan(nEnd));
                var newStartDt = date.ToDateTime(TimeOnly.FromTimeSpan(startTs));
                double restHours = (newStartDt - prevEnd).TotalHours;
                if (restHours < 11)
                    violations.Add(new ValidationViolation("R1",
                        $"Only {restHours:F1}h rest between shifts. Minimum is 11h. Previous shift ended at {nEnd}, new starts at {newStart}.",
                        "§5 ArbZG", true));
            }

            if (nearby.ShiftDate == date.AddDays(1))
            {
                var newEndDt = date.ToDateTime(TimeOnly.FromTimeSpan(endTs));
                var nextStartDt = nearby.ShiftDate.ToDateTime(TimeOnly.FromTimeSpan(nStart));
                double restHours = (nextStartDt - newEndDt).TotalHours;
                if (restHours < 11)
                    violations.Add(new ValidationViolation("R1",
                        $"Only {restHours:F1}h rest before next shift. Minimum is 11h. New shift ends at {newEnd}, next starts at {nStart}.",
                        "§5 ArbZG", true));
            }
        }

        // R3 — Max 48h per week
        var weekFrom = date.AddDays(-(int)date.DayOfWeek + 1);
        var weekTo   = weekFrom.AddDays(6);
        var weekShifts = nearbyShifts.Where(s => s.ShiftDate >= weekFrom && s.ShiftDate <= weekTo).ToList();
        double weekHours = durationHours;
        foreach (var ws in weekShifts)
        {
            if (!TimeSpan.TryParse(ws.ShiftStart, out var ws1) || !TimeSpan.TryParse(ws.ShiftEnd, out var we1)) continue;
            weekHours += we1 > ws1 ? (we1 - ws1).TotalHours : (TimeSpan.FromHours(24) - ws1 + we1).TotalHours;
        }
        if (weekHours > 48)
            violations.Add(new ValidationViolation("R3",
                $"Total weekly hours would be {weekHours:F1}h. Maximum allowed is 48h.",
                "§3 ArbZG", true));

        // R5 — Max 6 consecutive working days
        int consecutive = 1;
        for (int i = 1; i <= 6; i++)
        {
            var prevDay = date.AddDays(-i);
            if (nearbyShifts.Any(s => s.ShiftDate == prevDay)) consecutive++;
            else break;
        }
        if (consecutive > 6)
            violations.Add(new ValidationViolation("R5",
                $"Agent would work {consecutive} consecutive days. Maximum is 6.",
                "§5 ArbZG", true));

        // R6 — Students max 20h/week (warning only)
        if (isStudent && weekHours > 20)
            violations.Add(new ValidationViolation("R6",
                $"Student would work {weekHours:F1}h this week. Recommended maximum is 20h.",
                "§3 ArbZG (Student)", false));

        return new ValidationResult(!violations.Any(v => v.IsHardBlock), violations);
    }
}
