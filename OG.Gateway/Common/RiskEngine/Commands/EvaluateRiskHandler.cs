using Application.Common.Mediator;
using Domain.Entities;

namespace Application.Common.RiskEngine.Commands;

/// <summary>
/// Handler del <see cref="EvaluateRiskCommand"/>.
/// </summary>
/// <remarks>
/// ── HANDLER TEMPORAL (T-015) ──────────────────────────────────────────────────
/// Devuelve siempre <see cref="Verdict.Allow"/> con <see cref="RiskEvaluationResult.RiskScore"/> = 0.
/// T-016 implementará el PolicyScoreCalculator y reemplazará el cuerpo de <see cref="HandleAsync"/>.
/// El contrato <see cref="IRequestHandler{TRequest,TResponse}"/> NO cambia en tareas futuras.
/// ─────────────────────────────────────────────────────────────────────────────
/// </remarks>
public sealed class EvaluateRiskHandler
    : IRequestHandler<EvaluateRiskCommand, RiskEvaluationResult>
{
    /// <inheritdoc/>
    public Task<RiskEvaluationResult> HandleAsync(
        EvaluateRiskCommand request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new RiskEvaluationResult(Verdict.Allow, 0m));
}
