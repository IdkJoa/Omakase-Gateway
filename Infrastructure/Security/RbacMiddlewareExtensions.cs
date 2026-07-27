using Microsoft.AspNetCore.Builder;

namespace Infrastructure.Security;

/// <summary>
/// Métodos de extensión para registrar <see cref="RbacAuthorizationMiddleware"/>
/// en el pipeline de ASP.NET Core.
/// </summary>
public static class RbacMiddlewareExtensions
{
    /// <summary>
    /// Inserta el middleware RBAC que reemplaza los roles del token JWT
    /// por los de la tabla <c>user_roles</c> de la BD local (HU-028 T-058).
    /// <para>
    /// Debe invocarse DESPUÉS de <c>UseAuthentication()</c> y ANTES de
    /// <c>UseAuthorization()</c>.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UseRbacAuthorization(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RbacAuthorizationMiddleware>();
    }
}
