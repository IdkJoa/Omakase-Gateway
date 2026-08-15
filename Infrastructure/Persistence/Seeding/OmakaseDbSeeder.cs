using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.Security;
using Application.Common.Security.Mfa;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.AnomalyDetection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Persistence.Seeding;

public sealed class OmakaseDbSeeder : IDbSeeder
{
    private readonly OmakaseDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ITotpSecretProtector _totpProtector;
    private readonly BehaviorBaselineBootstrapper _baselineBootstrapper;
    private readonly IRedisService _redis;
    private readonly AnomalyDetectionOptions _anomalyOptions;

    public OmakaseDbSeeder(
        OmakaseDbContext db,
        IConfiguration configuration,
        ITotpSecretProtector totpProtector,
        BehaviorBaselineBootstrapper baselineBootstrapper,
        IRedisService redis,
        AnomalyDetectionOptions anomalyOptions)
    {
        _db = db;
        _configuration = configuration;
        _totpProtector = totpProtector;
        _baselineBootstrapper = baselineBootstrapper;
        _redis = redis;
        _anomalyOptions = anomalyOptions;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRiskScoreConfigAsync(cancellationToken);
        await SeedRolesAsync(cancellationToken);
        await SeedProtectedServicesAsync(cancellationToken);

        // Los roles deben persistirse antes de que SeedInitialUserAsync los consulte.
        await _db.SaveChangesAsync(cancellationToken);

        await SeedInitialUserAsync(cancellationToken);
        await SeedViewerUserAsync(cancellationToken);
        await SeedDemoClientUserAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        // El baseline necesita que el usuario demo ya tenga fila (FK).
        await SeedDemoBehaviorBaselineAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    // Valores CALIBRADOS en HU-035 (no los de diseño inicial 0.6/0.4, 40/75): con los originales el eje de
    // anomalía no podía accionar por sí solo y una violación determinista quedaba en el borde del desafío
    // en vez de denegar. Recalibrables desde el Dashboard sin redesplegar (SRS §9.1).
    private async Task SeedRiskScoreConfigAsync(CancellationToken ct)
    {
        if (await _db.RiskScoreConfigs.AnyAsync(ct)) return;

        _db.RiskScoreConfigs.Add(new RiskScoreConfig
        {
            Id                 = RiskScoreConfigId.New(),
            PolicyWeight       = 0.5m,
            AnomalyWeight      = 0.5m,
            ColdStartPenalty   = 30m,
            ColdStartN         = 10,
            ChallengeThreshold = 33m,
            BlockThreshold     = 70m,
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

    private async Task SeedProtectedServicesAsync(CancellationToken ct)
    {
        if (await _db.ProtectedServices.AnyAsync(ct)) return;

        // Servicio demo para YARP: rutea /httpbin/** hacia el upstream y permite demostrar la
        // hidratación/recarga cambiando upstream_url o is_active en BD.
        _db.ProtectedServices.Add(new ProtectedService
        {
            Id           = ProtectedServiceId.New(),
            Name         = "httpbin",
            UpstreamUrl  = "https://httpbin.org/",
            RequiresAuth = false,
            IsActive     = true,
        });
    }

    private async Task SeedInitialUserAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Type == UserType.SecurityOfficer, ct)) return;

        var adminRole = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == "ADMIN", ct);

        if (adminRole is null) return;

        var adminUser = new User
        {
            Id           = UserId.New(),
            Username     = "admin",
            Type         = UserType.SecurityOfficer,
            PasswordHash = null!,
            KeycloakSub  = null!,
            IsActive     = true,
        };

        _db.Users.Add(adminUser);
        _db.UserRoles.Add(new UserRole
        {
            Id         = UserRoleId.New(),
            UserId     = adminUser.Id,
            RoleId     = adminRole.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        });

        // Matches Keycloak config.
        var joelUser = new User
        {
            Id           = UserId.New(),
            Username     = "joel",
            Type         = UserType.SecurityOfficer,
            PasswordHash = null!,
            KeycloakSub  = null!,
            IsActive     = true,
        };

        _db.Users.Add(joelUser);
        _db.UserRoles.Add(new UserRole
        {
            Id         = UserRoleId.New(),
            UserId     = joelUser.Id,
            RoleId     = adminRole.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        });
    }

