namespace OG.Dashboard.Api.Contracts.Services;

/// <summary>
/// Representa un servicio protegido por el Gateway tal como se expone en la API.
/// Corresponde a la entidad <c>ProtectedService</c> del dominio.
/// </summary>
public sealed record ProtectedServiceDto(
    Guid Id,

    /// <summary>Nombre lógico del servicio; usado como clusterId en YARP.</summary>
    string Name,

    /// <summary>URL del upstream al que se re-enruta el tráfico.</summary>
    string UpstreamUrl,

    /// <summary>
    /// Cuando es true, el Gateway exige un JWT válido antes de evaluar el Risk Score.
    /// </summary>
    bool RequiresAuth,

    /// <summary>Indica si el servicio está activo (soft-delete).</summary>
    bool IsActive,

    DateTimeOffset CreatedAt,

    /// <summary>Número de políticas asociadas a este servicio.</summary>
    int AssociatedPoliciesCount
);
