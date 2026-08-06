namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Vigencia de las cachés de la ruta caliente del motor de riesgo (T-072).
/// </summary>
/// <remarks>
/// El motor resuelve en CADA evaluación datos que cambian con muy baja frecuencia: las políticas
/// del servicio, la configuración de riesgo y el estado MFA del usuario. Medido con 1.000 muestras
/// bajo 100 VUs sostenidos, ese I/O se llevaba el <b>83 %</b> del presupuesto de evaluación
/// (políticas 27,4 %, step-up 25,7 %, perfil 16,8 %, config 12,9 %) y dejaba el p95 en ~96 ms
/// frente a los 50 ms del requisito.
/// <para>
/// Cada TTL acota la ventana en la que un cambio administrativo tarda en verse. Con el valor por
/// defecto de 5 s, un alta o baja de política surte efecto en la siguiente evaluación pasados como
/// mucho 5 segundos, lo que sigue satisfaciendo el criterio de HU-023/HU-025 («las siguientes
/// evaluaciones usan el nuevo valor») sin redespliegue ni invalidación explícita.
/// </para>
/// <para>
/// <b>Válvula de seguridad:</b> un TTL de <c>0</c> desactiva por completo esa caché y el decorador
/// delega siempre en la implementación real. Permite descartar la caché como causa de un
/// comportamiento raro sin recompilar.
/// </para>
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
