using System;
using System.Text.Json;
using Application.Common.Mediator;
using Domain.Entities;

namespace Application.Common.RiskEngine.Commands;

/// <summary>
/// Command que transporta el contexto de una petición interceptada al motor de riesgo.
/// Despachado por <see cref="Middlewares.RiskEvaluationMiddleware"/> vía <see cref="IMediator"/>.
/// </summary>
/// <param name="Context">Contexto inmutable extraído de la petición HTTP.</param>
public sealed record EvaluateRiskCommand(RequestContext Context)
    : IRequest<RiskEvaluationResult>;

/// <summary>
/// Resultado producido por el motor de riesgo para una petición interceptada.
/// Incluye el desglose necesario para la auditoría (T-030).
/// </summary>
/// <param name="Verdict">Decisión de acceso: Allow, Challenge o Block.</param>
/// <param name="RiskScore">Score de riesgo consolidado (0–100).</param>
/// <param name="PolicyScore">Score de la capa determinista (0–100).</param>
/// <param name="AnomalyScore">Score de la capa de anomalías (0–100).</param>
/// <param name="Geo">Geolocalización del origen como JSON, o null si no se resolvió.</param>
/// <param name="TriggeredRules">Reglas disparadas con su score parcial, como JSON.</param>
/// <param name="ServiceId">Id del servicio destino evaluado, o null si no se resolvió.</param>
public sealed record RiskEvaluationResult(
    Verdict Verdict,
    decimal RiskScore,
    decimal PolicyScore,
    decimal AnomalyScore,
    JsonDocument? Geo,
    JsonDocument? TriggeredRules,
    Guid? ServiceId);
