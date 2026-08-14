using System.Security.Claims;
using Domain.Entities;

namespace Application.Common.Security;

// Los administradores se autentican vía Keycloak y su identidad primaria vive en el realm, pero
// las FKs del dominio exigen una fila local en `users`. Este puerto resuelve esa fila a partir del
// claim sub del token, aprovisionándola just-in-time si aún no existe (identidad dual Keycloak + JWT propio).
public interface ICurrentUserService
{
    // Null si el token no trae un claim sub utilizable o si la cuenta local está desactivada (fail-closed).
    Task<User?> GetOrProvisionAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
