using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Catalogue of internal services (upstreams) protected by the Gateway.
/// Acts as the source of truth for YARP dynamic-route configuration.
/// <see cref="Name"/> is used as the <c>clusterId</c> in YARP.
/// </summary>
public class ProtectedService : IAuditableEntity
{
    /// <summary>
    /// Nombres reservados al propio Gateway: registrarlos como servicio los dejaría inalcanzables,
    /// porque el prefijo se resuelve antes de llegar al proxy. También es la fuente única de las
    /// rutas que el motor de riesgo deja pasar sin evaluar, para que middleware y motor no diverjan.
    /// </summary>
    public static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth", "health", "alive", "openapi", "demo"
    };

    /// <summary><see cref="ReservedNames"/> como prefijo de ruta, para no recomponer la cadena en cada petición.</summary>
    public static readonly string[] ReservedPathPrefixes =
        [.. ReservedNames.Select(name => "/" + name)];

    public ProtectedServiceId Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string UpstreamUrl { get; set; } = string.Empty;

    /// <summary>Si es true, se exige un JWT válido antes de evaluar el Risk Score.</summary>
    public bool RequiresAuth { get; set; }

    /// <summary>Soft-delete flag; inactive services are excluded from routing.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ServicePolicy> ServicePolicies { get; set; } = new List<ServicePolicy>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
