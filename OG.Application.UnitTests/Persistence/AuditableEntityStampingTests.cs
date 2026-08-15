using System;
using System.Threading.Tasks;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using OG.Application.UnitTests.Security;
using Xunit;

namespace OG.Application.UnitTests.Persistence;

/// <summary>
/// Verifica el sellado centralizado de <c>updated_at</c> en
/// <see cref="OmakaseDbContext.SaveChangesAsync"/> (SRS §7.2, §7.5 y §7.9).
/// <para>
/// La garantía que importa es que ningún manejador tenga que acordarse de asignarlo:
/// basta con que la entidad implemente <c>IAuditableEntity</c>.
/// </para>
/// </summary>
public class AuditableEntityStampingTests : IDisposable
{
    private readonly TestDbContext _context;

    public AuditableEntityStampingTests()
    {
        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Insert_ShouldLeaveUpdatedAtNull()
    {
        var role = NewRole("AUDITOR");

        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        Assert.Null(role.UpdatedAt);
    }

    [Fact]
    public async Task Update_ShouldStampRole()
    {
        var role = NewRole("AUDITOR");
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        role.Description = "Solo lectura de logs de auditoría";
        await _context.SaveChangesAsync();

        Assert.NotNull(role.UpdatedAt);
        Assert.True(role.UpdatedAt >= role.CreatedAt);
    }

    [Fact]
    public async Task Update_ShouldStampProtectedService()
    {
        var service = new ProtectedService
        {
            Id           = ProtectedServiceId.New(),
            Name         = "httpbin",
            UpstreamUrl  = "https://httpbin.org/",
            RequiresAuth = false,
            IsActive     = true,
        };

        _context.ProtectedServices.Add(service);
        await _context.SaveChangesAsync();
        Assert.Null(service.UpdatedAt);

        service.UpstreamUrl = "http://localhost:8080/";
        await _context.SaveChangesAsync();

        Assert.NotNull(service.UpdatedAt);
    }

    [Fact]
    public async Task Update_ShouldStampAccessPolicy()
    {
        var author = new User
        {
            Id       = UserId.New(),
            Username = "officer",
            Type     = UserType.SecurityOfficer,
            IsActive = true,
        };

        var policy = new AccessPolicy
        {
            Id          = AccessPolicyId.New(),
            Name        = "Geofence RD",
            Type        = PolicyType.Geofence,
            Weight      = 1.0m,
            IsActive    = true,
            CreatedById = author.Id,
        };

        _context.Users.Add(author);
        _context.AccessPolicies.Add(policy);
        await _context.SaveChangesAsync();
        Assert.Null(policy.UpdatedAt);

        policy.Weight = 0.8m;
        await _context.SaveChangesAsync();

        Assert.NotNull(policy.UpdatedAt);
    }

    /// <summary>
    /// El sello solo debe alcanzar a la entidad realmente modificada: si tocar una arrastrase a
    /// las demás, <c>updated_at</c> dejaría de servir como rastro de cambios administrativos.
    /// </summary>
    [Fact]
    public async Task Update_ShouldNotStampUntouchedEntities()
    {
        var touched   = NewRole("ADMIN");
        var untouched = NewRole("VIEWER");

        _context.Roles.AddRange(touched, untouched);
        await _context.SaveChangesAsync();

        touched.Description = "Gestión completa";
        await _context.SaveChangesAsync();

        Assert.NotNull(touched.UpdatedAt);
        Assert.Null(untouched.UpdatedAt);
    }

    private static Role NewRole(string name) => new()
    {
        Id       = RoleId.New(),
        Name     = name,
        IsActive = true,
    };
}
