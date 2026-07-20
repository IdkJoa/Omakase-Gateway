using System.Security.Claims;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Security;

/// <summary>
/// Implementa <see cref="ICurrentUserService"/> sobre el DbContext (HU-023 T-047).
/// <para>
/// Orden de resolución: (1) fila ya vinculada por <c>keycloak_sub</c>; (2) fila de
/// Security Officer sembrada sin vincular con el mismo username (el seeder crea
/// <c>admin</c> con <c>keycloak_sub = null</c>) — se adopta y se vincula; (3) alta
/// just-in-time. Los índices únicos de <c>keycloak_sub</c> y <c>username</c> protegen
/// contra carreras: ante conflicto se relee la fila ganadora.
/// </para>
/// <para>
/// Nunca se adopta una fila de tipo <see cref="UserType.Client"/>: vincular el sub de un
/// administrador a una cuenta de client user sería una escalada de privilegios silenciosa.
/// Si el username ya está tomado por una cuenta no adoptable, se usa el propio sub como
/// username local (único por construcción).
/// </para>
/// </summary>
public sealed class KeycloakCurrentUserService : ICurrentUserService
{
    private readonly OmakaseDbContext _db;
    private readonly ILogSanitizer _sanitizer;
    private readonly ILogger<KeycloakCurrentUserService> _logger;

    public KeycloakCurrentUserService(
        OmakaseDbContext db,
        ILogSanitizer sanitizer,
        ILogger<KeycloakCurrentUserService> logger)
    {
        _db = db;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    public async Task<User?> GetOrProvisionAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var sub = principal.FindFirst("sub")?.Value
               ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(sub))
            return null;

        var linked = await _db.Users.FirstOrDefaultAsync(u => u.KeycloakSub == sub, cancellationToken);
        if (linked is not null)
            return linked.IsActive ? linked : null;

        var username = principal.FindFirst("preferred_username")?.Value
                    ?? principal.Identity?.Name
                    ?? sub;

        // Fila sembrada sin vincular (seeder: admin con keycloak_sub null) → adoptar.
        var unlinked = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == username && u.KeycloakSub == null && u.Type == UserType.SecurityOfficer,
            cancellationToken);

        if (unlinked is not null)
        {
            unlinked.KeycloakSub = sub;
            unlinked.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "JIT provisioning: fila local {Username} vinculada al sub de Keycloak ({UserId}).",
                _sanitizer.Sanitize(username), unlinked.Id.Value);

            return unlinked.IsActive ? unlinked : null;
        }

        // Username tomado por una cuenta no adoptable (p. ej. un client user homónimo):
        // el sub es único por construcción y sirve de username local.
        if (await _db.Users.AnyAsync(u => u.Username == username, cancellationToken))
            username = sub;

        var created = new User
        {
            Id           = UserId.New(),
            Username     = username,
            Type         = UserType.SecurityOfficer,
            // SecurityOfficer no usa contraseña local; se autentica vía Keycloak (HU-018).
            PasswordHash = null!,
            KeycloakSub  = sub,
            IsActive     = true,
        };

        _db.Users.Add(created);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Carrera con otra petición del mismo administrador: relee la fila ganadora.
            _db.Entry(created).State = EntityState.Detached;
            var winner = await _db.Users.FirstOrDefaultAsync(u => u.KeycloakSub == sub, cancellationToken);
            if (winner is null)
                throw;
            return winner.IsActive ? winner : null;
        }

        _logger.LogInformation(
            "JIT provisioning: administrador de Keycloak {Username} dado de alta en users ({UserId}).",
            _sanitizer.Sanitize(username), created.Id.Value);

        return created;
    }
}
