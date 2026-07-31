using System.Text.Json;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Infrastructure.AnomalyDetection;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Profiles;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Perfil de comportamiento por usuario (HU-026 / T-054) bajo
/// <c>GET /api/v1/users/{id}/profile</c>. Endpoint dedicado y de solo lectura, separado del
/// <c>UsersEndpoints</c> (gestión de usuarios) para no mezclar responsabilidades ni tocarlo.
/// <para>
/// Devuelve el perfil de <c>user_behavior_profiles</c> con el feature_vector decodificado
/// (reutilizando el serializer canónico + <see cref="BehaviorProfileProjection"/>) y los últimos
/// 10 accesos de <c>audit_logs</c>. En cold-start no se fabrica feature vector: se marca el estado.
/// </para>
/// <para>Autorización: <c>ReadAccess</c> (ADMIN|VIEWER). Sin caché.</para>
/// </summary>
public static class UserProfileEndpoints
{
    private const int RecentAccessCount = 10;

    public static IEndpointRouteBuilder MapUserProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/{id:guid}/profile", Get)
            .RequireAuthorization("ReadAccess")
            .WithTags("UserProfiles")
            .WithName("GetUserProfile")
            .WithSummary("Obtener el perfil de comportamiento aprendido de un usuario")
            .Produces<UserProfileDto>(StatusCodes.Status200OK)
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> Get(
        Guid id, OmakaseDbContext db, IOutputSanitizer enc, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var userId = UserId.From(id);

        var user = await db.Users.AsNoTracking()
            .Include(u => u.BehaviorProfile)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Usuario '{id}' no encontrado.", TraceId()));

        var recentAccesses = await LoadRecentAccessesAsync(db, userId, ct);

        var profile = user.BehaviorProfile;

        // Cold-start o perfil sin entrenar: se indica el estado de arranque en frío y NO se fabrica
        // un feature vector (2º criterio de aceptación). Igual se devuelven los contadores y accesos.
        if (profile is null || profile.IsColdStart || profile.FeatureVector is null)
        {
            return Results.Ok(new UserProfileDto(
                user.Id.Value,
                enc.Sanitize(user.Username),
                profile?.AccessCount ?? 0,
                IsColdStart: true,
                profile?.BaseRiskPenalty ?? 0m,
                profile?.LastTrainedAt,
                FeatureVector: null,
                recentAccesses));
        }

        // Decodifica el feature_vector con el serializer canónico (DRY) y lo proyecta a un
        // representativo (centroide promedio + hora habitual). Si la ventana está vacía → null.
        var (window, _) = UserProfileSerializer.Deserialize(profile.FeatureVector);
        var summary = BehaviorProfileProjection.Summarize(window);

        var featureVector = summary is { } s
            ? new FeatureVectorDto(s.HourSin, s.HourCos, s.Frequency, s.Diversity, s.TypicalHour)
            : null;

        return Results.Ok(new UserProfileDto(
            user.Id.Value,
            enc.Sanitize(user.Username),
            profile.AccessCount,
            profile.IsColdStart,
            profile.BaseRiskPenalty,
            profile.LastTrainedAt,
            featureVector,
            recentAccesses));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<List<RecentAccessDto>> LoadRecentAccessesAsync(
        OmakaseDbContext db, UserId userId, CancellationToken ct)
    {
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.EvaluatedAt)
            .Take(RecentAccessCount)
            .Select(a => new { a.EvaluatedAt, a.Verdict, a.RiskScore, a.SourceIp, a.Geo })
            .ToListAsync(ct);

        return rows
            .Select(a => ToRecentAccess(a.EvaluatedAt, a.Verdict, a.RiskScore, a.SourceIp, a.Geo))
            .ToList();
    }

    private static RecentAccessDto ToRecentAccess(
        DateTimeOffset evaluatedAt, Verdict verdict, decimal riskScore, string sourceIp, JsonDocument? geo)
    {
        string? country = null, city = null;
        if (geo is not null)
        {
            var root = geo.RootElement;
            if (root.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.String)
                country = c.GetString();
            if (root.TryGetProperty("city", out var ci) && ci.ValueKind == JsonValueKind.String)
                city = ci.GetString();
        }

        return new RecentAccessDto(evaluatedAt, verdict.ToString(), riskScore, sourceIp, country, city);
    }

    private static string TraceId() =>
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A";
}
