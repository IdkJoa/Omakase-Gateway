using System.Text.Json;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.AnomalyDetection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Datos de demostración: servicios, políticas, usuarios cliente y auditoría para que el Dashboard
/// muestre métricas reales desde el primer arranque en vez de tablas vacías.
/// </summary>
/// <remarks>
/// Separada de <see cref="OmakaseDbSeeder"/> (que siembra solo lo mínimo para funcionar) porque esta
/// escenografía no debe existir fuera de desarrollo; se activa solo con
/// <c>Demo:SeedExtendedData:Enabled=true</c> en <c>appsettings.Development.json</c>. Es idempotente:
/// cada bloque comprueba si su material ya está sembrado antes de insertar.
/// </remarks>
public sealed class DemoDataSeeder : IDbSeeder
{
    /// <summary>Semilla fija: identificadores, marcas de tiempo y puntajes salen siempre iguales.</summary>
    private const int Seed = 20260806;

    private const int HistoryDays = 7;
    private const int HistoryEvaluations = 60;

    private readonly OmakaseDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly BehaviorBaselineBootstrapper _baselineBootstrapper;
    private readonly IRedisService _redis;
    private readonly AnomalyDetectionOptions _anomalyOptions;

    public DemoDataSeeder(
        OmakaseDbContext db,
        IConfiguration configuration,
        BehaviorBaselineBootstrapper baselineBootstrapper,
        IRedisService redis,
        AnomalyDetectionOptions anomalyOptions)
    {
        _db = db;
        _configuration = configuration;
        _baselineBootstrapper = baselineBootstrapper;
        _redis = redis;
        _anomalyOptions = anomalyOptions;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_configuration.GetValue("Demo:SeedExtendedData:Enabled", false))
            return;

