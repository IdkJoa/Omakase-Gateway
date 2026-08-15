using System.Text.Json;
using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Deterministic access policy configured by administrators.
/// The <see cref="Config"/> JSONB field stores rule-specific parameters
/// (e.g., allowed countries for GEOFENCE, time windows for TIME_WINDOW).
/// </summary>
public class AccessPolicy : IAuditableEntity
{
    public AccessPolicyId Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public PolicyType Type { get; set; }
    public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

    /// <summary>Weighting factor of this rule in the Policy Score (0.000–9.999).</summary>
    public decimal Weight { get; set; }

    /// <summary>Soft-delete flag; inactive policies are not evaluated.</summary>
    public bool IsActive { get; set; } = true;

    public UserId CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public User? CreatedBy { get; set; }
    public ICollection<ServicePolicy> ServicePolicies { get; set; } = new List<ServicePolicy>();
}
