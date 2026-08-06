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

    /// <summary>Descriptive name of the policy.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Rule type that determines how <see cref="Config"/> is interpreted.</summary>
    public PolicyType Type { get; set; }

    /// <summary>
    /// JSONB parameters specific to the rule type
    /// (e.g., allowed countries, time windows, travel-speed threshold).
    /// </summary>
    public JsonDocument Config { get; set; } = JsonDocument.Parse("{}");

    /// <summary>Weighting factor of this rule in the Policy Score (0.000–9.999).</summary>
    public decimal Weight { get; set; }

    /// <summary>Soft-delete flag; inactive policies are not evaluated.</summary>
    public bool IsActive { get; set; } = true;

    // ── Foreign Keys ─────────────────────────────────────────────────────────

    /// <summary>ID of the administrator (Security Officer) who created this policy.</summary>
    public UserId CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Última modificación de la política (SRS §7.2). Null mientras no se edite.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation Properties

    public User? CreatedBy { get; set; }
    public ICollection<ServicePolicy> ServicePolicies { get; set; } = new List<ServicePolicy>();
}
