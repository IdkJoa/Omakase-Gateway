namespace Application.Common.RiskEngine;

/// <summary>
/// Immutable context extracted from an intercepted request (SRS RF-M1).
/// Shared input for every deterministic rule evaluator (RF-M2).
/// Carries only transport/context data; scoring lives in the evaluators.
/// </summary>
public sealed record RequestContext
{
    /// <summary>Source IP address of the request (IPv4/IPv6).</summary>
    public required string SourceIp { get; init; }

    /// <summary>Sanitised User-Agent header, when present.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Identifier of the authenticated actor, when resolved.</summary>
    public string? UserId { get; init; }

    /// <summary>Accept-Language header value from client request, when present.</summary>
    public string? AcceptLanguage { get; init; }

    /// <summary>Accept-Encoding header value from client request, when present.</summary>
    public string? AcceptEncoding { get; init; }

    /// <summary>Instant at which the request was intercepted.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
