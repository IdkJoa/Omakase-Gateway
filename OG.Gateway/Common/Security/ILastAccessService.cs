using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Common.Security;

/// <summary>
/// DTO que representa las coordenadas y la fecha del último acceso de un usuario.
/// </summary>
public sealed record LastAccessResult(
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude);

/// <summary>
/// Contrato para obtener la información geográfica del último acceso del usuario
/// para evaluar anomalías espacio-temporales como viajes imposibles (HU-014 / T-027).
/// </summary>
public interface ILastAccessService
{
    /// <summary>
    /// Obtiene los datos del último acceso del usuario.
    /// Retorna null si el usuario no tiene historial o no se pudo geolocalizar previamente.
    /// </summary>
    Task<LastAccessResult?> GetLastAccessAsync(string userId, CancellationToken cancellationToken = default);
}
