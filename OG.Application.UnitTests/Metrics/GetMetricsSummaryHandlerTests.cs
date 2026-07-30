using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OG.Application.UnitTests.Security;
using OG.Dashboard.Features.Metrics;
using Xunit;

namespace OG.Application.UnitTests.Metrics;

public class GetMetricsSummaryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _db;
    private readonly GetMetricsSummaryHandler _sut;

    public GetMetricsSummaryHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestDbContext(options);
        _db.Database.EnsureCreated();

        _sut = new GetMetricsSummaryHandler(_db, NullLogger<GetMetricsSummaryHandler>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private User SeedUser(UserId userId, string username)
    {
        var user = new User
        {
            Id = userId,
            Username = username,
            Type = UserType.SecurityOfficer,
            PasswordHash = "hash",
            KeycloakSub = Guid.NewGuid().ToString(),
            IsActive = true
        };
        _db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task Empty_Database_Returns_Zero_Metrics_Safely()
    {
        // Act
        var result = await _sut.GetMetricsSummaryAsync();

        // Assert
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : string.Empty);
        var m = result.Value;
        Assert.Equal(0, m.TotalEvaluations);
        Assert.Equal(0, m.AllowedCount);
        Assert.Equal(0, m.ChallengedCount);
        Assert.Equal(0, m.BlockedCount);
        Assert.Equal(0.0, m.AllowedPercent);
        Assert.Equal(0.0, m.ChallengedPercent);
        Assert.Equal(0.0, m.BlockedPercent);
        Assert.Equal(0.0, m.AverageRiskScore);
        Assert.Equal(0, m.UniqueUsers);
        Assert.Empty(m.RiskScoreSeries);
    }

    [Fact]
    public async Task Calculates_Correct_Metrics_And_Percentages()
    {
        // Arrange: 5 ALLOW, 3 CHALLENGE, 2 BLOCK (Total 10)
        var user1 = UserId.New();
        var user2 = UserId.New();
        SeedUser(user1, "user1");
        SeedUser(user2, "user2");

        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 0; i < 5; i++)
        {
            _db.AuditLogs.Add(CreateAuditLog(user1, Verdict.Allow, 20m, baseTime.AddMinutes(-10 * i)));
        }
        for (int i = 0; i < 3; i++)
        {
            _db.AuditLogs.Add(CreateAuditLog(user2, Verdict.Challenge, 50m, baseTime.AddMinutes(-5 * i)));
        }
        for (int i = 0; i < 2; i++)
        {
            _db.AuditLogs.Add(CreateAuditLog(user1, Verdict.Block, 90m, baseTime.AddMinutes(-2 * i)));
        }
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetMetricsSummaryAsync(from: baseTime.AddHours(-1), to: baseTime.AddHours(1));

        // Assert
        Assert.True(result.IsSuccess);
        var m = result.Value;
        Assert.Equal(10, m.TotalEvaluations);
        Assert.Equal(5, m.AllowedCount);
        Assert.Equal(3, m.ChallengedCount);
        Assert.Equal(2, m.BlockedCount);
        Assert.Equal(50.0, m.AllowedPercent);
        Assert.Equal(30.0, m.ChallengedPercent);
        Assert.Equal(20.0, m.BlockedPercent);

        // Average Risk Score: (5*20 + 3*50 + 2*90) / 10 = (100 + 150 + 180) / 10 = 43.0
        Assert.Equal(43.0, m.AverageRiskScore);
        Assert.Equal(2, m.UniqueUsers);
    }

    [Fact]
    public async Task Filters_By_Date_Range()
    {
        // Arrange
        var user = UserId.New();
        SeedUser(user, "testuser");
        var now = DateTimeOffset.UtcNow;

        // Fuera de rango (hace 48 horas)
        _db.AuditLogs.Add(CreateAuditLog(user, Verdict.Allow, 10m, now.AddHours(-48)));
        // Dentro de rango (hace 2 horas)
        _db.AuditLogs.Add(CreateAuditLog(user, Verdict.Block, 80m, now.AddHours(-2)));

        await _db.SaveChangesAsync();

        // Act (rango de las últimas 12 horas)
        var result = await _sut.GetMetricsSummaryAsync(from: now.AddHours(-12), to: now);

        // Assert
        Assert.True(result.IsSuccess);
        var m = result.Value;
        Assert.Equal(1, m.TotalEvaluations);
        Assert.Equal(1, m.BlockedCount);
        Assert.Equal(80.0, m.AverageRiskScore);
    }

    [Fact]
    public async Task Groups_RiskScoreSeries_By_Hour()
    {
        // Arrange: 2 logs en Hora A, 1 log en Hora B
        var user = UserId.New();
        SeedUser(user, "seriesuser");
        var timeHourA = new DateTimeOffset(2026, 7, 27, 10, 15, 0, TimeSpan.Zero);
        var timeHourB = new DateTimeOffset(2026, 7, 27, 11, 45, 0, TimeSpan.Zero);

        _db.AuditLogs.Add(CreateAuditLog(user, Verdict.Allow, 20m, timeHourA));
        _db.AuditLogs.Add(CreateAuditLog(user, Verdict.Allow, 40m, timeHourA.AddMinutes(10)));
        _db.AuditLogs.Add(CreateAuditLog(user, Verdict.Block, 80m, timeHourB));
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetMetricsSummaryAsync(from: timeHourA.AddHours(-1), to: timeHourB.AddHours(1));

        // Assert
        Assert.True(result.IsSuccess);
        var m = result.Value;
        Assert.Equal(2, m.RiskScoreSeries.Count);

        var pointA = m.RiskScoreSeries[0];
        Assert.Equal(30.0, pointA.AvgScore); // (20 + 40) / 2
        Assert.Equal(2, pointA.EvaluationCount);

        var pointB = m.RiskScoreSeries[1];
        Assert.Equal(80.0, pointB.AvgScore);
        Assert.Equal(1, pointB.EvaluationCount);
    }

    private static AuditLog CreateAuditLog(UserId? userId, Verdict verdict, decimal riskScore, DateTimeOffset evaluatedAt)
    {
        return new AuditLog
        {
            Id = AuditLogId.New(),
            EvaluationId = Guid.NewGuid(),
            UserId = userId,
            SourceIp = "127.0.0.1",
            Verdict = verdict,
            RiskScore = riskScore,
            PolicyScore = riskScore,
            AnomalyScore = riskScore,
            EvaluatedAt = evaluatedAt
        };
    }
}
