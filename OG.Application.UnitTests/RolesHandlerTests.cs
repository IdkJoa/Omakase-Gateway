using System;
using System.Threading.Tasks;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OG.Application.UnitTests.Security;
using OG.Dashboard.Features.Roles;
using Xunit;

namespace OG.Application.UnitTests;

public class RolesHandlerTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly RolesHandler _handler;

    public RolesHandlerTests()
    {
        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestDbContext(options);
        _handler = new RolesHandler(_context, Substitute.For<ILogger<RolesHandler>>());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Create_ShouldSucceed_WhenNameIsValid()
    {
        var result = await _handler.CreateRoleAsync("AUDITOR", "Solo lectura de logs");

        Assert.True(result.IsSuccess);
        Assert.Equal("AUDITOR", result.Value.Name);
        Assert.NotNull(await _context.Roles.FirstOrDefaultAsync(r => r.Name == "AUDITOR"));
    }

    // El nombre es la clave RBAC: rechazar vacío/null/whitespace/demasiado corto
    // en vez de crear un rol en blanco (o reventar con NRE en name.Trim() → 500).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public async Task Create_ShouldFail_WhenNameIsInvalid(string? invalidName)
    {
        var result = await _handler.CreateRoleAsync(invalidName!, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Roles.InvalidName", result.Error.Code);
    }

    [Fact]
    public async Task Create_ShouldFail_WhenNameAlreadyExists()
    {
        await _handler.CreateRoleAsync("VIEWER", null);

        var duplicate = await _handler.CreateRoleAsync("viewer", null); // case-insensitive

        Assert.True(duplicate.IsFailure);
        Assert.Equal("Roles.Conflict", duplicate.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public async Task Update_ShouldFail_WhenNameIsInvalid(string? invalidName)
    {
        var roleId = RoleId.New();
        _context.Roles.Add(new Role
        {
            Id = roleId,
            Name = "ORIGINAL",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _handler.UpdateRoleAsync(roleId.Value, invalidName!, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Roles.InvalidName", result.Error.Code);
    }
}
