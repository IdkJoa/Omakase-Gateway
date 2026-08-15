using System.Text.Json;
using Application.Common.Mediator;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Application.Common.Security;
using Application.Common.Security.Mfa;
using Domain.Entities;

namespace Application.Common.RiskEngine.Commands;

// Open/Closed: agregar una regla nueva = registrar un IRuleEvaluator; este handler no cambia.
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
    private readonly IStepUpStore _stepUpStore;
    private readonly IUserMfaInfoProvider _userMfaInfo;
    private readonly IFingerprintService _fingerprintService;

    public EvaluateRiskHandler(
        IServicePolicyProvider policyProvider,
        IEnumerable<IRuleEvaluator> evaluators,
        IPolicyScoreCalculator policyCalculator,
        IRiskScoreConsolidator consolidator,
        IRiskConfigProvider configProvider,
        IAnomalyDetector anomalyDetector,
        IGeoLocationService geoLocation,
        IUserProfileStore profileStore,
        IProfileUpdateChannel profileChannel,
        IStepUpStore stepUpStore,
        IUserMfaInfoProvider userMfaInfo,
        IFingerprintService fingerprintService)
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
        _stepUpStore = stepUpStore;
        _userMfaInfo = userMfaInfo;
        _fingerprintService = fingerprintService;
    }

    public async Task<RiskEvaluationResult> HandleAsync(
        EvaluateRiskCommand request,
        CancellationToken cancellationToken = default)
    {
        var context = request.Context;

        // Desglose de coste por fase (T-072): sin él, un p95 fuera de presupuesto no dice DÓNDE.
        var mark = System.Diagnostics.Stopwatch.GetTimestamp();
        double configMs = 0, policiesMs = 0, rulesMs = 0, geoCheckMs = 0,
               anomalyMs = 0, accessCountMs = 0, stepUpMs = 0, auditBuildMs = 0;

        var config = await _configProvider.GetAsync(cancellationToken);
        configMs = Lap(ref mark);

        Guid? serviceId = null;
        var ruleResults = new List<RuleEvaluationResult>();

        if (!string.IsNullOrWhiteSpace(context.ServiceName))
        {
            var set = await _policyProvider.GetByServiceNameAsync(context.ServiceName, cancellationToken);
            policiesMs = Lap(ref mark);

            if (set is not null)
            {
                serviceId = set.ServiceId.Value;

                // SRS §7.5 (HU-024): si el servicio exige autenticación (requires_auth) y la petición
                // no trae identidad resuelta, se deniega ANTES de evaluar — no se corren reglas ni ML.
                // El middleware traduce la bandera a 401 AUTHENTICATION_REQUIRED (no 403 de Block).
                if (set.RequiresAuth && string.IsNullOrWhiteSpace(context.UserId))
                    return AuthenticationRequiredResult(serviceId);

                foreach (var policy in set.Policies)
                {
                    var evaluator = _evaluators.FirstOrDefault(e => e.Type == policy.Type);
                    if (evaluator is null)
                        continue;

                    ruleResults.Add(await evaluator.EvaluateAsync(context, policy, cancellationToken));
                }

                rulesMs = Lap(ref mark);
            }
        }

        mark = System.Diagnostics.Stopwatch.GetTimestamp();

        bool geoDegraded = false;
        try
        {
            var geoCheck = await _geoLocation.ResolveAsync(context.SourceIp, cancellationToken);
            if (geoCheck.IsFailure)
            {
                geoDegraded = true;
                var activity = System.Diagnostics.Activity.Current;
                if (activity != null)
                {
                    activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Dependency failure: GeoLocation");
                    activity.SetTag("error", true);
                    activity.SetTag("dependency.name", "GeoLocation");
                }
            }
        }
        catch (Exception)
        {
            geoDegraded = true;
            var activity = System.Diagnostics.Activity.Current;
            if (activity != null)
            {
                activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Dependency failure: GeoLocation");
                activity.SetTag("error", true);
                activity.SetTag("dependency.name", "GeoLocation");
            }
        }

        geoCheckMs = Lap(ref mark);

        var policyScore = _policyCalculator.Calculate(ruleResults);
        if (geoDegraded)
        {
            // Degradación por dependencia caída (HU-032): penaliza en vez de fallar la evaluación.
            policyScore = Math.Min(100m, policyScore + 15m);
        }

        decimal anomalyScore;
        bool mlDegraded = false;
        try
        {
            anomalyScore = await _anomalyDetector.GetAnomalyScoreAsync(context, cancellationToken);
        }
        catch (Exception)
        {
            anomalyScore = 50m;
            mlDegraded = true;

            var activity = System.Diagnostics.Activity.Current;
            if (activity != null)
            {
                activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Dependency failure: ML.NET");
                activity.SetTag("error", true);
                activity.SetTag("dependency.name", "ML.NET");
            }
        }

        anomalyMs = Lap(ref mark);

        var accessCount = await ResolveAccessCountAsync(context, config, cancellationToken);
        var consolidated = _consolidator.Consolidate(policyScore, anomalyScore, accessCount, config);
        accessCountMs = Lap(ref mark);

        var (adjusted, mfaRules) = await ApplyStepUpPolicyAsync(context, consolidated, cancellationToken);
        consolidated = adjusted;
        stepUpMs = Lap(ref mark);

        EnqueueProfileUpdate(context, consolidated, config);

        var geoJson = await BuildGeoAsync(context.SourceIp, cancellationToken);
        var triggeredJson = BuildTriggeredRules(ruleResults, mfaRules, geoDegraded, mlDegraded);
        auditBuildMs = Lap(ref mark);

        return new RiskEvaluationResult(
            consolidated.Verdict,
            consolidated.RiskScore,
            policyScore,
            anomalyScore,
            geoJson,
            triggeredJson,
            serviceId,
            AuthenticationRequired: false,
            Timings: new EvaluationTimings(
                configMs, policiesMs, rulesMs, geoCheckMs,
                anomalyMs, accessCountMs, stepUpMs, auditBuildMs));
    }

    // Usa timestamps crudos para no asignar un Stopwatch por fase en la ruta caliente.
    private static double Lap(ref long mark)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(mark, now).TotalMilliseconds;
        mark = now;
        return ms;
    }

    // AuthenticationRequired hace que el middleware responda 401 en vez del 403 de Block.
    private static RiskEvaluationResult AuthenticationRequiredResult(Guid? serviceId)
    {
        var triggered = JsonSerializer.SerializeToDocument(new[]
        {
            new
            {
                rule = "AUTH_REQUIRED",
                score = 100m,
                detail = (string?)"el servicio exige autenticacion (requires_auth) y la peticion no trae identidad",
            }
        });

        return new RiskEvaluationResult(
            Verdict.Block,
            RiskScore: 100m,
            PolicyScore: 0m,
            AnomalyScore: 0m,
            Geo: null,
            TriggeredRules: triggered,
            ServiceId: serviceId,
            AuthenticationRequired: true);
    }

    // Sin identidad se usa config.ColdStartN (penalización 0, no aplica por usuario); con identidad,
    // 0 accesos significa usuario nuevo -> penalización máxima de cold-start.
    private async Task<int> ResolveAccessCountAsync(RequestContext context, RiskScoreConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.UserId))
            return config.ColdStartN;

        var profile = await _profileStore.GetAsync(context.UserId, cancellationToken);
        return profile?.AccessCount ?? 0;
    }

    private void EnqueueProfileUpdate(RequestContext context, ConsolidatedRisk consolidated, RiskScoreConfig config)
    {
        if (string.IsNullOrWhiteSpace(context.UserId))
            return;

        if (consolidated.Verdict != Verdict.Allow)
            return;

        _profileChannel.TryWrite(new ProfileUpdate(
            context.UserId,
            context.Timestamp,
            context.ServiceName ?? string.Empty,
            consolidated.ColdStartPenalty,
            config.ColdStartN));
    }

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

    // Clientes no interactivos no pueden completar el desafío MFA, así que se escala a Block
    // (fail-closed) en vez de dejarlos atascados en Challenge.
    private async Task<(ConsolidatedRisk Risk, IReadOnlyList<(string Rule, string Detail)> MfaRules)> ApplyStepUpPolicyAsync(
        RequestContext context, ConsolidatedRisk consolidated, CancellationToken cancellationToken)
    {
        if (consolidated.Verdict != Verdict.Challenge || string.IsNullOrWhiteSpace(context.UserId))
            return (consolidated, []);

        var mfaInfo = await _userMfaInfo.GetAsync(context.UserId, cancellationToken);
        if (mfaInfo is { IsInteractive: false })
        {
            System.Diagnostics.Activity.Current?.AddEvent(
                new System.Diagnostics.ActivityEvent("mfa.challenge.non_interactive_escalated"));
            return (consolidated with { Verdict = Verdict.Block }, new[]
            {
                (MfaRuleNames.Failed, "cliente no interactivo: step-up no completable"),
                (MfaRuleNames.NonInteractiveEscalated, "Challenge escalado a Block (fail-closed)"),
            });
        }

        var stepUp = await _stepUpStore.GetAsync(context.UserId);
        if (stepUp is not null && StepUpMatchesDevice(stepUp, context))
        {
            System.Diagnostics.Activity.Current?.AddEvent(
                new System.Diagnostics.ActivityEvent("mfa.stepup.satisfied"));
            return (consolidated with { Verdict = Verdict.Allow }, new[]
            {
                (MfaRuleNames.Satisfied, "step-up TOTP vigente: Challenge degradado a Allow"),
            });
        }

        return (consolidated, []);
    }

    // El step-up queda ligado a la huella del dispositivo que completó el desafío, para que otro
    // dispositivo no pueda reutilizarlo.
    private bool StepUpMatchesDevice(StepUpData stepUp, RequestContext context)
    {
        if (string.IsNullOrEmpty(stepUp.FingerprintHash))
            return true;

        var currentHash = _fingerprintService.GenerateHash(
            context.UserAgent, context.AcceptLanguage, context.AcceptEncoding);

        return string.Equals(stepUp.FingerprintHash, currentHash, StringComparison.Ordinal);
    }

    private static JsonDocument BuildTriggeredRules(
        IReadOnlyList<RuleEvaluationResult> ruleResults,
        IReadOnlyList<(string Rule, string Detail)> mfaRules,
        bool geoDegraded = false,
        bool mlDegraded = false)
    {
        var triggered = ruleResults
            .Where(r => r.Triggered)
            .Select(r => new { rule = r.RuleName, score = r.Score, detail = (string?)r.Detail })
            .ToList();

        if (geoDegraded)
        {
            triggered.Add(new { rule = "SERVICE_DEGRADATION", score = 15m, detail = (string?)"geolocalizacion_inoperativa" });
        }

        if (mlDegraded)
        {
            triggered.Add(new { rule = "SERVICE_DEGRADATION", score = 0m, detail = (string?)"ml_net_inoperativo" });
        }

        foreach (var (rule, detail) in mfaRules)
            triggered.Add(new { rule, score = 0m, detail = (string?)detail });

        return JsonSerializer.SerializeToDocument(triggered);
    }
}