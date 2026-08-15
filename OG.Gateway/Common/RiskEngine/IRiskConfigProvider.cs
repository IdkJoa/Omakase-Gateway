using Domain.Entities;

namespace Application.Common.RiskEngine;

public interface IRiskConfigProvider
{
    Task<RiskScoreConfig> GetAsync(CancellationToken cancellationToken = default);
}
