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
/// Datos de demostración para la defensa (HU-036 / T-077): tres servicios protegidos, cuatro
/// políticas deterministas con sus asociaciones, cinco usuarios cliente con perfil de
/// comportamiento y un historial de auditoría, de modo que el Dashboard muestre métricas
/// reales desde el primer arranque en vez de tablas vacías.
/// <para>
/// <b>Por qué es una clase aparte.</b> <see cref="OmakaseDbSeeder"/> siembra el mínimo que el
/// sistema necesita para funcionar: configuración del motor, roles, un administrador y el
/// servicio de ejemplo. Lo de aquí es escenografía de demostración, que no debe existir en
/// ningún entorno que no sea el de desarrollo. Separarlas deja el arranque productivo intacto
/// y permite añadir o quitar material de demo sin tocar el camino crítico.
/// </para>
/// <para>
/// <b>Reproducible e idempotente.</b> Todo se deriva de una semilla fija, así que dos entornos
/// limpios obtienen exactamente los mismos datos, y cada bloque comprueba antes si su material
/// ya está sembrado. Volver a arrancar no duplica nada.
/// </para>
/// <para>
/// Se activa solo con <c>Demo:SeedExtendedData:Enabled=true</c>, declarado únicamente en
/// <c>appsettings.Development.json</c>.
/// </para>
/// </summary>
public sealed class DemoDataSeeder : IDbSeeder
{
    /// <summary>
    /// Semilla del material de demostración. Fija la reproducibilidad: identificadores,
    /// marcas de tiempo y puntajes salen siempre iguales para un mismo día de referencia.
    /// </summary>
    private const int Seed = 20260806;

    /// <summary>Días de historial de auditoría que se sintetizan hacia atrás.</summary>
    private const int HistoryDays = 7;

    /// <summary>Evaluaciones históricas a sembrar. El backlog pide más de cincuenta.</summary>
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

        // El material de demostración cuelga del administrador sembrado por OmakaseDbSeeder:
        // access_policies.created_by es obligatorio. Si aún no existe, no hay nada que hacer.
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

    /// <summary>Nombre de cada política de demostración; también su clave de idempotencia.</summary>
    private static readonly (string Name, PolicyType Type, string Config)[] DemoPolicies =
    [
        ("Demo · Geofencing de países denegados", PolicyType.Geofence,
            """{"denied_countries":["JP","RU","KP"]}"""),

        // Ventana deliberadamente permisiva: el material de demostración no puede depender de
        // la hora a la que se defienda el trabajo. Para ver la regla disparar, basta con
        // estrecharla desde el Dashboard, que es justamente lo que HU-025 permite demostrar.
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

    /// <summary>
    /// Los dos servicios que faltan para llegar a los tres del backlog. Ilustran el motivo por
    /// el que <c>service_policies</c> existe: cada servicio aplica su propio conjunto de reglas.
    /// <para>
    /// <c>httpbin</c>, sembrado por <see cref="OmakaseDbSeeder"/>, se deja deliberadamente sin
    /// políticas asociadas: es el destino de las pruebas de carga, cuya medición debe reflejar
    /// el coste del motor y no el de las reglas.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Asocia reglas distintas a servicios distintos, que es el caso de uso que el SRS §7.8
    /// pone como ejemplo: la nómina exige las cuatro comprobaciones, mientras que el servicio
    /// de reportes se conforma con las dos que no dependen de la hora ni del historial de
    /// desplazamiento.
    /// </summary>
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

    /// <summary>
    /// Cuatro usuarios cliente que, con <c>demo.cliente</c> de <see cref="OmakaseDbSeeder"/>,
    /// completan los cinco del backlog.
    /// <para>
    /// <c>svc.integracion</c> se marca como no interactivo a propósito: es una cuenta de
    /// servicio, no puede completar un segundo factor, y su veredicto de desafío escala a
    /// bloqueo (SRS §3.6). Tenerla sembrada permite demostrar ese camino sin prepararlo a mano.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Da a cada usuario cliente de demostración un perfil de comportamiento sintético con su
    /// propia semilla, de modo que no sean copias del mismo patrón. Sin perfil, el motor los
    /// trataría a todos como arranque en frío y el Dashboard mostraría una única forma repetida.
    /// </summary>
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

            // La caché de Redis tiene prioridad sobre PostgreSQL en UserProfileStore: sembrar
            // solo en la base dejaría al motor puntuando contra un perfil ausente hasta que la
            // clave expirara. Se escriben los dos niveles a la vez.
            await _redis.CacheProfileAsync(
                userId.Value.ToString(),
                UserProfileSerializer.SerializeForCache(new UserAnomalyProfile(
                    baseline.TrainingWindow, baseline.RecentAccesses, baseline.AccessCount, isColdStart)),
                TimeSpan.FromMinutes(_anomalyOptions.ProfileCacheTtlMinutes));
        }
    }

    // ── Historial de auditoría ───────────────────────────────────────────────

    private sealed record Origen(string Ip, string Country, string City, double Lat, double Lon);

    /// <summary>
    /// Orígenes del historial sintético. Cada dirección se comprobó contra el mismo proveedor
    /// de geolocalización que consulta el motor, de modo que el país y la ciudad que aquí se
    /// escriben coinciden con los que resolvería una petición real desde esa dirección. Sin esa
    /// comprobación el historial diría una cosa y el sistema en vivo otra, y la contradicción
    /// aparecería justo al comparar una fila sembrada con una recién evaluada.
    /// </summary>
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

    /// <summary>
    /// Sintetiza evaluaciones pasadas para que el panel de métricas y el explorador de logs
    /// tengan algo que mostrar en la defensa.
    /// <para>
    /// Los puntajes no son aleatorios sueltos: cada fila se construye eligiendo un puntaje de
    /// política y otro de anomalía y aplicando después la MISMA fórmula y los MISMOS umbrales
    /// que usa el motor. Si un evaluador cruza el desglose con el veredicto, cuadra; datos de
    /// relleno incoherentes serían peor que no tener datos.
    /// </para>
    /// <para>Idempotente por el identificador de evaluación, que es determinista.</para>
    /// </summary>
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

            // Reparto intencionado: la mayoría del tráfico es limpio, una minoría presenta una
            // huella desconocida y unos pocos casos llegan desde un país denegado. Es el perfil
            // que un sistema real produce y el que hace legible la distribución del Dashboard.
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
                ? rnd.Next(4, 34)        // dentro del perfil aprendido
                : rnd.Next(58, 96)),     // desviación de conducta
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

    /// <summary>
    /// Identificador estable a partir de la semilla y un índice. Permite que el bloque de
    /// auditoría sea idempotente sin necesidad de una marca aparte: si la primera evaluación
    /// ya existe, el material está sembrado.
    /// </summary>
    private static Guid DeterministicGuid(int index)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(Seed).CopyTo(bytes, 0);
        BitConverter.GetBytes(index).CopyTo(bytes, 4);
        BitConverter.GetBytes(index * 2654435761L).CopyTo(bytes, 8);
        return new Guid(bytes);
    }
}
