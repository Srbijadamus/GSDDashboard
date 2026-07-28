using GSDDashboard.API.Services;
using System.Text.RegularExpressions;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class WicCoverageDetailHandler(
    WicCoverageService                 svc,
    ILogger<WicCoverageDetailHandler>  logger) : IDomainHandler
{
    public string DomainKey   => "wic-coverage";
    public string DomainLabel => "WIC coverage roles";

    public int Score(string q)
    {
        if (q.Contains("who covers")       || q.Contains("wer deckt")         ||
            q.Contains("main agent")       || q.Contains("hauptagent")        ||
            q.Contains("backup b")         || q.Contains("backup agent")      ||
            q.Contains("who is assigned")  || q.Contains("wer ist zustaendig")||
            q.Contains("coverage role")    || q.Contains("deckungsrolle"))
            return 90;
        if ((q.Contains("wic") && (q.Contains("backup") || q.Contains("main") ||
             q.Contains("role") || q.Contains("zustaendig"))))
            return 85;
        if (q.Contains("backup") || q.Contains("zustaendig"))
            return 60;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        var hint = parsed.LocationHint ?? ExtractCoverageCity(parsed.NormalizedQ) ?? ExtractLocationCode(parsed.NormalizedQ);

        List<WicListItemDto> wics;
        try   { wics = await A.FetchWithRetry(() => svc.GetWicsAsync(null)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "WicCoverage list fetch failed");
            return new AssistantResponse(A.BackendError, "current", null, A.BackendError);
        }

        if (hint is null)
        {
            var rows = wics.Select(w => new AssistantTableRow(
                w.DisplayName,
                w.LocationCode,
                $"Main: {w.MainCount}",
                $"Backup A: {w.BackupACount}",
                null,
                w.City ?? "",
                w.Bundesland ?? "")).ToArray();
            return new AssistantResponse(
                $"{wics.Count} WIC location{(wics.Count == 1 ? "" : "s")} found.",
                "current", rows.Length > 0 ? rows : null, null);
        }

        var matched = wics.Where(w =>
            w.DisplayName.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
            w.LocationCode.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
            (w.City ?? "").Contains(hint, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matched.Count == 0)
            return new AssistantResponse(
                $"No WIC location found matching \"{hint}\".",
                "current", null, null);

        var detailRows = new List<AssistantTableRow>();
        foreach (var wic in matched.Take(3))
        {
            WicCoverageDto? detail;
            try   { detail = await A.FetchWithRetry(() => svc.GetWicByCodeAsync(wic.LocationCode)); }
            catch { detail = null; }
            if (detail is null) continue;
            AddTierRows(detailRows, detail.Main,    wic.DisplayName, "MAIN");
            AddTierRows(detailRows, detail.BackupA, wic.DisplayName, "BACKUP_A");
            AddTierRows(detailRows, detail.BackupB, wic.DisplayName, "BACKUP_B");
        }

        var ans = detailRows.Count > 0
            ? $"Coverage agents for \"{hint}\" ({matched.Count} location match{(matched.Count == 1 ? "" : "es")})."
            : $"No coverage agents found for \"{hint}\".";
        return new AssistantResponse(ans, "current", detailRows.Count > 0 ? detailRows.ToArray() : null, null);
    }

    private static void AddTierRows(List<AssistantTableRow> list, AgentTierDto[] tier, string location, string tierLabel)
    {
        foreach (var a in tier)
            list.Add(new AssistantTableRow(
                a.Name,
                a.EmployeeId ?? "",
                location, "",
                null,
                tierLabel,
                a.PrimaryKid ?? ""));
    }

    private static string? ExtractCoverageCity(string q)
    {
        var m = Regex.Match(q, @"\b(?:who\s+covers?|wer\s+deckt|coverage\s+for|agents?\s+for)\s+([a-z][a-z\s\-]{1,30}?)(?:\?|$)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var city = m.Groups[1].Value.Trim();
            if (city.Length >= 2) return city;
        }
        return null;
    }

    private static string? ExtractLocationCode(string q)
    {
        var m = Regex.Match(q, @"\b(de|nl)_\w+", RegexOptions.IgnoreCase);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }
}
