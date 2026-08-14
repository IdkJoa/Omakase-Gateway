namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Vigencia de las cachés de la ruta caliente del motor de riesgo.
/// </summary>
/// <remarks>
/// El motor resolvía en cada evaluación datos que cambian con muy baja frecuencia (políticas, config de riesgo, MFA),
/// llevándose el 83% del presupuesto de evaluación. Cada TTL acota cuánto tarda en verse un cambio administrativo;
/// TTL 0 desactiva la caché correspondiente sin recompilar, útil para descartarla como causa de un comportamiento raro.
/// </remarks>
public sealed class RiskEngineCacheOptions
{
    public const string SectionName = "RiskEngineCache";

    /// <summary>Vigencia del conjunto de políticas por servicio, en segundos. 0 = sin caché.</summary>
    public int ServicePoliciesTtlSeconds { get; set; } = 5;

    /// <summary>Vigencia de la fila única de <c>risk_score_config</c>, en segundos. 0 = sin caché.</summary>
    public int RiskConfigTtlSeconds { get; set; } = 5;

    /// <summary>Vigencia del estado MFA por usuario, en segundos. 0 = sin caché.</summary>
    public int UserMfaInfoTtlSeconds { get; set; } = 5;
}
