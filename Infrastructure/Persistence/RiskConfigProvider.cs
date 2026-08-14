using Application.Common.RiskEngine;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class RiskConfigProvider : IRiskConfigProvider
{
    private readonly OmakaseDbContext _db;

    public RiskConfigProvider(OmakaseDbContext db) => _db = db;

    public async Task<RiskScoreConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        // OrderBy explícito: lectura determinista y silencia la advertencia de EF por FirstOrDefault sin orden.
        var config = await _db.RiskScoreConfigs.AsNoTracking()
            .OrderBy(r => r.UpdatedAt).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "risk_score_config no tiene filas. Ejecuta el seed (HU-006) antes de evaluar riesgo.");

        return config;
    }
}
