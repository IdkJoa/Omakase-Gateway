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
/// </summary>
/// <param name="Verdict">Decisión de acceso: Allow, Challenge o Block.</param>
/// <param name="RiskScore">Score de riesgo consolidado (0–100).</param>
public sealed record RiskEvaluationResult(
    Verdict Verdict,
    decimal RiskScore);
