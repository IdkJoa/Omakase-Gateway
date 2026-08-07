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

    /// <summary>
    /// Siembra la configuración del motor con los valores CALIBRADOS en HU-035, no con los
    /// de diseño inicial.
    /// <para>
    /// Importa la distinción. Los valores originales (0.6/0.4, umbrales 40 y 75) se fijaron
    /// antes de tener datos: con ellos el eje de anomalía topa en 40 y no puede accionar por
    /// sí solo, y una violación determinista al 100 % con anomalía neutra da exactamente
    /// 75.00 —el borde— por lo que quedaba degradada a desafío en vez de denegar. La
    /// calibración empírica igualó los pesos y bajó los umbrales a 33 y 70; es lo que reporta
    /// el Capítulo IV de la tesis y lo que debe encontrar quien levante el entorno, porque un
    /// motor sembrado sin calibrar no reproduce los resultados publicados.
    /// </para>
    /// <para>Siguen siendo recalibrables desde el Dashboard sin redesplegar (SRS §9.1).</para>
    /// </summary>
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

        // Servicio demo para YARP (HU-009): rutea /httpbin/** hacia el upstream.
        // Permite demostrar la hidratación/recarga cambiando upstream_url o is_active en BD.
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

        // 1. Seed admin user
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

        // 2. Seed joel user (matching Keycloak config)
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

    /// <summary>
    /// Siembra un usuario VIEWER (solo lectura) para probar la separación de roles del RBAC (HU-028):
    /// ReadAccess (ADMIN|VIEWER) le permite ver, AdminOnly (ADMIN) le deniega con 403. El usuario
    /// Keycloak correspondiente vive en <c>realm-export.json</c> (username 'viewer'). Guardado por
    /// username para ser idempotente y sembrarse aunque el admin ya exista, a diferencia de
    /// <see cref="SeedInitialUserAsync"/> (que se salta todo si ya hay un SecurityOfficer).
    /// </summary>
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

    /// <summary>
    /// HU-046 / T-102: client user de demo con secreto TOTP sembrado desde configuración
    /// (<c>Mfa:DemoUser:Username</c> + <c>Mfa:DemoUser:Password</c> + <c>Mfa:DemoUser:TotpSecret</c>
    /// en Base32). Permite demostrar login (HU-019) + Challenge→verify→Allow sin enrolamiento
    /// manual. Si la sección no está configurada, no se siembra nada.
    /// </summary>
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
            // Credencial local para /auth/login (HU-019): hash bcrypt factor 12 (SRS §3.6).
            PasswordHash  = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            KeycloakSub   = null!,
            IsActive      = true,
            IsInteractive = true,
            MfaEnabled    = true,
            TotpSecret    = _totpProtector.Protect(totpSecret.Trim()),
        });
    }

    /// <summary>
    /// HU-016 / T-089: siembra un baseline de comportamiento SINTÉTICO para el usuario de demo, de modo
    /// que el modelo de anomalías tenga historial suficiente que aprender desde el primer arranque.
    /// <para>
    /// Sin esto, el detector devuelve 50 neutro hasta acumular <c>MinTrainingSamples</c> accesos reales;
    /// y conseguirlos a base de una ráfaga produce un baseline degenerado que le enseña al modelo que el
    /// tráfico en ráfaga es normal — lo contrario de lo que HU-034 necesita medir.
    /// </para>
    /// <para>
    /// Solo se activa con <c>AnomalyDetection:DemoBaseline:Enabled=true</c> (declarado únicamente en
    /// <c>appsettings.Development.json</c>) y es idempotente: si el usuario ya tiene perfil, no lo pisa.
    /// El baseline es reproducible por semilla; debe declararse como sintético en la tesis.
    /// </para>
    /// </summary>
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

        // Idempotente: nunca sobrescribir un perfil existente (sintético previo o construido en vivo).
        if (await _db.UserBehaviorProfiles.AnyAsync(p => p.UserId == user.Id, ct))
            return;

        var days = _configuration.GetValue("AnomalyDetection:DemoBaseline:Days", 14);
        var seed = _configuration.GetValue("AnomalyDetection:DemoBaseline:Seed", 20260731);

        // La jornada modelada es configurable porque DEFINE qué considera normal el modelo: la hora del
        // acceso es una de las cuatro features. Un perfil 9–18 marca como anómalo cualquier acceso
        // nocturno — que es justo lo que HU-034 quiere detectar en el Escenario 2, pero también lo que
        // dispara falsos positivos si los casos legítimos del banco se ejecutan fuera de esa ventana.
        // OJO: las horas se interpretan en UTC, porque el motor evalúa con DateTimeOffset.UtcNow
        // (RiskEvaluationMiddleware). Para modelar una jornada de 9–18 en Santo Domingo (UTC-4) hay
        // que configurar 13–22. El default 9–18 UTC equivale a 5:00–14:00 hora local.
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

        // La caché de Redis (profile:{userId}, TTL 1 h) tiene PRIORIDAD sobre PostgreSQL en
        // UserProfileStore. Sembrar solo en la base deja al motor puntuando contra el perfil viejo
        // hasta que la clave expire — verificado en vivo: tras resembrar, el detector seguía viendo
        // los accesos de una ráfaga anterior y no aplicaba la degradación por falta de datos.
        // Se reescribe la caché con el baseline recién creado para que ambos niveles queden coherentes
        // desde el primer request.
        var perfilParaCache = new UserAnomalyProfile(
            baseline.TrainingWindow, baseline.RecentAccesses, baseline.AccessCount, isColdStart);

        await _redis.CacheProfileAsync(
            user.Id.Value.ToString(),
            UserProfileSerializer.SerializeForCache(perfilParaCache),
            TimeSpan.FromMinutes(_anomalyOptions.ProfileCacheTtlMinutes));
    }
}
