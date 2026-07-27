using System.Security.Claims;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests.Security;

/// <summary>
/// Tests unitarios del RbacAuthorizationMiddleware (HU-028 T-058).
/// Verifica que los roles del JWT se reemplazan por los de la tabla user_roles.
/// </summary>
public class RbacAuthorizationMiddlewareTests
{
    private readonly ILogSanitizer _sanitizer = Substitute.For<ILogSanitizer>();
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddDebug());

    public RbacAuthorizationMiddlewareTests()
    {
        _sanitizer.Sanitize(Arg.Any<string?>(), Arg.Any<int>())
            .Returns(callInfo => callInfo.ArgAt<string?>(0) ?? string.Empty);
    }

    /// <summary>
    /// Escenario HU-028: Admin accede a todo.
    /// Dado un usuario con rol ADMIN en user_roles, el middleware
    /// agrega el claim Role ADMIN al identity.
    /// </summary>
    [Fact]
    public async Task Admin_Gets_Role_Claim_From_Database()
    {
        // Arrange
        using var setup = await CreateDbWithUserAsync("admin-sub", "admin", "ADMIN");
        var context = CreateAuthenticatedContext(setup.Db, "admin-sub", jwtRoles: []);

        var middleware = new RbacAuthorizationMiddleware(
            _ => Task.CompletedTask,
            _loggerFactory.CreateLogger<RbacAuthorizationMiddleware>());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Single(roles);
        Assert.Contains("ADMIN", roles);
    }

    /// <summary>
    /// Escenario HU-028: Viewer solo lectura.
    /// Dado un usuario con rol VIEWER en user_roles, el middleware
    /// agrega solo el claim VIEWER (no ADMIN).
    /// </summary>
    [Fact]
    public async Task Viewer_Gets_Only_Viewer_Role_Claim()
    {
        // Arrange
        using var setup = await CreateDbWithUserAsync("viewer-sub", "viewer", "VIEWER");
        var context = CreateAuthenticatedContext(setup.Db, "viewer-sub", jwtRoles: ["ADMIN"]); // JWT dice ADMIN pero BD dice VIEWER

        var middleware = new RbacAuthorizationMiddleware(
            _ => Task.CompletedTask,
            _loggerFactory.CreateLogger<RbacAuthorizationMiddleware>());

        // Act
        await middleware.InvokeAsync(context);

        // Assert: JWT ADMIN claim fue reemplazado por BD VIEWER
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Single(roles);
        Assert.Contains("VIEWER", roles);
        Assert.DoesNotContain("ADMIN", roles);
    }

    /// <summary>
    /// Escenario HU-028: Sin rol.
    /// Dado un token válido sin roles en user_roles, el middleware
    /// deja al usuario sin claims de Role → policies fallarán con 403.
    /// </summary>
    [Fact]
    public async Task User_Without_Roles_Gets_No_Role_Claims()
    {
        // Arrange: usuario existe pero sin roles asignados
        using var setup = CreateInMemoryDb();
        var user = new User
        {
            Id = UserId.New(),
            Username = "noroles",
            Type = UserType.SecurityOfficer,
            PasswordHash = null!,
            KeycloakSub = "noroles-sub",
            IsActive = true
        };
        setup.Db.Users.Add(user);
        await setup.Db.SaveChangesAsync();

        var context = CreateAuthenticatedContext(setup.Db, "noroles-sub", jwtRoles: ["ADMIN"]);

        var middleware = new RbacAuthorizationMiddleware(
            _ => Task.CompletedTask,
            _loggerFactory.CreateLogger<RbacAuthorizationMiddleware>());

        // Act
        await middleware.InvokeAsync(context);

        // Assert: JWT ADMIN claim fue removido, no hay roles de BD
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Empty(roles);
    }

    /// <summary>
    /// Verifica que roles inactivos en BD no se asignan al principal.
    /// </summary>
    [Fact]
    public async Task Inactive_Roles_Are_Not_Added()
    {
        // Arrange
        using var setup = CreateInMemoryDb();
        var userId = UserId.New();
        var role = new Role { Id = RoleId.New(), Name = "INACTIVE_ROLE", IsActive = false };
        setup.Db.Users.Add(new User
        {
            Id = userId, Username = "test", Type = UserType.SecurityOfficer,
            PasswordHash = null!, KeycloakSub = "inactive-sub", IsActive = true
        });
        setup.Db.Roles.Add(role);
        setup.Db.UserRoles.Add(new UserRole { Id = UserRoleId.New(), UserId = userId, RoleId = role.Id });
        await setup.Db.SaveChangesAsync();

        var context = CreateAuthenticatedContext(setup.Db, "inactive-sub", jwtRoles: []);

        var middleware = new RbacAuthorizationMiddleware(
            _ => Task.CompletedTask,
            _loggerFactory.CreateLogger<RbacAuthorizationMiddleware>());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Empty(context.User.FindAll(ClaimTypes.Role));
    }

    /// <summary>
    /// Verifica que usuarios inactivos no reciben roles (fail-closed).
    /// </summary>
    [Fact]
    public async Task Inactive_User_Gets_No_Roles()
    {
        // Arrange
        using var setup = CreateInMemoryDb();
        var userId = UserId.New();
        var role = new Role { Id = RoleId.New(), Name = "ADMIN", IsActive = true };
        setup.Db.Users.Add(new User
        {
            Id = userId, Username = "inactive-user", Type = UserType.SecurityOfficer,
            PasswordHash = null!, KeycloakSub = "inactive-user-sub", IsActive = false // INACTIVE
        });
        setup.Db.Roles.Add(role);
        setup.Db.UserRoles.Add(new UserRole { Id = UserRoleId.New(), UserId = userId, RoleId = role.Id });
        await setup.Db.SaveChangesAsync();

        var context = CreateAuthenticatedContext(setup.Db, "inactive-user-sub", jwtRoles: []);

        var middleware = new RbacAuthorizationMiddleware(
            _ => Task.CompletedTask,
            _loggerFactory.CreateLogger<RbacAuthorizationMiddleware>());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Empty(context.User.FindAll(ClaimTypes.Role));
    }

    /// <summary>
    /// Verifica que peticiones no autenticadas pasan sin modificación.
    /// </summary>
    [Fact]
    public async Task Unauthenticated_Request_Passes_Through()
    {
        // Arrange
        using var setup = CreateInMemoryDb();
        var context = new DefaultHttpContext();
        // No authenticated user
        context.RequestServices = BuildServiceProvider(setup.Db);

        bool nextCalled = false;
        var middleware = new RbacAuthorizationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _loggerFactory.CreateLogger<RbacAuthorizationMiddleware>());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    /// <summary>
    /// Verifica que un usuario con múltiples roles recibe todos los claims.
    /// </summary>
    [Fact]
    public async Task User_With_Multiple_Roles_Gets_All_Claims()
    {
        // Arrange
        using var setup = CreateInMemoryDb();
        var userId = UserId.New();
        var adminRole = new Role { Id = RoleId.New(), Name = "ADMIN", IsActive = true };
        var viewerRole = new Role { Id = RoleId.New(), Name = "VIEWER", IsActive = true };
        setup.Db.Users.Add(new User
        {
            Id = userId, Username = "multirole", Type = UserType.SecurityOfficer,
            PasswordHash = null!, KeycloakSub = "multi-sub", IsActive = true
        });
        setup.Db.Roles.AddRange(adminRole, viewerRole);
        setup.Db.UserRoles.Add(new UserRole { Id = UserRoleId.New(), UserId = userId, RoleId = adminRole.Id });
        setup.Db.UserRoles.Add(new UserRole { Id = UserRoleId.New(), UserId = userId, RoleId = viewerRole.Id });
        await setup.Db.SaveChangesAsync();

        var context = CreateAuthenticatedContext(setup.Db, "multi-sub", jwtRoles: []);

        var middleware = new RbacAuthorizationMiddleware(
            _ => Task.CompletedTask,
            _loggerFactory.CreateLogger<RbacAuthorizationMiddleware>());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Equal(2, roles.Count);
        Assert.Contains("ADMIN", roles);
        Assert.Contains("VIEWER", roles);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private sealed class TestDbSetup : IDisposable
    {
        public SqliteConnection Connection { get; }
        public TestDbContext Db { get; }
        public UserId UserId { get; }

        public TestDbSetup(SqliteConnection connection, TestDbContext db, UserId userId = default)
        {
            Connection = connection;
            Db = db;
            UserId = userId;
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }

    private TestDbSetup CreateInMemoryDb()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new TestDbContext(options);
        db.Database.EnsureCreated();
        return new TestDbSetup(connection, db);
    }

    private async Task<TestDbSetup> CreateDbWithUserAsync(
        string sub, string username, params string[] roleNames)
    {
        var setup = CreateInMemoryDb();
        var userId = UserId.New();

        setup.Db.Users.Add(new User
        {
            Id = userId,
            Username = username,
            Type = UserType.SecurityOfficer,
            PasswordHash = null!,
            KeycloakSub = sub,
            IsActive = true
        });

        foreach (var roleName in roleNames)
        {
            var role = new Role { Id = RoleId.New(), Name = roleName, IsActive = true };
            setup.Db.Roles.Add(role);
            setup.Db.UserRoles.Add(new UserRole
            {
                Id = UserRoleId.New(),
                UserId = userId,
                RoleId = role.Id
            });
        }

        await setup.Db.SaveChangesAsync();
        return new TestDbSetup(setup.Connection, setup.Db, userId);
    }

    private HttpContext CreateAuthenticatedContext(
        OmakaseDbContext db, string sub, string[] jwtRoles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub),
            new("sub", sub)
        };
        foreach (var role in jwtRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new DefaultHttpContext
        {
            User = principal,
            RequestServices = BuildServiceProvider(db)
        };

        return context;
    }

    private IServiceProvider BuildServiceProvider(OmakaseDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<OmakaseDbContext>(db);
        services.AddSingleton<ILogSanitizer>(_sanitizer);
        return services.BuildServiceProvider();
    }
}
