using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Common.RiskEngine.Rules;

/// <summary>
/// Evaluador de la regla determinista Fingerprint Validation (HU-013 / T-025).
/// Compara la huella digital criptográfica generada en base a las cabeceras HTTP del cliente
/// con el conjunto de huellas conocidas del usuario guardadas en Redis (fingerprint:{userId}).
/// </summary>
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

        // Si no hay UserId (usuario anónimo), la regla no se puede evaluar contra un perfil.
        // Se asume que no genera score de violación parcial para no bloquear accesos públicos legítimos pre-auth.
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new RuleEvaluationResult(
                RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "anonymous_user");
        }

        try
        {
            // Generar el hash SHA-256 de la huella digital del cliente actual
            var currentHash = _fingerprintService.GenerateHash(
                context.UserAgent,
                context.AcceptLanguage,
                context.AcceptEncoding);

            // Obtener el SET de huellas digitales de dispositivos conocidos en Redis
            var knownFingerprints = await _redisService.GetFingerprintsAsync(userId);

            // Escenario 1: Arranque en frío (Cold Start)
            // Si el usuario no tiene ninguna huella registrada en Redis, se registra la primera y se confía.
            if (knownFingerprints.Count == 0)
            {
                _logger.LogInformation("Cold start de huella digital para el usuario {UserId}. Registrando primer dispositivo.", userId);
                await _redisService.StoreFingerprintAsync(userId, currentHash, _fingerprintTtl);
                return new RuleEvaluationResult(
                    RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "cold_start_registered");
            }

            // Escenario 2: Dispositivo conocido
            if (knownFingerprints.Contains(currentHash))
            {
                // Refrescar el TTL de expiración en Redis sobre la clave del SET
                await _redisService.StoreFingerprintAsync(userId, currentHash, _fingerprintTtl);
                return new RuleEvaluationResult(
                    RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: "known_device");
            }

            // Escenario 3: Dispositivo desconocido
            // Si el SET no está vacío pero la huella no coincide, se considera anomalía.
            // Se registra la nueva huella y se devuelve score parcial (50) para requerir MFA (Challenge).
            _logger.LogWarning("Dispositivo desconocido detectado para el usuario {UserId}. Se requiere verificación MFA.", userId);
            await _redisService.StoreFingerprintAsync(userId, currentHash, _fingerprintTtl);
            return new RuleEvaluationResult(
                RuleName, PartialViolationScore, policy.Weight, Triggered: true, Detail: "unknown_device");
        }
        catch (Exception ex)
        {
            // Fail-Closed Parcial (Módulo 9): En caso de fallo crítico en Redis o hashing,
            // asigna score parcial de riesgo (50) en lugar de omitir la protección.
            _logger.LogError(ex, "Fallo crítico en Redis al evaluar huella digital para el usuario {UserId}. Aplicando Fail-Closed parcial.", userId);
            return new RuleEvaluationResult(
                RuleName, PartialViolationScore, policy.Weight, Triggered: true, Detail: "redis_unavailable");
        }
    }
}
