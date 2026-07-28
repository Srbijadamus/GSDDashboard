namespace GSDDashboard.API.Modules.Assistant;

public interface IDomainHandler
{
    string DomainKey   { get; }
    string DomainLabel { get; }
    int    Score(string normalizedQ);
    Task<AssistantResponse> HandleAsync(AssistantParsedQuery parsed);
}
