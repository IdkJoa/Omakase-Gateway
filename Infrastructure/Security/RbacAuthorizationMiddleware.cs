using System.Security.Claims;
using Application.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Security;

/// <summary>
/// Middleware de autorización RBAC que reemplaza los roles del token JWT (Keycloak)
/// por los roles asignados en la tabla <c>user_roles</c> de la base de datos local.
/// <para>
/// Posición en el pipeline: DESPUÉS de <c>UseAuthentication()</c> y ANTES de
/// <c>UseAuthorization()</c>. Solo actúa sobre usuarios autenticados con un claim
/// <c>sub</c> válido.
/// </para>
/// <para>
/// Decisión de arquitectura (HU-028 T-058): la BD es la fuente de verdad para roles.
/// Los roles en el JWT de Keycloak se ignoran para decisiones de autorización.
/// Esto permite que los cambios de roles vía el Dashboard (asignar/revocar) surtan
/// efecto inmediatamente sin esperar a que el token expire.
/// </para>
/// <para>
/// Fail-closed: si la consulta a la BD falla, el usuario queda sin Role claims,
/// por lo que las policies <c>AdminOnly</c> y <c>ReadAccess</c> rechazarán la
/// petición con 403.
/// </para>
/// </summary>
public sealed class RbacAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RbacAuthorizationMiddleware> _logger;

    public RbacAuthorizationMiddleware(
        RequestDelegate next,
        ILogger<RbacAuthorizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await EnrichWithDbRolesAsync(context);
        }

        await _next(context);
    }

    private async Task EnrichWithDbRolesAsync(HttpContext context)
    {
        var identity = context.User.Identity as ClaimsIdentity;
        if (identity is null) return;

        var sub = identity.FindFirst("sub")?.Value
                  ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(sub)) return;

        var sanitizer = context.RequestServices.GetRequiredService<ILogSanitizer>();

        try
        {
            var db = context.RequestServices.GetRequiredService<OmakaseDbContext>();

            var roleNames = await db.Users
                .AsNoTracking()
                .Where(u => u.KeycloakSub == sub && u.IsActive)
                .SelectMany(u => u.UserRoles)
                .Where(ur => ur.Role != null && ur.Role.IsActive)
                .Select(ur => ur.Role!.Name)
                .ToListAsync(context.RequestAborted);

            var existingRoleClaims = identity.FindAll(ClaimTypes.Role).ToList();
            foreach (var claim in existingRoleClaims)
            {
                identity.RemoveClaim(claim);
            }

            foreach (var roleName in roleNames)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
            }

            if (roleNames.Count == 0)
            {
                _logger.LogWarning(
                    "AUDIT RBAC-001: Usuario autenticado (sub={Sub}) no tiene roles activos en user_roles. " +
                    "Acceso a endpoints protegidos será denegado.",
                    sanitizer.Sanitize(sub));
            }
        }
        catch (Exception ex)
        {
            // Fail-closed: si no podemos consultar la BD, el usuario queda sin roles y las policies rechazan con 403.
            _logger.LogError(ex,
                "AUDIT RBAC-002: Error consultando roles en BD para sub={Sub}. " +
                "Fail-closed: usuario queda sin Role claims.",
                sanitizer.Sanitize(sub));

            var existingRoleClaims = identity.FindAll(ClaimTypes.Role).ToList();
            foreach (var claim in existingRoleClaims)
            {
                identity.RemoveClaim(claim);
            }
        }
    }
}
