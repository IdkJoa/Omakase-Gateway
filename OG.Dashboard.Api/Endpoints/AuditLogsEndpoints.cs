using System.Text.Json;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Contracts.AuditLogs;
using OG.Dashboard.Api.Contracts.Common;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Exploración de logs de auditoría (HU-022) sobre <c>audit_logs</c> REAL (SRS §4.1).
/// <list type="bullet">
///   <item><c>GET /api/v1/logs</c> — lista paginada con filtros (verdict, userId, rango de fechas, IP, servicio).</item>
///   <item><c>GET /api/v1/logs/{evaluationId}</c> — detalle de una evaluación.</item>
/// </list>
/// Reemplaza el mock de Contract-First (T-088) cableándolo a datos reales, manteniendo el MISMO
/// contrato para que la vista de logs del front no cambie. Solo lectura (<c>ReadAccess</c>).
/// </summary>
public static class AuditLogsEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapAuditLogsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/logs")
            .WithTags("Audit Logs")
            .WithOpenApi();

        group.MapGet("/", GetLogs)
            .RequireAuthorization("ReadAccess")
            .WithName("GetAuditLogs")
            .WithSummary("Listar logs de auditoría paginados (datos reales)")
            .WithDescription(
                "Lista paginada de audit_logs con filtros opcionales por veredicto, userId, rango de " +
                "fechas, IP de origen y servicio. Más reciente primero. Contrato SRS §4.1.");

        group.MapGet("/{evaluationId:guid}", GetLogById)
            .RequireAuthorization("ReadAccess")
            .WithName("GetAuditLogById")
            .WithSummary("Obtener detalle de un log de auditoría por EvaluationId");

        return app;
    }

    private static async Task<IResult> GetLogs(
        OmakaseDbContext db,
        IOutputSanitizer enc,
        int page = 1,
        int pageSize = DefaultPageSize,
        string? verdict = null,
        string? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? sourceIp = null,
        string? serviceName = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(verdict) && Enum.TryParse<Verdict>(verdict, ignoreCase: true, out var v))
            query = query.Where(a => a.Verdict == v);

        if (!string.IsNullOrWhiteSpace(userId) && Guid.TryParse(userId, out var uid))
        {
            var typed = UserId.From(uid);
            query = query.Where(a => a.UserId == typed);
        }

        if (from.HasValue) query = query.Where(a => a.EvaluatedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.EvaluatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(sourceIp)) query = query.Where(a => a.SourceIp == sourceIp);
        if (!string.IsNullOrWhiteSpace(serviceName))
            query = query.Where(a => a.ProtectedService != null && a.ProtectedService.Name == serviceName);

        var totalRecords = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(a => a.EvaluatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new LogRow(
                a.EvaluationId,
                a.EvaluatedAt,
                a.UserId,
                a.User != null ? a.User.Username : null,
                a.ProtectedService != null ? a.ProtectedService.Name : null,
                a.SourceIp,
                a.Geo,
                a.UserAgent,
                a.PolicyScore,
                a.AnomalyScore,
                a.RiskScore,
                a.Verdict,
                a.TriggeredRules))
            .ToListAsync(ct);

        var data = rows.Select(r => ToDto(r, enc)).ToList();
        return Results.Ok(new PagedResponse<AuditLogDto>(page, pageSize, totalRecords, data));
    }

    private static async Task<IResult> GetLogById(Guid evaluationId, OmakaseDbContext db, IOutputSanitizer enc, CancellationToken ct)
    {
        var row = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EvaluationId == evaluationId)
            .Select(a => new LogRow(
                a.EvaluationId,
                a.EvaluatedAt,
                a.UserId,
                a.User != null ? a.User.Username : null,
                a.ProtectedService != null ? a.ProtectedService.Name : null,
                a.SourceIp,
                a.Geo,
                a.UserAgent,
                a.PolicyScore,
                a.AnomalyScore,
                a.RiskScore,
                a.Verdict,
                a.TriggeredRules))
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return Results.NotFound(new ErrorResponse(
                "NOT_FOUND",
                $"No se encontró un log con EvaluationId '{evaluationId}'.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.Ok(ToDto(row, enc));
    }

    private sealed record LogRow(
        Guid EvaluationId, DateTimeOffset EvaluatedAt, UserId? UserId, string? Username, string? ServiceName,
        string SourceIp, JsonDocument? Geo, string? UserAgent,
        decimal PolicyScore, decimal AnomalyScore, decimal RiskScore, Verdict Verdict, JsonDocument? TriggeredRules);

    private static AuditLogDto ToDto(LogRow a, IOutputSanitizer enc) => new(
        a.EvaluationId,
        a.EvaluatedAt,
        a.UserId is { } uid ? uid.Value.ToString() : null,
        a.Username is null ? null : enc.Sanitize(a.Username),
        a.ServiceName is null ? null : enc.Sanitize(a.ServiceName),
        a.SourceIp,
        ParseGeo(a.Geo),
        a.UserAgent is null ? null : enc.Sanitize(a.UserAgent),
        a.PolicyScore,
        a.AnomalyScore,
        a.RiskScore,
        a.Verdict.ToString().ToUpperInvariant(),
        ParseTriggeredRules(a.TriggeredRules));

    /// <summary>Decodifica el JSONB de geolocalización a <see cref="GeoDto"/>; null si no hay país/ciudad.</summary>
    private static GeoDto? ParseGeo(JsonDocument? geo)
    {
        if (geo is null) return null;
        var root = geo.RootElement;

        string Str(string k) => root.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        double? Num(string k) => root.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

        var country = Str("country");
        var city = Str("city");
        if (string.IsNullOrEmpty(country) && string.IsNullOrEmpty(city)) return null;

        return new GeoDto(country, city, Num("latitude"), Num("longitude"));
    }

    /// <summary>
    /// Extrae los nombres de regla del JSONB <c>triggered_rules</c>. Soporta el formato real del motor
    /// (<c>[{rule,score,detail}]</c>) y el legado (<c>["RULE"]</c>).
    /// </summary>
    private static IReadOnlyList<string> ParseTriggeredRules(JsonDocument? rules)
    {
        if (rules is null || rules.RootElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var el in rules.RootElement.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
                list.Add(el.GetString() ?? "");
            else if (el.ValueKind == JsonValueKind.Object
                     && el.TryGetProperty("rule", out var r) && r.ValueKind == JsonValueKind.String)
                list.Add(r.GetString() ?? "");
        }
        return list;
    }
}
