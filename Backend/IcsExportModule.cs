using System.Diagnostics;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.MyShifts;

/// <summary>
/// B7 (Features:IcsExport). GET /api/my/shifts.ics exports the LOGGED-IN user's own
/// ShiftEntries for the next 60 days as an ICS calendar.
///
/// Security notes (all enforced here, independent of Auth:EnforceAuthorization):
///   - Anonymous callers always get 401 (the RBAC middleware is a no-op in the rollout
///     phase, so the endpoint must not rely on it).
///   - Only the session's own EmployeeId is ever read; there is no id parameter a caller
///     could change to reach another person's rows.
///   - Logging records only the event count and duration, never names or ids.
///
/// Robustness: every row is serialized defensively. A row with a non-parsable time (or
/// any other shape Ical.Net cannot represent) is skipped for that event, never a 500.
/// </summary>
public static class MyShiftsEndpointMapper
{
    private const int ExportDays = 60;
    private const string Berlin = "Europe/Berlin";

    public static void MapMyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/my/shifts.ics", async (HttpContext ctx, GSDContext db, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("IcsExport");
            var sw = Stopwatch.StartNew();

            // Anonymous 401 always — even while Auth:EnforceAuthorization=false (RBAC no-op).
            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                logger.LogInformation("ICS export denied: anonymous request");
                return Results.Json(new { error = "Authentication required" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var employeeId = ctx.User.FindFirst(AuthClaims.EmployeeId)?.Value ?? "";
            var today = DateOnly.FromDateTime(DateTime.Today);
            var end = today.AddDays(ExportDays);

            // A role without an EmployeeId (e.g. an RTM placeholder) has no "own shifts":
            // the request still succeeds (200) with an empty calendar.
            List<ShiftEntry> shifts = new();
            if (!string.IsNullOrWhiteSpace(employeeId))
            {
                shifts = await db.ShiftEntries
                    .Where(s => s.EmployeeId == employeeId && s.ShiftDate >= today && s.ShiftDate <= end)
                    .OrderBy(s => s.ShiftDate)
                    .ToListAsync();
            }

            var calendar = new Calendar();
            int emitted = 0;
            foreach (var s in shifts)
            {
                var evt = BuildEvent(s);
                if (evt == null) continue;
                calendar.Events.Add(evt);
                emitted++;
            }

            var serializer = new CalendarSerializer();
            var ics = serializer.SerializeToString(calendar);
            sw.Stop();

            logger.LogInformation("ICS export complete: events={EventCount} durationMs={DurationMs}",
                emitted, sw.ElapsedMilliseconds);

            ctx.Response.Headers.ContentDisposition = "attachment; filename=\"shifts.ics\"";
            return Results.Text(ics, "text/calendar");
        }).WithTags("My");
    }

    /// <summary>
    /// Builds one VEVENT, or null when the row is unrepresentable (a non-parsable time, an
    /// inverted range, etc.). The per-row try/catch is the guarantee that bad data never
    /// surfaces as a 500: it degrades to "that one shift is omitted".
    /// </summary>
    private static CalendarEvent? BuildEvent(ShiftEntry s)
    {
        try
        {
            var type = string.IsNullOrWhiteSpace(s.ShiftType) ? "Shift" : s.ShiftType.Trim();
            var summary = type.Replace('_', ' ');

            // Timed only for the two types the plan treats as carrying hours, and only when
            // both bounds parse. Anything else (or a bad time) falls back to an all-day event.
            var hasStart = TimeOnly.TryParse(s.ShiftStart, System.Globalization.CultureInfo.InvariantCulture, out var start);
            var hasStop  = TimeOnly.TryParse(s.ShiftEnd,   System.Globalization.CultureInfo.InvariantCulture, out var stop);
            var isTimed = (type == ShiftTypes.Working || type == ShiftTypes.WicDuty) && hasStart && hasStop;

            if (isTimed)
            {
                var startDt = s.ShiftDate.ToDateTime(start);
                var endDt = s.ShiftDate.ToDateTime(stop);
                if (endDt <= startDt) endDt = startDt.AddHours(1);
                return new CalendarEvent
                {
                    Summary = summary,
                    DtStart = new CalDateTime(startDt, Berlin),
                    DtEnd = new CalDateTime(endDt, Berlin),
                };
            }

            // All-day: date-only CalDateTime values span [ShiftDate, ShiftDate+1). Ical.Net
            // emits a DATE (not DATE-TIME) value for a date-only CalDateTime, which is the
            // correct all-day encoding.
            var day = s.ShiftDate;
            var next = day.AddDays(1);
            return new CalendarEvent
            {
                Summary = summary,
                DtStart = new CalDateTime(day.Year, day.Month, day.Day),
                DtEnd = new CalDateTime(next.Year, next.Month, next.Day),
            };
        }
        catch
        {
            return null; // non-parsable data must never 500
        }
    }
}
