using GSDDashboard.API.Modules.Employees;

namespace GSDDashboard.API.Modules.Assistant;

public sealed class EmployeesHandler(
    EmployeeService           svc,
    ILogger<EmployeesHandler> logger) : IDomainHandler
{
    public string DomainKey   => "employees";
    public string DomainLabel => "employee list";

    public int Score(string q)
    {
        if (q.Contains("employee list")   || q.Contains("mitarbeiterliste") ||
            q.Contains("list of employee") || q.Contains("alle mitarbeiter") ||
            q.Contains("active employees") || q.Contains("aktive mitarbeiter") ||
            q.Contains("how many employees") || q.Contains("wie viele mitarbeiter") ||
            q.Contains("show employees")   || q.Contains("list agents"))
            return 90;
        if ((q.Contains("employee") || q.Contains("mitarbeiter")) &&
            (q.Contains("team lead") || q.Contains("teamleiter") || q.Contains("role") || q.Contains("rolle")))
            return 80;
        return 0;
    }

    public async Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed)
    {
        string? teamLead = parsed.TeamLeadHint;
        string? role     = null;

        var q = parsed.NormalizedQ;
        if (q.Contains("voice"))       role = "Voice";
        else if (q.Contains("chat"))   role = "Chat";
        else if (q.Contains("ssp"))    role = "SSP";
        else if (q.Contains("dispatcher")) role = "Dispatcher";

        List<EmployeeDto> employees;
        try   { employees = await A.FetchWithRetry(() => svc.GetEmployeesAsync(role, null, teamLead, null, true)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Employees fetch failed");
            return new AssistantResponse(A.BackendError, "current", null, A.BackendError);
        }

        var rows = employees.Select(e => new AssistantTableRow(
            e.FullName ?? e.EmployeeId,
            e.EmployeeId,
            e.PrimaryRole ?? "",
            e.TeamLeadName ?? "",
            null,
            e.Engagement ?? "",
            e.Category ?? "")).ToArray();

        var label = role is not null
            ? $"active {role} employees"
            : teamLead is not null
                ? $"employees under {teamLead}"
                : "active employees";

        var text = rows.Length == 0
            ? $"No {label} found."
            : $"{rows.Length} {label}.";

        return new AssistantResponse(text, "current", rows.Length > 0 ? rows : null, null);
    }
}
