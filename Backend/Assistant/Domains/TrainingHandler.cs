using GSDDashboard.API.Modules.Training;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class TrainingHandler(
    TrainingService           svc,
    ILogger<TrainingHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "training";
    public string DomainLabel => "training sessions";

    public int Score(string q)
    {
        if (q.Contains("training") || q.Contains("schulung") ||
            q.Contains("session")  || q.Contains("trainings"))
            return 100;
        if (q.Contains("topic") || q.Contains("thema") || q.Contains("kurs"))
            return 80;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var q = parsed.NormalizedQ;

        if (q.Contains("topic") || q.Contains("thema") || q.Contains("kurs") ||
            q.Contains("available") || q.Contains("verfuegbar"))
        {
            List<TrainingTopicDto> topics;
            try   { topics = await A.FetchWithRetry(() => svc.GetTopicsAsync()); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Training topics fetch failed");
                return new AssistantResponse(A.BackendError, "current", null, A.BackendError);
            }
            var rows = topics.Select(t => new AssistantTableRow(
                t.Name,
                t.Id.ToString(),
                $"{t.DurationHours}h",
                $"{t.MinGroupSize}-{t.MaxGroupSize} agents",
                null,
                t.IsMandatory ? "Mandatory" : "Optional",
                t.Notes ?? "")).ToArray();
            return new AssistantResponse(
                $"{topics.Count} training topic{(topics.Count == 1 ? "" : "s")} available.",
                "current", rows.Length > 0 ? rows : null, null);
        }

        var fromStr  = parsed.DateWasExplicit
            ? parsed.From.ToString("yyyy-MM-dd")
            : DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var toStr    = parsed.DateWasExplicit
            ? parsed.To.ToString("yyyy-MM-dd")
            : DateOnly.FromDateTime(DateTime.Today).AddDays(14).ToString("yyyy-MM-dd");
        var rangeStr = $"{fromStr} → {toStr}";

        List<TrainingSessionDto> sessions;
        try   { sessions = await A.FetchWithRetry(() => svc.GetSessionsAsync(fromStr, toStr)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Training sessions fetch failed");
            return new AssistantResponse(A.BackendError, rangeStr, null, A.BackendError);
        }

        var sessionRows = sessions
            .OrderBy(s => s.ScheduledDate)
            .Select(s => new AssistantTableRow(
                s.TopicName,
                s.Id.ToString(),
                s.ScheduledDate,
                $"{s.StartTime}-{s.EndTime}",
                s.AgentIds.Count,
                s.Status,
                s.ConfirmedBy ?? ""))
            .ToArray();

        var text = sessionRows.Length == 0
            ? $"No training sessions scheduled ({rangeStr})."
            : $"{sessionRows.Length} training session{(sessionRows.Length == 1 ? "" : "s")} ({rangeStr}).";

        return new AssistantResponse(text, rangeStr, sessionRows.Length > 0 ? sessionRows : null, null);
    }
}
