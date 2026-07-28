using GSDDashboard.API.Modules.Vacations;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class VacationsHandler(
    VacationService           svc,
    ILogger<VacationsHandler> logger) : IDomainHandler
{
    public string DomainKey   => "vacations";
    public string DomainLabel => "all-employee vacation";

    public int Score(string q)
    {
        if (q.Contains("sick") || q.Contains("krank")) return 0;
        if (q.Contains("pipeline"))                    return 0;
        if (q.Contains("training"))                    return 0;
        if (q.Contains("balance") || q.Contains("urlaubskonto")) return 0;

        int score = 0;
        // Explicit "all employees" signals intent for non-WIC data
        if (q.Contains("all employ")    || q.Contains("alle mitarbeiter") ||
            q.Contains("all employee")  || q.Contains("everyone on leave") ||
            q.Contains("jeder im urlaub"))
            score += 85;

        if (q.Contains("wic")) score += 25; // lower than WicLeave's +90 for "wic"

        if (q.Contains("leave") || q.Contains("urlaub") || q.Contains("vacation") ||
            q.Contains("holiday") || q.Contains("absent") || q.Contains("absence") ||
            q.Contains("away") || q.Contains("frei") || q.Contains("abwesend"))
            score += 40;

        return score;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var fromStr  = parsed.From.ToString("yyyy-MM-dd");
        var toStr    = parsed.To.ToString("yyyy-MM-dd");
        var rangeStr = $"{fromStr} → {toStr}";

        List<VacationDto> vacations;
        try   { vacations = await A.FetchWithRetry(() => svc.GetVacationsAsync(fromStr, toStr, null, null, null)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vacations fetch failed");
            return new AssistantResponse(A.BackendError, rangeStr, null, A.BackendError);
        }

        if (parsed.PersonHint is not null)
            vacations = vacations
                .Where(v => (v.FirstName ?? "").Contains(parsed.PersonHint, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var rows = vacations
            .OrderBy(v => v.FirstDay)
            .Select(v => new AssistantTableRow(
                v.FirstName ?? v.EmployeeId ?? "",
                v.EmployeeId ?? "",
                v.FirstDay, v.LastDay,
                v.WorkDaysNet,
                "", ""))
            .ToArray();

        var text = rows.Length == 0
            ? $"No employees on annual leave ({rangeStr})."
            : $"{rows.Length} employee{(rows.Length == 1 ? "" : "s")} on annual leave ({rangeStr}).";

        return new AssistantResponse(text, rangeStr, rows.Length > 0 ? rows : null, null);
    }
}
