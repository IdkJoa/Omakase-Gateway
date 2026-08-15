namespace Application.Common.RiskEngine;

// Carries only transport/context data; scoring lives in the rule evaluators.
public sealed record RequestContext
{
    public required string SourceIp { get; init; }

    public string? UserAgent { get; init; }

    public string? UserId { get; init; }

    public string? AcceptLanguage { get; init; }

    public string? AcceptEncoding { get; init; }

    // Resolved from the request path via the HU-009 convention (/{name}/**); null if unresolved.
    public string? ServiceName { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
