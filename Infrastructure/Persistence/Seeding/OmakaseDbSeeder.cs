using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Seeding;

public sealed class OmakaseDbSeeder : IDbSeeder
{
    private readonly OmakaseDbContext _db;

    public OmakaseDbSeeder(OmakaseDbContext db) => _db = db;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRiskScoreConfigAsync(cancellationToken);
        await SeedRolesAsync(cancellationToken);

        // Los roles deben persistirse antes de que SeedInitialUserAsync los consulte.
        await _db.SaveChangesAsync(cancellationToken);

        await SeedInitialUserAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedRiskScoreConfigAsync(CancellationToken ct)
    {
        if (await _db.RiskScoreConfigs.AnyAsync(ct)) return;

        _db.RiskScoreConfigs.Add(new RiskScoreConfig
        {
            Id                 = RiskScoreConfigId.New(),
            PolicyWeight       = 0.6m,
            AnomalyWeight      = 0.4m,
            ColdStartPenalty   = 30m,
            ChallengeThreshold = 40m,
            BlockThreshold     = 75m,
        });
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        if (await _db.Roles.AnyAsync(ct)) return;

        _db.Roles.AddRange(
            new Role
            {
                Id          = RoleId.New(),
                Name        = "ADMIN",
                Description = "Full administrative access to gateway configuration, policies, and risk monitoring.",
                IsActive    = true,
            },
            new Role
            {
                Id          = RoleId.New(),
                Name        = "VIEWER",
                Description = "Read-only access to audit logs and risk dashboards.",
                IsActive    = true,
            });
    }

    private async Task SeedInitialUserAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Type == UserType.SecurityOfficer, ct)) return;

        var adminRole = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == "ADMIN", ct);

        if (adminRole is null) return;

        var user = new User
        {
            Id           = UserId.New(),
            Username     = "admin",
            Type         = UserType.SecurityOfficer,
            // SecurityOfficer no usa contraseña local; se autentica vía Keycloak.
            PasswordHash = null!,
            KeycloakSub  = null!,
            IsActive     = true,
        };

        _db.Users.Add(user);
        _db.UserRoles.Add(new UserRole
        {
            Id         = UserRoleId.New(),
            UserId     = user.Id,
            RoleId     = adminRole.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        });
    }
}
