using GSDDashboard.API.Modules.SickLeave;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class SickLeaveHandler(
    SickLeaveService           svc,
    ILogger<SickLeaveHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "sick-leave";
    public string DomainLabel => "sick leave";

    public int Score(string q)
    {
        if (q.Contains("sick")         || q.Contains("krank")       ||
            q.Contains("krankenstand") || q.Contains("krankheit")   ||
            q.Contains("sick leave")   || q.Contains("krankheitstag"))
            return 100;
        // "sl" as standalone word
        if (System.Text.RegularExpressions.Regex.IsMatch(q, @"\bsl\b"))
            return 80;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var q = parsed.NormalizedQ;

        if (q.Contains("stats")      || q.Contains("statistik")  ||
            q.Contains("summary")    || q.Contains("breakdown")  ||
            q.Contains("uebersicht") || q.Contains("overview"))
        {
            var fromStr = parsed.DateWasExplicit ? parsed.From.ToString("yyyy-MM-dd") : null;
            var toStr   = parsed.DateWasExplicit ? parsed.To.ToString("yyyy-MM-dd")   : null;
            SickLeaveStatsDto stats;
            try   { stats = await A.FetchWithRetry(() => svc.GetStatsAsync(fromStr, toStr)); }
            catch (Exception ex)
            {
                logger.LogError(ex, "SickLeave stats fetch failed");
                return new AssistantResponse(A.BackendError, "", null, A.BackendError);
            }
            var label = fromStr is not null ? $"{fromStr} → {toStr}" : "all time";
            var text  = $"Sick leave ({label}): {stats.TotalActive} active, " +
                        $"avg {stats.AverageDuration:F1} days. " +
                        $"Self: {stats.SelfCount}, Child: {stats.ChildCount}.";
            return new AssistantResponse(text, label, null, null);
        }

        var date      = parsed.DateWasExplicit ? parsed.From : DateOnly.FromDateTime(DateTime.Today);
        var dateLabel = date.ToString("yyyy-MM-dd");

        List<SickLeaveDto> leaves;
        try   { leaves = await A.FetchWithRetry(() => svc.GetActiveOnDateAsync(date)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "SickLeave active fetch failed for {Date}", date);
            return new AssistantResponse(A.BackendError, dateLabel, null, A.BackendError);
        }

        // Long-term sick filter — "longer than N days", "21 day", "langzeitkrank", "long-term"
        bool isLongSick = q.Contains("longer than") || q.Contains("more than")     ||
                          q.Contains("21 day")       || q.Contains("langzeitkrank") ||
                          q.Contains("long-term")    || q.Contains("laenger als");
        if (isLongSick)
        {
            int threshold = 21;
            var numMatch = System.Text.RegularExpressions.Regex.Match(q, @"(\d+)\s*day");
            if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out var parsedDays))
                threshold = parsedDays;

            int today = date.DayNumber;
            leaves = leaves
                .Where(l => l.FirstDay != null &&
                            DateOnly.TryParse(l.FirstDay, out var fd) &&
                            (today - fd.DayNumber) >= threshold)
                .ToList();

            if (parsed.PersonHint is not null)
                leaves = leaves
                    .Where(l => (l.FullName ?? "").Contains(parsed.PersonHint, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var longRows = leaves.Select(l => new AssistantTableRow(
                l.FullName ?? l.EmployeeId ?? "",
                l.EmployeeId ?? "",
                l.FirstDay, l.LastDay,
                l.DurationDays, "", l.LeaveType ?? "SL")).ToArray();

            var longText = longRows.Length == 0
                ? $"No employees on sick leave for {threshold}+ days as of {dateLabel}."
                : $"{longRows.Length} employee{(longRows.Length == 1 ? "" : "s")} sick for {threshold}+ days as of {dateLabel}.";

            return new AssistantResponse(longText, dateLabel, longRows.Length > 0 ? longRows : null, null);
        }

        if (parsed.PersonHint is not null)
            leaves = leaves
                .Where(l => (l.FullName ?? "").Contains(parsed.PersonHint, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var rows = leaves.Select(l => new AssistantTableRow(
            l.FullName ?? l.EmployeeId ?? "",
            l.EmployeeId ?? "",
            l.FirstDay, l.LastDay,
            l.DurationDays,
            "",
            l.LeaveType ?? "SL")).ToArray();

        var answerText = rows.Length == 0
            ? $"No one is on sick leave on {dateLabel}."
            : $"{rows.Length} employee{(rows.Length == 1 ? "" : "s")} on sick leave on {dateLabel}.";

        return new AssistantResponse(answerText, dateLabel, rows.Length > 0 ? rows : null, null);
    }
}
