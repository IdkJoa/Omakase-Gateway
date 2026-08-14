using System;
using System.Text.Json;
using Application.Common.Mediator;
using Domain.Entities;

namespace Application.Common.RiskEngine.Commands;

public sealed record EvaluateRiskCommand(RequestContext Context)
    : IRequest<RiskEvaluationResult>;

// AuthenticationRequired: precondición fallida (SRS §7.5) — el servicio exige JWT y la petición
// llegó sin identidad; el middleware responde 401 en vez del veredicto de riesgo.
// Timings es null cuando la evaluación se cortó antes de empezar (precondición de autenticación).
public sealed record RiskEvaluationResult(
    Verdict Verdict,
    decimal RiskScore,
    decimal PolicyScore,
    decimal AnomalyScore,
    JsonDocument? Geo,
    JsonDocument? TriggeredRules,
    Guid? ServiceId,
    bool AuthenticationRequired = false,
    EvaluationTimings? Timings = null);

// Desglose por fase (T-072): el presupuesto es <=50ms p95 y un total único no dice dónde se va
// el tiempo entre las consultas a PostgreSQL, Redis e inferencia ML.NET.
public sealed record EvaluationTimings(
    double ConfigMs,
    double PoliciesMs,
    double RulesMs,
    double GeoCheckMs,
    double AnomalyMs,
    double AccessCountMs,
    double StepUpMs,
    double AuditBuildMs);
