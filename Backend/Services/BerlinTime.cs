using System.Globalization;

namespace GSDDashboard.API.Services;

/// <summary>
/// The one place where a stored instant becomes the wall clock a Pulse user sees.
///
/// The database keeps UTC (ManualChangeAt, CreatedAt, ... are written with
/// DateTime.UtcNow), and the requirement is that every time shown in the UI is
/// Europe/Berlin. Rather than scatter DateTime.ToLocalTime()/AddHours(2) around —
/// which silently depends on the process time zone and gets CET/CEST wrong — every
/// display goes through this helper, exactly like the ShiftKiosk side goes through
/// one AT TIME ZONE 'W. Europe Standard Time' expression.
///
/// No OS or SQL Server time zone is read or changed here: the zone rule lives in
/// the time zone database, so summer time is applied by the platform.
/// </summary>
public static class BerlinTime
{
    /// <summary>
    /// Europe/Berlin, resolved by IANA id first (portable) and by the Windows id
    /// second. Falls back to the process-local zone only if neither exists, never to
    /// UTC — a silent UTC fallback would reproduce the very defect this fixes.
    /// </summary>
    public static readonly TimeZoneInfo Zone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    /// <summary>Berlin wall clock for a stored value. See <see cref="FromUtc(DateTime)"/>.</summary>
    public static DateTime FromUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // SQL Server datetime2 has no offset; every column this helper formats is
            // written with DateTime.UtcNow, so an Unspecified value is a UTC instant.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    /// <summary>Berlin wall clock for a stored value, or null when there is none.</summary>
    public static DateTime? FromUtc(DateTime? value) => value is null ? null : FromUtc(value.Value);

    /// <summary>
    /// Formats a stored UTC instant as Berlin wall clock. Returns null for null, so it
    /// can be used directly in a DTO field next to ManualChangeBy.
    /// </summary>
    public static string? Stamp(DateTime? value, string format) =>
        value is null ? null : FromUtc(value.Value).ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Berlin wall clock now (the OS clock is never used for this).</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>The Berlin calendar day — the default day for date filters.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>Berlin wall clock now, formatted (e.g. a "generated at" stamp).</summary>
    public static string StampNow(string format) => Now.ToString(format, CultureInfo.InvariantCulture);
}
