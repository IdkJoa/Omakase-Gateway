using Domain.Common;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Application.Common.Security;
using Microsoft.Extensions.Logging;

namespace OG.Dashboard.Features.Services;

public sealed class DeleteProtectedServiceHandler(OmakaseDbContext context, IRedisService redis, ILogger<DeleteProtectedServiceHandler> logger)
{
    private const string YarpReloadChannel = "yarp-reload-channel";

    public async Task<Result> DeleteProtectedServiceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Iniciando eliminación (soft-delete) del servicio protegido: {Id}", id);
        try
        {
            var serviceId = new Domain.ValueObjects.ProtectedServiceId(id);
            var service = await context.ProtectedServices.FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);
            if (service is null)
            {
                logger.LogWarning("No se encontró el servicio protegido con ID: {Id} para eliminar", id);
                return Result.Failure(new Error("ProtectedService.NotFound", $"Servicio '{id}' no encontrado."));
            }

            service.IsActive = false;
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Servicio protegido marcado como inactivo en base de datos: {Id}", id);

            await redis.PublishAsync(YarpReloadChannel, "reload");
            logger.LogInformation("Señal de recarga enviada vía Redis a YARP.");

            return Result.Success();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Ocurrió un error inesperado al eliminar el servicio protegido: {Id}", id);
            return Result.Failure(new Error("UnhandledException", "Ocurrió un error inesperado al eliminar el servicio."));
        }
    }
}
