using Application.Common.RiskEngine;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// Reads the single global risk_score_config row (T-029).
/// The seeder (HU-006) guarantees exactly one row exists.
/// </summary>
public sealed class RiskConfigProvider : IRiskConfigProvider
{
    private readonly OmakaseDbContext _db;

    public RiskConfigProvider(OmakaseDbContext db) => _db = db;

    public async Task<RiskScoreConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        // Fila única (singleton): OrderBy explícito para una lectura determinista y silenciar
        // la advertencia de EF sobre FirstOrDefault sin orden (esta consulta corre en cada evaluación).
        var config = await _db.RiskScoreConfigs.AsNoTracking()
            .OrderBy(r => r.UpdatedAt).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "risk_score_config no tiene filas. Ejecuta el seed (HU-006) antes de evaluar riesgo.");

        return config;
    }
}
