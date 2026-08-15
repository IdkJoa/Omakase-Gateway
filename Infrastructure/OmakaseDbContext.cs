using Domain.Common;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public class OmakaseDbContext(DbContextOptions<OmakaseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AccessPolicy> AccessPolicies => Set<AccessPolicy>();
    public DbSet<UserBehaviorProfile> UserBehaviorProfiles => Set<UserBehaviorProfile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProtectedService> ProtectedServices => Set<ProtectedService>();
    public DbSet<RiskScoreConfig> RiskScoreConfigs => Set<RiskScoreConfig>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ServicePolicy> ServicePolicies => Set<ServicePolicy>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    // Convencion. Se auto convierte cada TypedId struct a GUID y viceversa
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new TypedIdConvention());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OmakaseDbContext).Assembly);
    }

    // Sella updated_at aquí para toda IAuditableEntity modificada, evitando que cada handler tenga que recordarlo.
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditableEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc cref="SaveChangesAsync(CancellationToken)"/>
    public override int SaveChanges()
    {
        StampAuditableEntities();
        return base.SaveChanges();
    }

    private void StampAuditableEntities()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
    }
}
