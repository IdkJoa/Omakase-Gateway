using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Catalogue of internal services (upstreams) protected by the Gateway.
/// Acts as the source of truth for YARP dynamic-route configuration.
/// <see cref="Name"/> is used as the <c>clusterId</c> in YARP.
/// </summary>
public class ProtectedService
{
    public ProtectedServiceId Id { get; init; }

    /// <summary>Logical name, used as the YARP cluster ID.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target URL for upstream re-routing.</summary>
    public string UpstreamUrl { get; set; } = string.Empty;

    /// <summary>
    /// When true, a valid JWT must be present before the Risk Score is evaluated.
    /// </summary>
    public bool RequiresAuth { get; set; }

    /// <summary>Soft-delete flag; inactive services are excluded from routing.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // ── Navigation Properties ─────────────────────────────────────────────────

    public ICollection<ServicePolicy> ServicePolicies { get; set; } = new List<ServicePolicy>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
