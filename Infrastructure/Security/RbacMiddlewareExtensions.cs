using Microsoft.AspNetCore.Builder;

namespace Infrastructure.Security;

public static class RbacMiddlewareExtensions
{
    // Debe invocarse DESPUÉS de UseAuthentication() y ANTES de UseAuthorization().
    public static IApplicationBuilder UseRbacAuthorization(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RbacAuthorizationMiddleware>();
    }
}
