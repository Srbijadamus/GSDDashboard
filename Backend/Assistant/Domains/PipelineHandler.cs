using GSDDashboard.API.Modules.Pipeline;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class PipelineHandler(
    PipelineService           svc,
    ILogger<PipelineHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "pipeline";
    public string DomainLabel => "WIC pipeline events";

    public int Score(string q)
    {
        if (q.Contains("pipeline"))
            return 100;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var fromStr  = parsed.DateWasExplicit
            ? parsed.From.ToString("yyyy-MM-dd")
            : DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var toStr    = parsed.DateWasExplicit
            ? parsed.To.ToString("yyyy-MM-dd")
            : DateOnly.FromDateTime(DateTime.Today).AddDays(30).ToString("yyyy-MM-dd");
        var rangeStr = $"{fromStr} → {toStr}";

        List<PipelineDto> items;
        try   { items = await A.FetchWithRetry(() => svc.GetAllAsync(fromStr, toStr, null)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline fetch failed");
            return new AssistantResponse(A.BackendError, rangeStr, null, A.BackendError);
        }

        if (parsed.LocationHint is not null)
            items = items
                .Where(p => (p.LocationCode ?? "").Contains(parsed.LocationHint, StringComparison.OrdinalIgnoreCase) ||
                            (p.LocationName ?? "").Contains(parsed.LocationHint, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var rows = items
            .OrderBy(p => p.PipelineDate)
            .Select(p => new AssistantTableRow(
                p.Title ?? p.Description ?? "(no title)",
                p.Id.ToString(),
                p.PipelineDate,
                p.PipelineDateEnd ?? p.PipelineDate,
                null,
                p.LocationName ?? p.LocationCode ?? "",
                p.Status))
            .ToArray();

        var text = rows.Length == 0
            ? $"No pipeline events found ({rangeStr})."
            : $"{rows.Length} pipeline event{(rows.Length == 1 ? "" : "s")} ({rangeStr}).";

        return new AssistantResponse(text, rangeStr, rows.Length > 0 ? rows : null, null);
    }
}
