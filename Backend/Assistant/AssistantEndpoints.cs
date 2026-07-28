namespace GSDDashboard.API.Modules.Assistant;

public static class AssistantEndpointMapper
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/assistant").WithTags("Assistant");
        grp.MapPost("/ask", async (AskRequest req, AssistantService svc) =>
            Results.Ok(await svc.AskAsync(req.Question ?? "")));
        grp.MapPost("/score", (AskRequest req, IEnumerable<IDomainHandler> handlers) =>
        {
            var q = SharedParser.NormalizeUmlauts((req.Question ?? "").ToLowerInvariant().Trim());
            var scores = handlers
                .Select(h => new { domain = h.DomainKey, label = h.DomainLabel, score = h.Score(q) })
                .OrderByDescending(x => x.score);
            return Results.Ok(scores);
        });
    }
}
