using Domain.Common;
using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Application.Common.Security;
using Microsoft.Extensions.Logging;

namespace OG.Dashboard.Features.Services;

public sealed class CreateProtectedServiceHandler(OmakaseDbContext context, IRedisService redis, ILogger<CreateProtectedServiceHandler> logger)
{
    private const string YarpReloadChannel = "yarp-reload-channel";

    public async Task<Result<ProtectedService>> CreateProtectedServiceAsync(
        string name,
        string upstreamUrl,
        bool requiresAuth,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Iniciando creación de servicio protegido: {Name}", name);
        try
        {
            if (!IsValidUrl(upstreamUrl))
            {
                logger.LogWarning("Validación fallida: UpstreamUrl inválida ({UpstreamUrl}) para el servicio {Name}", upstreamUrl, name);
                return Result.Failure<ProtectedService>(new Error("ProtectedService.InvalidUrl", "UpstreamUrl debe ser una URL HTTP/HTTPS válida."));
            }

            var exists = await context.ProtectedServices.AnyAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken);
            if (exists)
            {
                logger.LogWarning("Conflicto: Ya existe un servicio con el nombre {Name}", name);
                return Result.Failure<ProtectedService>(new Error("ProtectedService.Conflict", $"Ya existe un servicio con el nombre '{name}'."));
            }

            var service = new ProtectedService
            {
                Id = new Domain.ValueObjects.ProtectedServiceId(Guid.NewGuid()),
                Name = name,
                UpstreamUrl = upstreamUrl,
                RequiresAuth = requiresAuth,
                IsActive = isActive,
                CreatedAt = DateTimeOffset.UtcNow
            };

            context.ProtectedServices.Add(service);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Servicio protegido guardado en base de datos: {Id}", service.Id.Value);

            await redis.PublishAsync(YarpReloadChannel, "reload");
            logger.LogInformation("Señal de recarga enviada vía Redis a YARP.");

            return service;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Ocurrió un error inesperado al crear el servicio protegido: {Name}", name);
            return Result.Failure<ProtectedService>(new Error("UnhandledException", $"Ocurrió un error inesperado al crear el servicio: {e.Message}"));
        }
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}