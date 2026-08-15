using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// Servicio de persistencia para consultar el último acceso del usuario en la base de datos de auditoría (HU-014 / T-027).
/// </summary>
public sealed class LastAccessService : ILastAccessService
{
    private readonly OmakaseDbContext _dbContext;

    public LastAccessService(OmakaseDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LastAccessResult?> GetLastAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        if (!Guid.TryParse(userId, out var userGuid))
            return null;

        var domainUserId = new UserId(userGuid);

        // Solo accesos con Verdict.Allow: es el ancla del Viaje Imposible, y un intento denegado permitiría
        // a un atacante reubicar el ancla con una petición desechable (o generaría falsos positivos legítimos).
        var lastLog = await _dbContext.AuditLogs
            .Where(a => a.UserId == domainUserId && a.Geo != null && a.Verdict == Verdict.Allow)
            .OrderByDescending(a => a.EvaluatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastLog?.Geo is null)
            return null;

        try
        {
            var root = lastLog.Geo.RootElement;
            double? lat = null;
            double? lon = null;

            if (root.TryGetProperty("Latitude", out var latProp) || root.TryGetProperty("latitude", out latProp))
            {
                lat = latProp.GetDouble();
            }
            if (root.TryGetProperty("Longitude", out var lonProp) || root.TryGetProperty("longitude", out lonProp))
            {
                lon = lonProp.GetDouble();
            }

            if (lat.HasValue && lon.HasValue)
            {
                return new LastAccessResult(lastLog.EvaluatedAt, lat.Value, lon.Value);
            }
        }
        catch
        {
            // Fail-safe silencioso si el JSON de base de datos viene con formato corrupto.
        }

        return null;
    }
}
