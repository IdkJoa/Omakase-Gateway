using System.Security.Claims;
using Domain.Entities;

namespace Application.Common.Security;

/// <summary>
/// Puente de identidad entre el token de Keycloak y la tabla <c>users</c> (HU-023 T-047).
/// <para>
/// Los administradores (Security Officers) se autentican vía Keycloak (HU-018) y su
/// identidad primaria vive en el realm, pero las FKs del dominio (<c>access_policies.created_by</c>,
/// <c>user_roles.user_id</c>, <c>audit_logs.user_id</c>) exigen una fila local en <c>users</c>.
/// Este puerto resuelve esa fila a partir del claim <c>sub</c> del token, aprovisionándola
/// just-in-time si aún no existe (SRS §9.5 — identidad dual Keycloak + JWT propio).
/// </para>
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Devuelve el usuario local correspondiente al principal autenticado, creándolo
    /// o vinculándolo (fila sembrada sin <c>keycloak_sub</c>) si es la primera vez.
    /// Devuelve <c>null</c> si el token no trae un claim <c>sub</c> utilizable o si
    /// la cuenta local está desactivada (fail-closed).
    /// </summary>
    Task<User?> GetOrProvisionAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
