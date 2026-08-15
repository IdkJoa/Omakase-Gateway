using Domain.Common;
using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OG.Dashboard.Features.Services;

public sealed class GetProtectedServicesHandler(OmakaseDbContext context, ILogger<GetProtectedServicesHandler> logger)
{
    public async Task<Result<(int TotalCount, List<ProtectedService> Services)>> GetProtectedServicesAsync(
        int page,
        int pageSize,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Iniciando obtención de servicios protegidos (Page: {Page}, PageSize: {PageSize}, IsActive: {IsActive})", page, pageSize, isActive);
        try
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 25;

            var query = context.ProtectedServices.AsNoTracking();

            if (isActive.HasValue)
            {
                query = query.Where(s => s.IsActive == isActive.Value);
            }

            var totalCount = await query.CountAsync(cancellationToken);
            
            var services = await query
                .Include(s => s.ServicePolicies)
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            logger.LogInformation("Obtención de servicios completada. Total: {TotalCount}, Retornados: {Retornados}", totalCount, services.Count);
            return (totalCount, services);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Ocurrió un error inesperado al obtener la lista de servicios protegidos.");
            return Result.Failure<(int TotalCount, List<ProtectedService> Services)>(new Error("UnhandledException", "Ocurrió un error inesperado al obtener los servicios."));
        }
    }
}
