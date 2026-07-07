using System.Security.Claims;
using System.Text.Json;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace OG.Application.UnitTests.Security;

public class AuthenticationExtensionsTests
{
    [Fact]
    public async Task OnTokenValidated_ShouldMapKeycloakRolesToClaims()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        services.AddLogging();
        services.AddOmakaseAuthentication(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        var claimsIdentity = new ClaimsIdentity();
        var realmAccessJson = JsonSerializer.Serialize(new { roles = new[] { "ADMIN", "VIEWER" } });
        claimsIdentity.AddClaim(new Claim("realm_access", realmAccessJson));

        var principal = new ClaimsPrincipal(claimsIdentity);
        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options)
        {
            Principal = principal
        };

        // Act
        await options.Events!.OnTokenValidated!(context);

        // Assert
        var roleClaims = claimsIdentity.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains("ADMIN", roleClaims);
        Assert.Contains("VIEWER", roleClaims);
    }

    [Fact]
    public async Task OnAuthenticationFailed_ShouldWriteAuditLogToDatabase()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var mockDbContext = Substitute.For<OmakaseDbContext>(new DbContextOptions<OmakaseDbContext>());
        var mockAuditLogs = Substitute.For<DbSet<Domain.Entities.AuditLog>>();
        mockDbContext.AuditLogs.Returns(mockAuditLogs);

        services.AddScoped(_ => mockDbContext);
        services.AddLogging();
        services.AddOmakaseAuthentication(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        var context = new AuthenticationFailedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options)
        {
            Exception = new Exception("Invalid signature")
        };

        // Act
        await options.Events!.OnAuthenticationFailed!(context);

        // Assert
        mockAuditLogs.Received(1).Add(Arg.Is<Domain.Entities.AuditLog>(log => 
            log.SourceIp == "127.0.0.1" && 
            log.Verdict == Domain.Entities.Verdict.Block && 
            log.RiskScore == 100
        ));
        await mockDbContext.Received(1).SaveChangesAsync();
    }
}
