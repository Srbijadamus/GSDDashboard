using GSDDashboard.API.Modules.ALBalance;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class ALBalanceHandler(
    ALBalanceService           svc,
    ILogger<ALBalanceHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "al-balance";
    public string DomainLabel => "AL balance";

    public int Score(string q)
    {
        if (q.Contains("balance")         || q.Contains("verbleibend")    ||
            q.Contains("urlaubskonto")    || q.Contains("remaining al")   ||
            q.Contains("al remaining")    || q.Contains("al balance")     ||
            q.Contains("jahresurlaub")    || q.Contains("remaining days") ||
            q.Contains("remaining leave") || q.Contains("tage uebrig")    ||
            q.Contains("how many days left"))
            return 90;
        if ((q.Contains("wie viele tage") || q.Contains("how many days")) &&
            (q.Contains("urlaub") || q.Contains("leave") || q.Contains("al")))
            return 85;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        List<ALBalanceDto> all;
        try   { all = await A.FetchWithRetry(() => svc.GetAllAsync()); }
        catch (Exception ex)
        {
            logger.LogError(ex, "ALBalance fetch failed");
            return new AssistantResponse(A.BackendError, "current", null, A.BackendError);
        }

        if (parsed.PersonHint is not null)
        {
            var matches = all
                .Where(a => (a.EmployeeName ?? "").Contains(parsed.PersonHint, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
                return new AssistantResponse(
                    $"No employee found matching \"{parsed.PersonHint}\" in AL balance records.",
                    "current", null, null);
            var rows = matches.Select(ToRow).ToArray();
            return new AssistantResponse(
                $"AL balance for \"{parsed.PersonHint}\": {matches.Count} record{(matches.Count == 1 ? "" : "s")}.",
                "current", rows, null);
        }

        if (parsed.IsLowestQuery)
        {
            var worst = all
                .Where(a => a.RemainingAL >= 0)
                .OrderBy(a => a.RemainingAL)
                .Take(10)
                .ToList();
            return new AssistantResponse(
                $"Top 10 employees with lowest remaining AL balance (current).",
                "current", worst.Select(ToRow).ToArray(), null);
        }

        var allRows = all.Select(ToRow).ToArray();
        return new AssistantResponse(
            $"AL balance for {allRows.Length} employee{(allRows.Length == 1 ? "" : "s")} (current).",
            "current", allRows.Length > 0 ? allRows : null, null);
    }

    private static AssistantTableRow ToRow(ALBalanceDto a) =>
        new(a.EmployeeName ?? a.EmployeeId ?? "",
            a.EmployeeId ?? "",
            $"Eligible: {a.EligibleDays}",
            $"Remaining: {a.RemainingAL}",
            a.RemainingAL,
            "",
            $"SL:{a.CountSL}");
}
