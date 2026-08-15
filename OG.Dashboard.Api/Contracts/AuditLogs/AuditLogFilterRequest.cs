namespace OG.Dashboard.Api.Contracts.AuditLogs;

public sealed record AuditLogFilterRequest(
    int Page = 1,
    int PageSize = 25,
    string? Verdict = null,
    string? UserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? SourceIp = null,
    string? ServiceName = null
);