    // Usuario VIEWER (solo lectura) para probar la separación de roles del RBAC (HU-028). Se busca por
    // username (no por "ya hay un SecurityOfficer" como SeedInitialUserAsync) para sembrarse aunque el
    // admin ya exista.
    private async Task SeedViewerUserAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Username == "viewer", ct)) return;

        var viewerRole = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == "VIEWER", ct);

        if (viewerRole is null) return;

        var viewerUser = new User
        {
            Id           = UserId.New(),
            Username     = "viewer",
            Type         = UserType.SecurityOfficer,
            PasswordHash = null!,
            KeycloakSub  = null!,
            IsActive     = true,
        };

        _db.Users.Add(viewerUser);
        _db.UserRoles.Add(new UserRole
        {
            Id         = UserRoleId.New(),
            UserId     = viewerUser.Id,
            RoleId     = viewerRole.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        });
    }

    // Client user de demo con secreto TOTP sembrado desde configuración (Mfa:DemoUser:*), para demostrar
    // login + Challenge→verify→Allow sin enrolamiento manual. Si la sección no está configurada, no siembra nada.
    private async Task SeedDemoClientUserAsync(CancellationToken ct)
    {
        var username = _configuration["Mfa:DemoUser:Username"];
        var password = _configuration["Mfa:DemoUser:Password"];
        var totpSecret = _configuration["Mfa:DemoUser:TotpSecret"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(totpSecret)
            || string.IsNullOrWhiteSpace(password))
            return;

        if (await _db.Users.AnyAsync(u => u.Username == username, ct)) return;

        _db.Users.Add(new User
        {
            Id            = UserId.New(),
            Username      = username,
            Type          = UserType.Client,
            // Hash bcrypt factor 12 (SRS §3.6).
            PasswordHash  = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            KeycloakSub   = null!,
            IsActive      = true,
            IsInteractive = true,
            MfaEnabled    = true,
            TotpSecret    = _totpProtector.Protect(totpSecret.Trim()),
        });
    }

    // Baseline de comportamiento SINTÉTICO para el usuario de demo: sin esto el detector devuelve 50 neutro
    // hasta acumular accesos reales, y forzarlos con una ráfaga le enseñaría al modelo que el tráfico en
    // ráfaga es normal. Solo se activa con AnomalyDetection:DemoBaseline:Enabled=true (Development).
    private async Task SeedDemoBehaviorBaselineAsync(CancellationToken ct)
    {
        if (!_configuration.GetValue("AnomalyDetection:DemoBaseline:Enabled", false))
            return;

        var username = _configuration["Mfa:DemoUser:Username"];
        if (string.IsNullOrWhiteSpace(username))
            return;

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
            return;

        // Nunca sobrescribir un perfil existente (sintético previo o construido en vivo).
        if (await _db.UserBehaviorProfiles.AnyAsync(p => p.UserId == user.Id, ct))
            return;

        var days = _configuration.GetValue("AnomalyDetection:DemoBaseline:Days", 14);
        var seed = _configuration.GetValue("AnomalyDetection:DemoBaseline:Seed", 20260731);

        // La jornada modelada DEFINE qué considera normal el modelo (la hora es una de las cuatro features),
        // así que una ventana mal calibrada dispara falsos positivos. OJO: las horas son UTC porque el motor
        // evalúa con DateTimeOffset.UtcNow; para 9–18 en Santo Domingo (UTC-4) hay que configurar 13–22.
        var officeStart = _configuration.GetValue("AnomalyDetection:DemoBaseline:OfficeStartHour", 9);
        var officeEnd = _configuration.GetValue("AnomalyDetection:DemoBaseline:OfficeEndHour", 18);
        var meanPerDay = _configuration.GetValue("AnomalyDetection:DemoBaseline:MeanRequestsPerWorkday", 40);

        var profile = SyntheticTrafficProfile.OfficeWorker with
        {
            OfficeStart = TimeSpan.FromHours(Math.Clamp(officeStart, 0, 23)),
            OfficeEnd = TimeSpan.FromHours(Math.Clamp(officeEnd, 1, 24)),
            MeanRequestsPerWorkday = Math.Max(1, meanPerDay),
        };

        if (profile.OfficeEnd <= profile.OfficeStart)
            profile = profile with { OfficeStart = TimeSpan.FromHours(9), OfficeEnd = TimeSpan.FromHours(18) };

        var baseline = _baselineBootstrapper.Build(DateTimeOffset.UtcNow, days, seed, profile);

        var coldStartN = (await _db.RiskScoreConfigs.AsNoTracking().FirstOrDefaultAsync(ct))?.ColdStartN ?? 10;

        var isColdStart = baseline.AccessCount < coldStartN;

        _db.UserBehaviorProfiles.Add(new UserBehaviorProfile
        {
            Id              = UserBehaviorProfileId.New(),
            UserId          = user.Id,
            FeatureVector   = UserProfileSerializer.Serialize(baseline.TrainingWindow, baseline.RecentAccesses),
            AccessCount     = baseline.AccessCount,
            IsColdStart     = isColdStart,
            BaseRiskPenalty = 0m,
            LastTrainedAt   = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);

        // Redis tiene prioridad sobre PostgreSQL en UserProfileStore: sembrar solo en la base deja al motor
        // puntuando contra el perfil viejo hasta que la clave expire (verificado en vivo tras resembrar).
        var perfilParaCache = new UserAnomalyProfile(
            baseline.TrainingWindow, baseline.RecentAccesses, baseline.AccessCount, isColdStart);

        await _redis.CacheProfileAsync(
            user.Id.Value.ToString(),
            UserProfileSerializer.SerializeForCache(perfilParaCache),
            TimeSpan.FromMinutes(_anomalyOptions.ProfileCacheTtlMinutes));
    }
}
