using Domain.Common;
using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OG.Dashboard.Features.Services;

public sealed class GetProtectedServiceHandler(OmakaseDbContext context, ILogger<GetProtectedServiceHandler> logger)
{
    public async Task<Result<ProtectedService>> GetProtectedServiceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Iniciando obtención del servicio protegido con ID: {Id}", id);
        try
        {
            var serviceId = new Domain.ValueObjects.ProtectedServiceId(id);
            var service = await context.ProtectedServices
                .AsNoTracking()
                .Include(s => s.ServicePolicies)
                .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

            if (service is null)
            {
                logger.LogWarning("No se encontró el servicio protegido con ID: {Id}", id);
                return Result.Failure<ProtectedService>(new Error("ProtectedService.NotFound", $"Servicio '{id}' no encontrado."));
            }

            logger.LogInformation("Servicio protegido obtenido exitosamente: {Name}", service.Name);
            return service;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Ocurrió un error inesperado al obtener el servicio protegido con ID: {Id}", id);
            return Result.Failure<ProtectedService>(new Error("UnhandledException", $"Ocurrió un error inesperado al obtener el servicio: {e.Message}"));
        }
    }
}