        // access_policies.created_by es obligatorio y depende del administrador sembrado por OmakaseDbSeeder.
        var admin = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Type == UserType.SecurityOfficer, cancellationToken);

        if (admin is null)
            return;

        await SeedPoliciesAsync(admin.Id, cancellationToken);
        await SeedServicesAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await SeedServicePoliciesAsync(cancellationToken);
        await SeedClientUsersAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await SeedBehaviorProfilesAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await SeedAuditHistoryAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    // ── Políticas ────────────────────────────────────────────────────────────

    private static readonly (string Name, PolicyType Type, string Config)[] DemoPolicies =
    [
        ("Demo · Geofencing de países denegados", PolicyType.Geofence,
            """{"denied_countries":["JP","RU","KP"]}"""),

        // Ventana deliberadamente permisiva: la demo no puede depender de la hora a la que se presente;
        // estrecharla desde el Dashboard es justo lo que HU-025 permite demostrar.
        ("Demo · Ventana horaria de servicio", PolicyType.TimeWindow,
            """{"start_time":"00:00","end_time":"23:59","timezone":"AST"}"""),

        ("Demo · Huella de dispositivo conocida", PolicyType.Fingerprint, "{}"),
        ("Demo · Viaje imposible", PolicyType.ImpossibleTravel, "{}"),
    ];

    private async Task SeedPoliciesAsync(UserId createdBy, CancellationToken ct)
    {
        var existentes = await _db.AccessPolicies
            .AsNoTracking()
            .Select(p => p.Name)
            .ToListAsync(ct);

        foreach (var (name, type, config) in DemoPolicies)
        {
            if (existentes.Contains(name))
                continue;

            _db.AccessPolicies.Add(new AccessPolicy
            {
                Id          = AccessPolicyId.New(),
                Name        = name,
                Type        = type,
                Config      = JsonDocument.Parse(config),
                Weight      = 1.000m,
                IsActive    = true,
                CreatedById = createdBy,
            });
        }
    }

    // ── Servicios protegidos ─────────────────────────────────────────────────

    // httpbin (sembrado por OmakaseDbSeeder) queda deliberadamente sin políticas: es el destino de las
    // pruebas de carga y su medición debe reflejar el coste del motor, no el de las reglas.
    private static readonly (string Name, string Upstream, bool RequiresAuth)[] DemoServices =
    [
        ("reportes", "https://httpbin.org/anything", false),
        ("nomina",   "https://httpbin.org/anything", true),
    ];

    private async Task SeedServicesAsync(CancellationToken ct)
    {
        var existentes = await _db.ProtectedServices
            .AsNoTracking()
            .Select(s => s.Name)
            .ToListAsync(ct);

        foreach (var (name, upstream, requiresAuth) in DemoServices)
        {
            if (existentes.Contains(name))
                continue;

            _db.ProtectedServices.Add(new ProtectedService
            {
                Id           = ProtectedServiceId.New(),
                Name         = name,
                UpstreamUrl  = upstream,
                RequiresAuth = requiresAuth,
                IsActive     = true,
            });
        }
    }

    // Nómina exige las cuatro comprobaciones; reportes solo las dos que no dependen de hora ni desplazamiento (SRS §7.8).
    private async Task SeedServicePoliciesAsync(CancellationToken ct)
    {
        var asignaciones = new Dictionary<string, string[]>
        {
            ["nomina"]   = DemoPolicies.Select(p => p.Name).ToArray(),
            ["reportes"] = [DemoPolicies[0].Name, DemoPolicies[2].Name],
        };

        var servicios = await _db.ProtectedServices
            .Where(s => asignaciones.Keys.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, s => s.Id, ct);

        var nombresPolitica = DemoPolicies.Select(p => p.Name).ToArray();
        var politicas = await _db.AccessPolicies
            .Where(p => nombresPolitica.Contains(p.Name))
            .ToDictionaryAsync(p => p.Name, p => p.Id, ct);

        var yaAsociadas = await _db.ServicePolicies
            .AsNoTracking()
            .Select(sp => new { sp.ServiceId, sp.PolicyId })
            .ToListAsync(ct);

        foreach (var (servicio, politicasDelServicio) in asignaciones)
        {
            if (!servicios.TryGetValue(servicio, out var serviceId))
                continue;

            foreach (var nombrePolitica in politicasDelServicio)
            {
                if (!politicas.TryGetValue(nombrePolitica, out var policyId))
                    continue;

                if (yaAsociadas.Any(a => a.ServiceId == serviceId && a.PolicyId == policyId))
                    continue;

                _db.ServicePolicies.Add(new ServicePolicy
                {
                    Id        = ServicePolicyId.New(),
                    ServiceId = serviceId,
                    PolicyId  = policyId,
                    IsEnabled = true,
                });
            }
        }
    }

    // ── Usuarios cliente ─────────────────────────────────────────────────────

    // svc.integracion se marca como no interactivo a propósito: es cuenta de servicio, no puede completar
    // MFA, y su veredicto de desafío escala a bloqueo (SRS §3.6) — demuestra ese camino sin prepararlo a mano.
    private static readonly (string Username, bool Interactive, int ProfileSeed)[] DemoClients =
    [
        ("cliente.ventas",    true,  Seed + 11),
        ("cliente.soporte",   true,  Seed + 22),
        ("cliente.finanzas",  true,  Seed + 33),
        ("svc.integracion",   false, Seed + 44),
    ];

    private async Task SeedClientUsersAsync(CancellationToken ct)
    {
        var password = _configuration["Demo:SeedExtendedData:ClientPassword"];
        if (string.IsNullOrWhiteSpace(password))
            return;

        var nombres = DemoClients.Select(c => c.Username).ToArray();
        var existentes = await _db.Users
            .AsNoTracking()
            .Where(u => nombres.Contains(u.Username))
            .Select(u => u.Username)
            .ToListAsync(ct);

        foreach (var (username, interactive, _) in DemoClients)
        {
            if (existentes.Contains(username))
                continue;

            _db.Users.Add(new User
            {
                Id            = UserId.New(),
                Username      = username,
                Type          = UserType.Client,
                PasswordHash  = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
                KeycloakSub   = null!,
                IsActive      = true,
                IsInteractive = interactive,
                MfaEnabled    = false,
            });
        }
    }

    // Cada cliente usa su propia semilla para no ser copia del mismo patrón; sin perfil el motor
    // los trataría a todos como arranque en frío.
    private async Task SeedBehaviorProfilesAsync(CancellationToken ct)
    {
        var nombres = DemoClients.Select(c => c.Username).ToArray();
        var usuarios = await _db.Users
            .AsNoTracking()
            .Where(u => nombres.Contains(u.Username))
            .ToDictionaryAsync(u => u.Username, u => u.Id, ct);

        var conPerfil = await _db.UserBehaviorProfiles
            .AsNoTracking()
            .Select(p => p.UserId)
            .ToListAsync(ct);

        var coldStartN = (await _db.RiskScoreConfigs.AsNoTracking().FirstOrDefaultAsync(ct))?.ColdStartN ?? 10;

        foreach (var (username, _, profileSeed) in DemoClients)
        {
            if (!usuarios.TryGetValue(username, out var userId) || conPerfil.Contains(userId))
                continue;

            var baseline = _baselineBootstrapper.Build(DateTimeOffset.UtcNow, HistoryDays * 2, profileSeed);
            var isColdStart = baseline.AccessCount < coldStartN;

            _db.UserBehaviorProfiles.Add(new UserBehaviorProfile
            {
                Id              = UserBehaviorProfileId.New(),
                UserId          = userId,
                FeatureVector   = UserProfileSerializer.Serialize(baseline.TrainingWindow, baseline.RecentAccesses),
                AccessCount     = baseline.AccessCount,
                IsColdStart     = isColdStart,
                BaseRiskPenalty = 0m,
                LastTrainedAt   = DateTimeOffset.UtcNow,
            });

            // Redis tiene prioridad sobre PostgreSQL en UserProfileStore: sembrar solo en la base dejaría
            // al motor puntuando contra un perfil ausente hasta que expire la caché.
            await _redis.CacheProfileAsync(
                userId.Value.ToString(),
                UserProfileSerializer.SerializeForCache(new UserAnomalyProfile(
                    baseline.TrainingWindow, baseline.RecentAccesses, baseline.AccessCount, isColdStart)),
                TimeSpan.FromMinutes(_anomalyOptions.ProfileCacheTtlMinutes));
        }
    }

    // ── Historial de auditoría ───────────────────────────────────────────────

    private sealed record Origen(string Ip, string Country, string City, double Lat, double Lon);

    // Cada dirección se comprobó contra el mismo proveedor de geolocalización que consulta el motor,
    // para que el historial sembrado no contradiga una fila recién evaluada.
    private static readonly Origen[] Origenes =
    [
        new("190.166.12.45", "DO", "Santo Domingo", 18.4861, -69.9312),
        new("200.88.8.11",   "DO", "Santo Domingo", 18.4861, -69.9312),
        new("23.20.0.1",     "US", "Ashburn",       39.0438, -77.4874),
        new("80.30.15.4",    "ES", "Chiclana",      36.4194,  -6.1464),
        new("153.121.36.1",  "JP", "Tokio",         35.6762, 139.6503),
    ];

    private static readonly string[] Agentes =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
        "OmakaseMobileClient/1.2.0 (Android 14; Mobile)",
    ];

    // Cada fila aplica la MISMA fórmula y los MISMOS umbrales que el motor real, así que el desglose
    // cuadra con el veredicto; idempotente porque el identificador de evaluación es determinista.
    private async Task SeedAuditHistoryAsync(CancellationToken ct)
    {
        var primeraEvaluacion = DeterministicGuid(0);
        if (await _db.AuditLogs.AnyAsync(a => a.EvaluationId == primeraEvaluacion, ct))
            return;

        var config = await _db.RiskScoreConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (config is null)
            return;

        var clientes = await _db.Users
            .AsNoTracking()
            .Where(u => u.Type == UserType.Client)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var servicios = await _db.ProtectedServices
            .AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (clientes.Count == 0 || servicios.Count == 0)
            return;

        var rnd = new Random(Seed);
        var ahora = DateTimeOffset.UtcNow;

        for (var i = 0; i < HistoryEvaluations; i++)
        {
            var origen = Origenes[rnd.Next(Origenes.Length)];
            var reglas = new List<object>();

            // Reparto intencionado: mayoría de tráfico limpio, minoría con huella desconocida y unos
            // pocos desde país denegado, para que la distribución del Dashboard sea legible.
            decimal policyScore = 0m;
            if (origen.Country == "JP")
            {
                policyScore = 100m;
                reglas.Add(new { rule = "GEOFENCE", score = 100m, detail = $"denied:{origen.Country}" });
            }
            else if (rnd.NextDouble() < 0.18)
            {
                policyScore = 50m;
                reglas.Add(new { rule = "FINGERPRINT", score = 50m, detail = "unknown_device" });
            }

            var anomalyScore = Math.Round((decimal)(rnd.NextDouble() < 0.80
                ? rnd.Next(4, 34)
                : rnd.Next(58, 96)),
                2);

            var riskScore = Math.Clamp(
                config.PolicyWeight * policyScore + config.AnomalyWeight * anomalyScore, 0m, 100m);

            var verdict = riskScore <= config.ChallengeThreshold ? Verdict.Allow
                        : riskScore <= config.BlockThreshold ? Verdict.Challenge
                        : Verdict.Block;

            _db.AuditLogs.Add(new AuditLog
            {
                Id              = AuditLogId.New(),
                EvaluationId    = DeterministicGuid(i),
                UserId          = clientes[rnd.Next(clientes.Count)],
                ServiceId       = servicios[rnd.Next(servicios.Count)],
                SourceIp        = origen.Ip,
                Geo             = JsonSerializer.SerializeToDocument(new
                {
                    country = origen.Country,
                    city    = origen.City,
                    lat     = origen.Lat,
                    lon     = origen.Lon,
                }),
                UserAgent       = Agentes[rnd.Next(Agentes.Length)],
                FingerprintHash = Convert.ToHexString(DeterministicGuid(1000 + i).ToByteArray())[..32].ToLowerInvariant(),
                PolicyScore     = policyScore,
                AnomalyScore    = anomalyScore,
                RiskScore       = Math.Round(riskScore, 2),
                Verdict         = verdict,
                TriggeredRules  = JsonSerializer.SerializeToDocument(reglas),
                EvaluatedAt     = ahora.AddMinutes(-rnd.Next(1, HistoryDays * 24 * 60)),
            });
        }
    }

    // Identificador estable a partir de semilla+índice: si la primera evaluación ya existe, el material está sembrado.
    private static Guid DeterministicGuid(int index)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(Seed).CopyTo(bytes, 0);
        BitConverter.GetBytes(index).CopyTo(bytes, 4);
        BitConverter.GetBytes(index * 2654435761L).CopyTo(bytes, 8);
        return new Guid(bytes);
    }
}
