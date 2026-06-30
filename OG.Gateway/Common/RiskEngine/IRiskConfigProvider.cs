using Domain.Entities;

namespace Application.Common.RiskEngine;

/// <summary>
/// Provides the single global <see cref="RiskScoreConfig"/> row (weights and
/// thresholds). Port implemented in Infrastructure over the DbContext.
/// </summary>
public interface IRiskConfigProvider
{
    Task<RiskScoreConfig> GetAsync(CancellationToken cancellationToken = default);
}
