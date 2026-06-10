using System.Diagnostics;

namespace Application.Common.Telemetry;

/// <summary>
/// ActivitySource centralizado para todos los spans custom del Omakase Gateway.
/// </summary>
/// <remarks>
/// Uso en cualquier HU del Risk Engine:
/// <code>
/// using Activity? span = OmakaseActivity.Source.StartActivity(OmakaseActivity.Spans.PolicyEvaluation);
/// span?.SetTag(OmakaseActivity.Tags.UserId, userId.ToString());
/// </code>
/// El ActivitySource está registrado en ServiceDefaults vía AddSource("OmakaseGateway"),
/// por lo que los spans se exportan automáticamente al OTel Collector → Tempo → Grafana.
/// </remarks>
public static class OmakaseActivity
{
    /// <summary>
    /// Nombre del ActivitySource. Debe coincidir con AddSource() en ServiceDefaults/Extensions.cs.
    /// </summary>
    public const string SourceName = "OmakaseGateway";

    /// <summary>ActivitySource singleton para crear spans.</summary>
    public static readonly ActivitySource Source = new(SourceName, version: "1.0.0");

    /// <summary>
    /// Nombres de span por etapa del pipeline de evaluación de riesgo.
    /// Cada constante corresponde a un span visible en Grafana/Tempo.
    /// </summary>
    public static class Spans
    {
        /// <summary>
        /// Intercepción de la petición HTTP entrante.
        /// Span raíz del pipeline — padre de todos los demás.
        /// Implementado en: HU-007 (middleware de intercepción YARP).
        /// </summary>
        public const string Interception = "gateway.request.intercept";

        /// <summary>
        /// Evaluación del conjunto de políticas deterministas asignadas al servicio.
        /// Padre de uno o más spans PolicyRule.
        /// Implementado en: HU-014 (cálculo del Policy Score).
        /// </summary>
        public const string PolicyEvaluation = "risk.policy.evaluate";

        /// <summary>
        /// Evaluación de una política individual (Geofencing, TimeWindow, Fingerprint, ImpossibleTravel).
        /// Span hijo de PolicyEvaluation.
        /// Implementado en: HU-010, HU-011, HU-012, HU-013.
        /// </summary>
        public const string PolicyRule = "risk.policy.rule";

        /// <summary>
        /// Inferencia del modelo ML.NET para calcular el Anomaly Score.
        /// Implementado en: HU-016 (modelo de comportamiento).
        /// </summary>
        public const string AnomalyInference = "risk.anomaly.infer";

        /// <summary>
        /// Cálculo del Risk Score consolidado (Wp * PolicyScore + Wa * AnomalyScore + ColdStartPenalty)
        /// y emisión del veredicto (Allow / Challenge / Block).
        /// Implementado en: HU-014.
        /// </summary>
        public const string RiskScore = "risk.score.calculate";

        /// <summary>
        /// Escritura del registro inmutable en audit_logs (PostgreSQL).
        /// Implementado en: HU-015 (auditoría).
        /// </summary>
        public const string AuditLog = "audit.log.write";
    }

    /// <summary>
    /// Tags (atributos OTel) estándar para los spans de Omakase.
    /// Usar estos en SetTag() para garantizar nombres consistentes en Grafana.
    /// </summary>
    public static class Tags
    {
        /// <summary>ID del usuario evaluado (UserId.Value.ToString()).</summary>
        public const string UserId = "omakase.user_id";

        /// <summary>Nombre del servicio upstream (ProtectedService.Name).</summary>
        public const string ServiceTarget = "omakase.service";

        /// <summary>Veredicto emitido: Allow | Challenge | Block.</summary>
        public const string Verdict = "omakase.verdict";

        /// <summary>Risk Score final (0–100).</summary>
        public const string RiskScore = "omakase.risk_score";

        /// <summary>Policy Score calculado (0–100).</summary>
        public const string PolicyScore = "omakase.policy_score";

        /// <summary>Anomaly Score calculado (0–100).</summary>
        public const string AnomalyScore = "omakase.anomaly_score";

        /// <summary>True si el usuario está en cold start (sin historial suficiente).</summary>
        public const string IsColdStart = "omakase.is_cold_start";

        /// <summary>IP de origen de la petición (resuelto tras X-Forwarded-For).</summary>
        public const string SourceIp = "omakase.source_ip";

        /// <summary>Tipo de política evaluada (Geofence | TimeWindow | Fingerprint | ImpossibleTravel).</summary>
        public const string PolicyType = "omakase.policy_type";

        /// <summary>Peso de la política en el Policy Score (0–9.999).</summary>
        public const string PolicyWeight = "omakase.policy_weight";
    }
}
