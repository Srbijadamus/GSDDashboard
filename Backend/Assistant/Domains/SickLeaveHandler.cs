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
