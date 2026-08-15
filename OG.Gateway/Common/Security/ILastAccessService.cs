using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Common.Security;

public sealed record LastAccessResult(
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude);

public interface ILastAccessService
{
    // Null si el usuario no tiene historial o no se pudo geolocalizar previamente.
    Task<LastAccessResult?> GetLastAccessAsync(string userId, CancellationToken cancellationToken = default);
}
