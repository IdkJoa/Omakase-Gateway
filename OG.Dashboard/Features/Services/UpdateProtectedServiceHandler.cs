using Domain.Common;
using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Application.Common.Security;
using Microsoft.Extensions.Logging;

namespace OG.Dashboard.Features.Services;

public sealed class UpdateProtectedServiceHandler(OmakaseDbContext context, IRedisService redis, ILogger<UpdateProtectedServiceHandler> logger)
{
    private const string YarpReloadChannel = "yarp-reload-channel";

    public async Task<Result<ProtectedService>> UpdateProtectedServiceAsync(
        Guid id,
        string name,
        string upstreamUrl,
        bool requiresAuth,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Iniciando actualización del servicio protegido: {Id}", id);
        try
        {
            var serviceId = new Domain.ValueObjects.ProtectedServiceId(id);
            var service = await context.ProtectedServices
                .Include(s => s.ServicePolicies)
                .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);
                
            if (service is null)
            {
                logger.LogWarning("No se encontró el servicio protegido con ID: {Id} para actualizar", id);
                return Result.Failure<ProtectedService>(new Error("ProtectedService.NotFound", $"Servicio '{id}' no encontrado."));
            }

            if (!IsValidServiceName(name))
            {
                logger.LogWarning("Validación fallida: nombre de servicio inválido ({Name})", name);
                return Result.Failure<ProtectedService>(new Error("ProtectedService.InvalidName",
                    "El nombre es obligatorio y solo admite letras, números, punto, guion y guion bajo (1-64 caracteres, sin espacios ni '/')."));
            }

            if (ProtectedService.ReservedNames.Contains(name.Trim()))
            {
                logger.LogWarning("Validación fallida: El nombre {Name} está reservado por el sistema", name);
                return Result.Failure<ProtectedService>(new Error("ProtectedService.ReservedName", $"El nombre '{name}' está reservado por el sistema y no se puede utilizar."));
            }

            if (!IsValidUrl(upstreamUrl))
            {
                logger.LogWarning("Validación fallida: UpstreamUrl inválida ({UpstreamUrl}) para el servicio {Id}", upstreamUrl, id);
                return Result.Failure<ProtectedService>(new Error("ProtectedService.InvalidUrl", "UpstreamUrl debe ser una URL HTTP/HTTPS válida."));
            }

            if (service.Name != name)
            {
                var exists = await context.ProtectedServices.AnyAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken);
                if (exists)
                {
                    logger.LogWarning("Conflicto: Ya existe otro servicio con el nombre {Name}", name);
                    return Result.Failure<ProtectedService>(new Error("ProtectedService.Conflict", $"Ya existe un servicio con el nombre '{name}'."));
                }
            }

            service.Name = name;
            service.UpstreamUrl = upstreamUrl;
            service.RequiresAuth = requiresAuth;
            service.IsActive = isActive;

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Servicio protegido actualizado en base de datos: {Id}", id);

            await redis.PublishAsync(YarpReloadChannel, "reload");
            logger.LogInformation("Señal de recarga enviada vía Redis a YARP.");

            return service;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Ocurrió un error inesperado al actualizar el servicio protegido: {Id}", id);
            return Result.Failure<ProtectedService>(new Error("UnhandledException", "Ocurrió un error inesperado al actualizar el servicio."));
        }
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// El nombre se publica como segmento de ruta en YARP (<c>/{name}/**</c>): debe ser no vacío y
    /// solo letras/números/punto/guion/guion bajo (sin espacios ni '/'), 1-64 chars. Evita rutas rotas.
    /// </summary>
    private static bool IsValidServiceName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && System.Text.RegularExpressions.Regex.IsMatch(name.Trim(), "^[a-zA-Z0-9._-]{1,64}$");
}
