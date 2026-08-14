using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine.Rules.Helpers;
using Application.Common.Security;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Common.RiskEngine.Rules;

public sealed class ImpossibleTravelRuleEvaluator : IRuleEvaluator
{
    private const decimal CoherentScore = 0m;
    private const decimal SevereScore = 100m;
    private const string RuleName = "IMPOSSIBLE_TRAVEL";
    private const double MaxVelocityKmh = 900.0;

    private readonly IGeoLocationService _geoLocationService;
    private readonly ILastAccessService _lastAccessService;
    private readonly ILogger<ImpossibleTravelRuleEvaluator> _logger;

    public ImpossibleTravelRuleEvaluator(
        IGeoLocationService geoLocationService,
        ILastAccessService lastAccessService,
        ILogger<ImpossibleTravelRuleEvaluator> logger)
    {
        _geoLocationService = geoLocationService;
        _lastAccessService = lastAccessService;
        _logger = logger;
    }

    public PolicyType Type => PolicyType.ImpossibleTravel;

    public async Task<RuleEvaluationResult> EvaluateAsync(
        RequestContext context,
        AccessPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var userId = context.UserId;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new RuleEvaluationResult(
                RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "anonymous_user");
        }

        try
        {
            var currentGeoResult = await _geoLocationService.ResolveAsync(context.SourceIp, cancellationToken);
            if (currentGeoResult.IsFailure)
            {
                _logger.LogWarning("Geolocalización fallida para la IP actual {Ip} en regla Viaje Imposible. Omitiendo regla.", context.SourceIp);
                return new RuleEvaluationResult(
                    RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "current_geo_unavailable");
            }

            var currentGeo = currentGeoResult.Value;

            var lastAccess = await _lastAccessService.GetLastAccessAsync(userId, cancellationToken);
            if (lastAccess is null)
            {
                return new RuleEvaluationResult(
                    RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "no_history");
            }

            var distance = Haversine.Distance(
                lastAccess.Latitude, lastAccess.Longitude,
                currentGeo.Latitude, currentGeo.Longitude);

            var timeDelta = context.Timestamp - lastAccess.Timestamp;

            // Mínimo de 1 segundo para evitar división por cero en peticiones concurrentes.
            var seconds = Math.Max(timeDelta.TotalSeconds, 1.0);
            var hours = seconds / 3600.0;

            var speed = distance / hours;

            if (speed > MaxVelocityKmh)
            {
                _logger.LogWarning(
                    "Viaje Imposible DETECTADO para usuario {UserId}. Distancia: {Distance:F1} km, Tiempo: {Time} min, Velocidad: {Speed:F1} km/h (Límite: {Limit} km/h)",
                    userId, distance, timeDelta.TotalMinutes, speed, MaxVelocityKmh);

                return new RuleEvaluationResult(
                    RuleName,
                    SevereScore,
                    policy.Weight,
                    Triggered: true,
                    Detail: $"impossible_travel_detected: distance={distance:F1}km, speed={speed:F1}km/h");
            }

            return new RuleEvaluationResult(
                RuleName,
                CoherentScore,
                policy.Weight,
                Triggered: false,
                Detail: $"travel_plausible: distance={distance:F1}km, speed={speed:F1}km/h");
        }
        catch (Exception ex)
        {
            // Score coherente ante fallo para evitar falsos positivos de bloqueo; el error queda
            // registrado en logs/OTel.
            _logger.LogError(ex, "Error inesperado al evaluar la regla Viaje Imposible para el usuario {UserId}.", userId);
            return new RuleEvaluationResult(
                RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "evaluation_failed");
        }
    }
}
