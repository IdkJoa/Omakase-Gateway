using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OG.Application.UnitTests.Security;
using OG.Dashboard.Features.Services;
using Xunit;

namespace OG.Application.UnitTests;

public class ProtectedServicesHandlersTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly IRedisService _redisServiceMock;

    public ProtectedServicesHandlersTests()
    {
        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            
        _context = new TestDbContext(options);
        _redisServiceMock = Substitute.For<IRedisService>();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Create_ShouldInsertToDatabase_AndPublishToRedisYarpChannel()
    {
        var loggerMock = Substitute.For<ILogger<CreateProtectedServiceHandler>>();
        var handler = new CreateProtectedServiceHandler(_context, _redisServiceMock, loggerMock);

        var result = await handler.CreateProtectedServiceAsync(
            "test-service", 
            "http://test-upstream", 
            false, 
            true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test-service", result.Value.Name);

        // Verify it was saved to DB
        var savedService = await _context.ProtectedServices.FirstOrDefaultAsync(s => s.Name == "test-service");
        Assert.NotNull(savedService);

        // Verify Redis Publish was called to trigger YARP reload
        await _redisServiceMock.Received(1).PublishAsync("yarp-reload-channel", "reload");
    }

    [Fact]
    public async Task Update_ShouldModifyDatabase_AndPublishToRedisYarpChannel()
    {
        var serviceId = ProtectedServiceId.New();
        var existingService = new ProtectedService 
        { 
            Id = serviceId, 
            Name = "old-name", 
            UpstreamUrl = "http://old-url",
            IsActive = true,
            RequiresAuth = false
        };
        _context.ProtectedServices.Add(existingService);
        await _context.SaveChangesAsync();

        var loggerMock = Substitute.For<ILogger<UpdateProtectedServiceHandler>>();
        var handler = new UpdateProtectedServiceHandler(_context, _redisServiceMock, loggerMock);

        var result = await handler.UpdateProtectedServiceAsync(
            serviceId.Value,
            "new-name",
            "http://new-url",
            true,
            false);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-name", result.Value.Name);
        Assert.Equal("http://new-url", result.Value.UpstreamUrl);

        // Verify DB was updated
        var updatedService = await _context.ProtectedServices.FindAsync(serviceId);
        Assert.NotNull(updatedService);
        Assert.Equal("new-name", updatedService.Name);

        // Verify Redis Publish was called to trigger YARP reload
        await _redisServiceMock.Received(1).PublishAsync("yarp-reload-channel", "reload");
    }

    [Fact]
    public async Task Delete_ShouldSoftDelete_AndPublishToRedisYarpChannel()
    {
        var serviceId = ProtectedServiceId.New();
        var existingService = new ProtectedService 
        { 
            Id = serviceId, 
            Name = "to-delete", 
            UpstreamUrl = "http://url",
            IsActive = true,
            RequiresAuth = false
        };
        _context.ProtectedServices.Add(existingService);
        await _context.SaveChangesAsync();

        var loggerMock = Substitute.For<ILogger<DeleteProtectedServiceHandler>>();
        var handler = new DeleteProtectedServiceHandler(_context, _redisServiceMock, loggerMock);

        var result = await handler.DeleteProtectedServiceAsync(serviceId.Value);

        Assert.True(result.IsSuccess);

        // Verify DB soft delete
        var deletedService = await _context.ProtectedServices.FindAsync(serviceId);
        Assert.NotNull(deletedService);
        Assert.False(deletedService.IsActive);

        // Verify Redis Publish was called to trigger YARP reload
        await _redisServiceMock.Received(1).PublishAsync("yarp-reload-channel", "reload");
    }

    [Theory]
    [InlineData("health")]
    [InlineData("auth")]
    [InlineData("alive")]
    [InlineData("openapi")]
    [InlineData("demo")]
    [InlineData("HEALTH")]
    public async Task Create_ShouldFail_WhenNameIsReserved(string reservedName)
    {
        var loggerMock = Substitute.For<ILogger<CreateProtectedServiceHandler>>();
        var handler = new CreateProtectedServiceHandler(_context, _redisServiceMock, loggerMock);

        var result = await handler.CreateProtectedServiceAsync(
            reservedName, 
            "http://test-upstream", 
            false, 
            true);

        Assert.True(result.IsFailure);
        Assert.Equal("ProtectedService.ReservedName", result.Error.Code);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("auth")]
    [InlineData("alive")]
    [InlineData("openapi")]
    [InlineData("demo")]
    public async Task Update_ShouldFail_WhenNameIsReserved(string reservedName)
    {
        var serviceId = ProtectedServiceId.New();
        var existingService = new ProtectedService 
        { 
            Id = serviceId, 
            Name = "valid-name", 
            UpstreamUrl = "http://old-url",
            IsActive = true,
            RequiresAuth = false
        };
        _context.ProtectedServices.Add(existingService);
        await _context.SaveChangesAsync();

        var loggerMock = Substitute.For<ILogger<UpdateProtectedServiceHandler>>();
        var handler = new UpdateProtectedServiceHandler(_context, _redisServiceMock, loggerMock);

        var result = await handler.UpdateProtectedServiceAsync(
            serviceId.Value,
            reservedName,
            "http://new-url",
            true,
            false);

        Assert.True(result.IsFailure);
        Assert.Equal("ProtectedService.ReservedName", result.Error.Code);
    }

    // #5: el nombre se publica como segmento de ruta YARP → rechazar vacío/null/espacios/slashes.
    [Theory]
    [InlineData(null)]           // antes reventaba con NRE en name.Trim()
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con espacio")]
    [InlineData("con/slash")]
    [InlineData("mal$char")]
    public async Task Create_ShouldFail_WhenNameIsInvalid(string? invalidName)
    {
        var loggerMock = Substitute.For<ILogger<CreateProtectedServiceHandler>>();
        var handler = new CreateProtectedServiceHandler(_context, _redisServiceMock, loggerMock);

        var result = await handler.CreateProtectedServiceAsync(invalidName!, "http://test-upstream", false, true);

        Assert.True(result.IsFailure);
        Assert.Equal("ProtectedService.InvalidName", result.Error.Code);
    }
}
