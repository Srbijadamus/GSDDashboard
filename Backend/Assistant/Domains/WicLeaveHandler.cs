using WicAssistantService = GSDDashboard.API.Modules.WicAssistant.WicAssistantService;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class WicLeaveHandler(WicAssistantService wicSvc) : IDomainHandler
{
    public string DomainKey   => "wic-leave";
    public string DomainLabel => "WIC annual leave";

    public int Score(string q)
    {
        // Yield to higher-specificity domains
        if (q.Contains("sick")     || q.Contains("krank"))           return 0;
        if (q.Contains("pipeline"))                                   return 0;
        if (q.Contains("training") || q.Contains("schulung"))        return 0;
        if (q.Contains("balance")  || q.Contains("urlaubskonto"))    return 0;
        if (q.Contains("forecast") || q.Contains("prognose")  ||
            q.Contains("vorhersage") || q.Contains("at risk"))       return 0;
        if (q.Contains("employee list") || q.Contains("mitarbeiterliste")) return 0;

        int score = 0;
        if (q.Contains("wic"))
            score += 90;
        if (q.Contains("leave") || q.Contains("urlaub") || q.Contains("annual") ||
            q.Contains("absent") || q.Contains("absence") || q.Contains("away") ||
            q.Contains("frei")   || q.Contains("abwesend") || q.Contains("off"))
            score += 60;
        if (q.Contains("vacation") || q.Contains("holiday"))
            score += 55;
        if (q.Contains("who is") || q.Contains("wer ist") || q.Contains("wer hat"))
            score += 10;
        if (q.Contains("how many") || q.Contains("wie viele"))
            score += 5;
        // "lowest/worst" strongly signals the WicAssistantService LowestCoverage intent
        if (q.Contains("lowest") || q.Contains("fewest") || q.Contains("wenigsten") ||
            q.Contains("worst")  || q.Contains("schlechtesten"))
            score += 15;
        return score;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var wic = await wicSvc.AskAsync(parsed.RawQuestion);

        string? hint = null;
        if (!parsed.NormalizedQ.Contains("wic") &&
            !parsed.NormalizedQ.Contains("all employ") &&
            !parsed.NormalizedQ.Contains("alle mitarbeiter") &&
            wic.Error is null)
        {
            hint = "WIC agents only. For all employees: \"show all employee vacation\"";
        }

        return new AssistantResponse(
            wic.AnswerText,
            wic.DateRangeChecked,
            wic.Table?.Select(r => new AssistantTableRow(
                r.Employee, r.EmployeeId, r.Start, r.End, r.WorkDays, r.WicLocation, r.Role)).ToArray(),
            wic.Error,
            hint);
    }
}
