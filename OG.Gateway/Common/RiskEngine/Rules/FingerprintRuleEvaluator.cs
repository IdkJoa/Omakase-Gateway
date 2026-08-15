using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Common.RiskEngine.Rules;

public sealed class FingerprintRuleEvaluator : IRuleEvaluator
{
    private const decimal CoherentScore = 0m;
    private const decimal PartialViolationScore = 50m;
    private const string RuleName = "FINGERPRINT";
    private readonly TimeSpan _fingerprintTtl = TimeSpan.FromHours(24);

    private readonly IFingerprintService _fingerprintService;
    private readonly IRedisService _redisService;
    private readonly ILogger<FingerprintRuleEvaluator> _logger;

    public FingerprintRuleEvaluator(
        IFingerprintService fingerprintService,
        IRedisService redisService,
        ILogger<FingerprintRuleEvaluator> logger)
    {
        _fingerprintService = fingerprintService;
        _redisService = redisService;
        _logger = logger;
    }

    public PolicyType Type => PolicyType.Fingerprint;

    public async Task<RuleEvaluationResult> EvaluateAsync(
        RequestContext context,
        AccessPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var userId = context.UserId;

        // Sin identidad no se evalúa contra un perfil; se asume coherente para no bloquear
        // accesos públicos legítimos pre-auth.
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new RuleEvaluationResult(
                RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "anonymous_user");
        }

        try
        {
            var currentHash = _fingerprintService.GenerateHash(
                context.UserAgent,
                context.AcceptLanguage,
                context.AcceptEncoding);

            var knownFingerprints = await _redisService.GetFingerprintsAsync(userId);

            if (knownFingerprints.Count == 0)
            {
                _logger.LogInformation("Cold start de huella digital para el usuario {UserId}. Registrando primer dispositivo.", userId);
                await _redisService.StoreFingerprintAsync(userId, currentHash, _fingerprintTtl);
                return new RuleEvaluationResult(
                    RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "cold_start_registered");
            }

            if (knownFingerprints.Contains(currentHash))
            {
                await _redisService.StoreFingerprintAsync(userId, currentHash, _fingerprintTtl);
                return new RuleEvaluationResult(
                    RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "known_device");
            }

            _logger.LogWarning("Dispositivo desconocido detectado para el usuario {UserId}. Se requiere verificación MFA.", userId);
            await _redisService.StoreFingerprintAsync(userId, currentHash, _fingerprintTtl);
            return new RuleEvaluationResult(
                RuleName, PartialViolationScore, policy.Weight, Triggered: true, Detail: "unknown_device");
        }
        catch (Exception ex)
        {
            // Fail-closed parcial: ante fallo de Redis/hashing se asigna score parcial (50)
            // en vez de omitir la protección.
            _logger.LogError(ex, "Fallo crítico en Redis al evaluar huella digital para el usuario {UserId}. Aplicando Fail-Closed parcial.", userId);
            return new RuleEvaluationResult(
                RuleName, PartialViolationScore, policy.Weight, Triggered: true, Detail: "redis_unavailable");
        }
    }
}
