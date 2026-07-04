using System.Text.Json;
using Application.Common.Mediator;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Application.Common.Security;
using Domain.Entities;

namespace Application.Common.RiskEngine.Commands;

/// <summary>
/// Handler del <see cref="EvaluateRiskCommand"/> — motor de riesgo (HU-015 / T-028, T-029, T-030).
/// </summary>
/// <remarks>
/// Flujo: resuelve el servicio destino (nombre del path, HU-009) y sus políticas → corre las reglas
/// aplicables (<see cref="IRuleEvaluator"/> por <c>PolicyType</c>) → Policy Score ponderado (T-028) →
/// Anomaly Score (stub, Sprint 3 = ML.NET) → Risk Score + veredicto (T-029). Devuelve además el
/// desglose de auditoría (geo, reglas disparadas, service_id) que el middleware persiste (T-030).
/// <para>Open/Closed: agregar una regla nueva = registrar un <see cref="IRuleEvaluator"/>; este handler no cambia.</para>
/// <para>Cold-start (T-034) fijo en 0 en Sprint 2: sin perfiles se evalúa con <c>access_count = N</c>
/// (penalización 0). La fórmula queda completa y se activa cuando los perfiles existan en Sprint 3.</para>
/// </remarks>
public sealed class EvaluateRiskHandler
    : IRequestHandler<EvaluateRiskCommand, RiskEvaluationResult>
{
    private readonly IServicePolicyProvider _policyProvider;
    private readonly IEnumerable<IRuleEvaluator> _evaluators;
    private readonly IPolicyScoreCalculator _policyCalculator;
    private readonly IRiskScoreConsolidator _consolidator;
    private readonly IRiskConfigProvider _configProvider;
    private readonly IAnomalyDetector _anomalyDetector;
    private readonly IGeoLocationService _geoLocation;
    private readonly IUserProfileStore _profileStore;
    private readonly IProfileUpdateChannel _profileChannel;

    public EvaluateRiskHandler(
        IServicePolicyProvider policyProvider,
        IEnumerable<IRuleEvaluator> evaluators,
        IPolicyScoreCalculator policyCalculator,
        IRiskScoreConsolidator consolidator,
        IRiskConfigProvider configProvider,
        IAnomalyDetector anomalyDetector,
        IGeoLocationService geoLocation,
        IUserProfileStore profileStore,
        IProfileUpdateChannel profileChannel)
    {
        _policyProvider = policyProvider;
        _evaluators = evaluators;
        _policyCalculator = policyCalculator;
        _consolidator = consolidator;
        _configProvider = configProvider;
        _anomalyDetector = anomalyDetector;
        _geoLocation = geoLocation;
        _profileStore = profileStore;
        _profileChannel = profileChannel;
    }

    /// <inheritdoc/>
    public async Task<RiskEvaluationResult> HandleAsync(
        EvaluateRiskCommand request,
        CancellationToken cancellationToken = default)
    {
        var context = request.Context;
        var config = await _configProvider.GetAsync(cancellationToken);

        // 1. Resolver servicio destino (por nombre del path, HU-009) y correr sus reglas.
        Guid? serviceId = null;
        var ruleResults = new List<RuleEvaluationResult>();

        if (!string.IsNullOrWhiteSpace(context.ServiceName))
        {
            var set = await _policyProvider.GetByServiceNameAsync(context.ServiceName, cancellationToken);
            if (set is not null)
            {
                serviceId = set.ServiceId.Value;
                foreach (var policy in set.Policies)
                {
                    var evaluator = _evaluators.FirstOrDefault(e => e.Type == policy.Type);
                    if (evaluator is null)
                        continue;

                    ruleResults.Add(await evaluator.EvaluateAsync(context, policy, cancellationToken));
                }
            }
        }

        // 2. Policy Score ponderado (T-028).
        var policyScore = _policyCalculator.Calculate(ruleResults);

        // 3. Anomaly Score (modelo ML.NET real; 50 si sin identidad/cold-start).
        var anomalyScore = await _anomalyDetector.GetAnomalyScoreAsync(context, cancellationToken);

        // 4. Risk Score + veredicto (T-029) con el access_count real del perfil (T-034 / HU-017).
        var accessCount = await ResolveAccessCountAsync(context, config, cancellationToken);
        var consolidated = _consolidator.Consolidate(policyScore, anomalyScore, accessCount, config);

        // 5. Alimentar el perfil de forma asíncrona (T-033 + base_risk_penalty T-034): fuera de la ruta crítica.
        EnqueueProfileUpdate(context, consolidated, config);

        // 6. Desglose de auditoría (T-030): geo + reglas disparadas.
        var geoJson = await BuildGeoAsync(context.SourceIp, cancellationToken);
        var triggeredJson = BuildTriggeredRules(ruleResults);

        return new RiskEvaluationResult(
            consolidated.Verdict,
            consolidated.RiskScore,
            policyScore,
            anomalyScore,
            geoJson,
            triggeredJson,
            serviceId);
    }

    /// <summary>
    /// access_count real para la penalización de cold-start (T-034 / HU-017): del perfil si hay identidad
    /// (0 si el usuario es nuevo → penalización máxima); sin identidad, N (penalización 0, no aplica por usuario).
    /// </summary>
    private async Task<int> ResolveAccessCountAsync(RequestContext context, RiskScoreConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.UserId))
            return config.ColdStartN;

        var profile = await _profileStore.GetAsync(context.UserId, cancellationToken);
        return profile?.AccessCount ?? 0;
    }

    /// <summary>Encola la actualización del perfil (T-033 + base_risk_penalty T-034). Solo con identidad; fire-and-forget.</summary>
    private void EnqueueProfileUpdate(RequestContext context, ConsolidatedRisk consolidated, RiskScoreConfig config)
    {
        if (string.IsNullOrWhiteSpace(context.UserId))
            return;

        _profileChannel.TryWrite(new ProfileUpdate(
            context.UserId,
            context.Timestamp,
            context.ServiceName ?? string.Empty,
            consolidated.ColdStartPenalty,
            config.ColdStartN));
    }

    /// <summary>Resuelve la geolocalización y la serializa a JSON para el audit (clave para HU-014).</summary>
    private async Task<JsonDocument?> BuildGeoAsync(string sourceIp, CancellationToken cancellationToken)
    {
        var geo = await _geoLocation.ResolveAsync(sourceIp, cancellationToken);
        if (geo.IsFailure)
            return null;

        return JsonSerializer.SerializeToDocument(new
        {
            country = geo.Value.CountryCode,
            city = geo.Value.City,
            latitude = geo.Value.Latitude,
            longitude = geo.Value.Longitude,
        });
    }

    /// <summary>Serializa las reglas disparadas con su score parcial (triggered_rules JSONB).</summary>
    private static JsonDocument BuildTriggeredRules(IReadOnlyList<RuleEvaluationResult> ruleResults)
    {
        var triggered = ruleResults
            .Where(r => r.Triggered)
            .Select(r => new { rule = r.RuleName, score = r.Score, detail = r.Detail })
            .ToArray();

        return JsonSerializer.SerializeToDocument(triggered);
    }
}
