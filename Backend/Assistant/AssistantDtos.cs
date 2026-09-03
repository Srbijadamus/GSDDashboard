using System.Text.Json.Serialization;

namespace GSDDashboard.API.Modules.Assistant;

public record AskRequest(string Question);

public record AssistantTableRow(
    string   Employee, string EmployeeId,
    string   Start,    string End,
    decimal? WorkDays, string WicLocation, string Role);

public record AssistantCoverageRow(
    string Location,
    string LocationCode,
    string Date,
    int    PresentAgents,
    int    MinRequired,
    int?   Deficit,
    string Status);

public record AssistantResponse(
    string                  AnswerText,
    string                  DateRangeChecked,
    AssistantTableRow[]?    Table,
    string?                 Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string?                 Hint = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string?                 TableType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AssistantCoverageRow[]? CoverageTable = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int?                    CoverageTableTotal = null);

internal static class A
{
    internal const string BackendError = "Could not retrieve data from the backend";

    internal static async Task<T> FetchWithRetry<T>(Func<Task<T>> fetch)
    {
        try   { return await fetch(); }
        catch { await Task.Delay(1000); return await fetch(); }
    }
}
