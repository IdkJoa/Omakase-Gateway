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

        // Parsear el string ID a UserId (Typed ID del Dominio)
        if (!Guid.TryParse(userId, out var userGuid))
            return null;

        var domainUserId = new UserId(userGuid);

        // Último acceso CONCEDIDO con geolocalización registrada.
        //
        // El filtro por Verdict.Allow es de seguridad, no cosmético: este punto es el ancla contra la
        // que se mide el Viaje Imposible. Si un intento denegado sirviera de ancla, un atacante podría
        // neutralizar la regla con una petición desechable — la primera desde su ubicación se bloquea
        // pero reubica el ancla, y la segunda ya parece plausible (distancia ~0). Además, un intento
        // rechazado no es evidencia de dónde estuvo el usuario, así que tomarlo como referencia genera
        // falsos positivos contra el usuario legítimo cuando vuelve desde su ubicación habitual.
        //
        // Mismo criterio que HU-017 para el perfil de comportamiento: solo los accesos efectivamente
        // concedidos describen al usuario. Los concedidos tras step-up MFA se auditan ya como Allow
        // (el veredicto se persiste después del ajuste), así que el tráfico legítimo no se pierde.
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

            // Soportar búsquedas de propiedades tanto en PascalCase como en camelCase / lowercase
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
