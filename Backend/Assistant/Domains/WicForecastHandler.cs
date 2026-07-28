using GSDDashboard.API.Services;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class WicForecastHandler(
    ForecastService              svc,
    ILogger<WicForecastHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "wic-forecast";
    public string DomainLabel => "WIC coverage forecast";

    public int Score(string q)
    {
        // "wic coverage" (without a "lowest/worst/fewest" superlative) is a forecast query,
        // so score it slightly higher than the WicLeave baseline of 90 to win the tie.
        if (q.Contains("wic coverage") || q.Contains("wic deckung"))
            return 95;
        if (q.Contains("forecast")         || q.Contains("prognose")         ||
            q.Contains("vorhersage")        || q.Contains("at risk")          ||
            q.Contains("coverage risk")     || q.Contains("deckungsluecke")   ||
            q.Contains("coverage forecast") || q.Contains("risk days")        ||
            q.Contains("wic risk")          || q.Contains("wic forecast")     ||
            q.Contains("upcoming coverage") || q.Contains("coverage next"))
            return 90;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        int horizon;
        if (parsed.DateWasExplicit)
        {
            var days = (int)(parsed.To.ToDateTime(TimeOnly.MinValue) -
                             parsed.From.ToDateTime(TimeOnly.MinValue)).TotalDays;
            horizon = Math.Max(1, Math.Min(days + 1, 90));
        }
        else
        {
            horizon = 14;
        }

        ForecastResponse forecast;
        try   { forecast = await A.FetchWithRetry(() => svc.GetForecastAsync(horizon, null)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Forecast fetch failed");
            return new AssistantResponse(A.BackendError, $"next {horizon} days", null, A.BackendError);
        }

        var label   = $"Next {horizon} days";
        var atRisk  = forecast.Locations.Where(l => l.AtRiskDays > 0).ToList();

        if (atRisk.Count == 0)
            return new AssistantResponse(
                $"WIC coverage looks good for the next {horizon} days — " +
                $"no at-risk days across {forecast.LocationCount} locations.",
                label, null, null);

        var rows = atRisk
            .SelectMany(loc => loc.Forecast
                .Where(d => d.IsAtRisk)
                .Select(d => new AssistantTableRow(
                    loc.DisplayName,
                    loc.LocationCode,
                    d.Date, "",
                    d.CoverageBuffer < 0 ? d.CoverageBuffer : (int?)null,
                    loc.City,
                    d.Status)))
            .Take(20)
            .ToArray();

        var text = $"WIC coverage at risk: {forecast.TotalAtRiskDays} at-risk day(s) across " +
                   $"{atRisk.Count} location(s) in the next {horizon} days.";
        return new AssistantResponse(text, label, rows.Length > 0 ? rows : null, null);
    }
}
