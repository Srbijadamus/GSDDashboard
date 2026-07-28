namespace GSDDashboard.API.Modules.Assistant;

public record AssistantParsedQuery(
    string   RawQuestion,
    string   NormalizedQ,
    DateOnly From,
    DateOnly To,
    bool     DateWasExplicit,
    string?  PersonHint,
    string?  LocationHint,
    string?  TeamLeadHint,
    bool     IsCountQuery,
    bool     IsLowestQuery);
