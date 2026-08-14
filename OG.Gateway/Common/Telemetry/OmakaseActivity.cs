using System.Diagnostics;

namespace Application.Common.Telemetry;

/// <remarks>
/// El ActivitySource está registrado en ServiceDefaults vía AddSource("OmakaseGateway"),
/// por lo que los spans se exportan automáticamente al OTel Collector → Tempo → Grafana.
/// </remarks>
public static class OmakaseActivity
{
    public const string SourceName = "OmakaseGateway";

    public static readonly ActivitySource Source = new(SourceName, version: "1.0.0");

    public static class Spans
    {
        public const string Interception = "gateway.request.intercept";

        public const string PolicyEvaluation = "risk.policy.evaluate";

        public const string PolicyRule = "risk.policy.rule";

        public const string AnomalyInference = "risk.anomaly.infer";

        public const string RiskScore = "risk.score.calculate";

        public const string AuditLog = "audit.log.write";
    }

    public static class Tags
    {
        public const string UserId = "omakase.user_id";

        public const string ServiceTarget = "omakase.service";

        public const string Verdict = "omakase.verdict";

        public const string RiskScore = "omakase.risk_score";

        public const string PolicyScore = "omakase.policy_score";

        public const string AnomalyScore = "omakase.anomaly_score";

        public const string IsColdStart = "omakase.is_cold_start";

        public const string SourceIp = "omakase.source_ip";

        public const string PolicyType = "omakase.policy_type";

        public const string PolicyWeight = "omakase.policy_weight";
    }
}
