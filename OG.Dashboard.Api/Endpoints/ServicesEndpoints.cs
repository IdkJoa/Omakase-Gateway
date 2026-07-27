using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Services;
using OG.Dashboard.Features.Services;
using Application.Common.Security;
using System.Diagnostics;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints CRUD de servicios protegidos por el Gateway.
/// CRUD bajo /api/v1/services — corresponde a la entidad ProtectedService del dominio.
/// El campo <c>Name</c> de cada servicio actúa como clusterId en YARP.
/// </summary>
public static class ServicesEndpoints
{
    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/services")
            .WithTags("Protected Services")
            .WithOpenApi();

        // GET /api/v1/services
        group.MapGet("/", GetAll)
            .RequireAuthorization("ReadAccess")
            .WithName("GetServices")
            .WithSummary("Listar servicios protegidos")
            .WithDescription("Devuelve la lista paginada de servicios protegidos por el Gateway. Soporta filtro por isActive.");

        // GET /api/v1/services/{id}
        group.MapGet("/{id:guid}", GetById)
            .RequireAuthorization("ReadAccess")
            .WithName("GetServiceById")
            .WithSummary("Obtener un servicio protegido por ID");

        // POST /api/v1/services
        group.MapPost("/", Create)
            .RequireAuthorization("AdminOnly")
            .WithName("CreateService")
            .WithSummary("Registrar un nuevo servicio protegido")
            .Produces<ProtectedServiceDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // PUT /api/v1/services/{id}
        group.MapPut("/{id:guid}", Update)
            .RequireAuthorization("AdminOnly")
            .WithName("UpdateService")
            .WithSummary("Actualizar un servicio protegido existente")
            .Produces<ProtectedServiceDto>(StatusCodes.Status200OK);

        // DELETE /api/v1/services/{id}
        group.MapDelete("/{id:guid}", Delete)
            .RequireAuthorization("AdminOnly")
            .WithName("DeleteService")
            .WithSummary("Eliminar (soft-delete) un servicio protegido")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAll(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] bool? isActive,
        [FromServices] GetProtectedServicesHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.GetProtectedServicesAsync(page, pageSize, isActive, ct);
        if (result.IsFailure)
        {
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        var (totalCount, services) = result.Value;
        var dtos = services.Select(s => new ProtectedServiceDto(
            s.Id.Value, enc.Sanitize(s.Name), enc.Sanitize(s.UpstreamUrl), s.RequiresAuth, s.IsActive, s.CreatedAt, s.ServicePolicies.Count)).ToList();
            
        return Results.Ok(new PagedResponse<ProtectedServiceDto>(page, pageSize, totalCount, dtos));
    }

    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] GetProtectedServiceHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.GetProtectedServiceAsync(id, ct);
        if (result.IsFailure)
        {
            if (result.Error.Code.Contains("NotFound"))
            {
                return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
        }
            
        var service = result.Value;
        var dto = new ProtectedServiceDto(
            service.Id.Value, enc.Sanitize(service.Name), enc.Sanitize(service.UpstreamUrl), service.RequiresAuth, service.IsActive, service.CreatedAt, service.ServicePolicies.Count);
            
        return Results.Ok(dto);
    }

    private static async Task<IResult> Create(
        [FromBody] UpsertServiceRequest request,
        [FromServices] CreateProtectedServiceHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.CreateProtectedServiceAsync(request.Name, request.UpstreamUrl, request.RequiresAuth, request.IsActive, ct);
        
        if (result.IsFailure)
        {
            if (result.Error.Code.Contains("InvalidUrl"))
                return Results.BadRequest(new ErrorResponse("INVALID_URL", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
            if (result.Error.Code.Contains("Conflict"))
                return Results.Conflict(new ErrorResponse("CONFLICT", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
                
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        var service = result.Value;
        var dto = new ProtectedServiceDto(
            service.Id.Value, enc.Sanitize(service.Name), enc.Sanitize(service.UpstreamUrl), service.RequiresAuth, service.IsActive, service.CreatedAt, 0);
            
        return Results.Created($"/api/v1/services/{dto.Id}", dto);
    }

    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpsertServiceRequest request,
        [FromServices] UpdateProtectedServiceHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.UpdateProtectedServiceAsync(id, request.Name, request.UpstreamUrl, request.RequiresAuth, request.IsActive, ct);
        
        if (result.IsFailure)
        {
            if (result.Error.Code.Contains("NotFound"))
                return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
            if (result.Error.Code.Contains("InvalidUrl"))
                return Results.BadRequest(new ErrorResponse("INVALID_URL", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
            if (result.Error.Code.Contains("Conflict"))
                return Results.Conflict(new ErrorResponse("CONFLICT", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
                
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        var service = result.Value;
        var dto = new ProtectedServiceDto(
            service.Id.Value, enc.Sanitize(service.Name), enc.Sanitize(service.UpstreamUrl), service.RequiresAuth, service.IsActive, service.CreatedAt, service.ServicePolicies.Count);
            
        return Results.Ok(dto);
    }

    private static async Task<IResult> Delete(
        Guid id,
        [FromServices] DeleteProtectedServiceHandler handler,
        CancellationToken ct)
    {
        var result = await handler.DeleteProtectedServiceAsync(id, ct);
        
        if (result.IsFailure)
        {
            if (result.Error.Code.Contains("NotFound"))
                return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, Activity.Current?.TraceId.ToString() ?? "N/A"));
        }
            
        return Results.NoContent();
    }
}
