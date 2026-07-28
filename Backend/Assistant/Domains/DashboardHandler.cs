using GSDDashboard.API.Modules.Dashboard;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class DashboardHandler(
    DashboardService           svc,
    ILogger<DashboardHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "dashboard";
    public string DomainLabel => "dashboard summary";

    public int Score(string q)
    {
        if (q.Contains("dashboard")      || q.Contains("summary")        ||
            q.Contains("overview")       || q.Contains("uebersicht")     ||
            q.Contains("headcount")      || q.Contains("kopfzahl")       ||
            q.Contains("working today")  || q.Contains("heute im dienst")||
            q.Contains("how many agents")|| q.Contains("wie viele agenten")||
            q.Contains("agents today")   || q.Contains("agenten heute")  ||
            q.Contains("on duty")        || q.Contains("im dienst"))
            return 80;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var date    = parsed.DateWasExplicit ? parsed.From : DateOnly.FromDateTime(DateTime.Today);
        var dateStr = date.ToString("yyyy-MM-dd");

        DashboardSummaryDto summary;
        try   { summary = await A.FetchWithRetry(() => svc.GetSummaryAsync(date)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dashboard summary fetch failed for {Date}", date);
            return new AssistantResponse(A.BackendError, dateStr, null, A.BackendError);
        }

        var text = $"Dashboard {dateStr}: {summary.TotalActive} total active. " +
                   $"Voice: {summary.WorkingVoice}, Chat: {summary.WorkingChat}, " +
                   $"SSP: {summary.WorkingSSP}, Dispatcher: {summary.WorkingDispatcher}. " +
                   $"AL: {summary.OnAL}, SL: {summary.OnSL}, " +
                   $"Training: {summary.OnTraining}, WIC duty: {summary.OnWicDuty}. " +
                   $"WIC unoccupied: {summary.WicUnoccupiedCount}.";

        return new AssistantResponse(text, dateStr, null, null);
    }
}
