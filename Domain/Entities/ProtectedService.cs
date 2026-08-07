using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Catalogue of internal services (upstreams) protected by the Gateway.
/// Acts as the source of truth for YARP dynamic-route configuration.
/// <see cref="Name"/> is used as the <c>clusterId</c> in YARP.
/// </summary>
public class ProtectedService
{
    /// <summary>
    /// Primeros segmentos de ruta que pertenecen al propio Gateway y no pueden nombrar un
    /// servicio protegido: registrar uno con estos nombres lo dejaría inalcanzable, porque el
    /// prefijo se resuelve antes de llegar al proxy.
    /// <para>
    /// Es también la fuente única de las rutas que el motor de riesgo deja pasar sin evaluar.
    /// Mantener una segunda lista en el middleware invitaba a que ambas divergieran, y una
    /// divergencia aquí significa o bien evaluar el endpoint de autenticación (que no tiene
    /// identidad todavía) o bien dejar sin evaluar algo que sí debía pasar por el motor.
    /// </para>
    /// </summary>
    public static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth", "health", "alive", "openapi", "demo"
    };

    /// <summary>
    /// <see cref="ReservedNames"/> en forma de prefijo de ruta, para comparar contra
    /// <c>HttpRequest.Path</c> sin recomponer la cadena en cada petición.
    /// </summary>
    public static readonly string[] ReservedPathPrefixes =
        [.. ReservedNames.Select(name => "/" + name)];

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
