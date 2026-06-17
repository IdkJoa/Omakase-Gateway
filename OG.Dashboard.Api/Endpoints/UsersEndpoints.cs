using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Users;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints mock de usuarios y perfiles de comportamiento.
/// GET /api/v1/users — Vista administrativa de usuarios con roles y perfil de comportamiento.
/// </summary>
public static class UsersEndpoints
{
    // ── Datos mock ────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<UserDto> MockUsers =
    [
        new(
            Id: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Username: "admin.omakase",
            UserType: "SECURITY_OFFICER",
            IsActive: true,
            FailedAttempts: 0,
            LockedUntil: null,
            CreatedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt: null,
            Roles: ["Admin"],
            BehaviorProfile: new UserBehaviorSummaryDto(
                AccessCount: 580,
                LastAccessAt: new DateTimeOffset(2026, 5, 29, 14, 30, 0, TimeSpan.Zero),
                AvgRequestsPerHour: 12.4,
                UniqueEndpointsCount: 18)
        ),
        new(
            Id: Guid.Parse("11111111-aaaa-bbbb-cccc-dddddddddddd"),
            Username: "viewer.security",
            UserType: "SECURITY_OFFICER",
            IsActive: true,
            FailedAttempts: 0,
            LockedUntil: null,
            CreatedAt: new DateTimeOffset(2026, 4, 10, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt: null,
            Roles: ["Viewer"],
            BehaviorProfile: new UserBehaviorSummaryDto(
                AccessCount: 210,
                LastAccessAt: new DateTimeOffset(2026, 5, 29, 10, 0, 0, TimeSpan.Zero),
                AvgRequestsPerHour: 4.2,
                UniqueEndpointsCount: 7)
        ),
        new(
            Id: Guid.Parse("22222222-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Username: "jperez",
            UserType: "CLIENT_USER",
            IsActive: true,
            FailedAttempts: 0,
            LockedUntil: null,
            CreatedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt: null,
            Roles: [],
            BehaviorProfile: new UserBehaviorSummaryDto(
                AccessCount: 142,
                LastAccessAt: new DateTimeOffset(2026, 5, 29, 14, 32, 0, TimeSpan.Zero),
                AvgRequestsPerHour: 8.3,
                UniqueEndpointsCount: 5)
        ),
        new(
            Id: Guid.Parse("33333333-cccc-dddd-eeee-ffffffffffff"),
            Username: "mgarcia",
            UserType: "CLIENT_USER",
            IsActive: true,
            FailedAttempts: 3,
            LockedUntil: null,
            CreatedAt: new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 5, 29, 14, 28, 0, TimeSpan.Zero),
            Roles: [],
            BehaviorProfile: new UserBehaviorSummaryDto(
                AccessCount: 7,
                LastAccessAt: new DateTimeOffset(2026, 5, 29, 14, 28, 0, TimeSpan.Zero),
                AvgRequestsPerHour: 21.6,
                UniqueEndpointsCount: 12)
        ),
        new(
            Id: Guid.Parse("44444444-dddd-eeee-ffff-aaaaaaaaaaaa"),
            Username: "lrodriguez",
            UserType: "CLIENT_USER",
            IsActive: false,
            FailedAttempts: 5,
            LockedUntil: new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero),
            CreatedAt: new DateTimeOffset(2026, 5, 10, 11, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 5, 29, 14, 15, 0, TimeSpan.Zero),
            Roles: [],
            BehaviorProfile: new UserBehaviorSummaryDto(
                AccessCount: 89,
                LastAccessAt: new DateTimeOffset(2026, 5, 29, 14, 15, 0, TimeSpan.Zero),
                AvgRequestsPerHour: 3.1,
                UniqueEndpointsCount: 4)
        ),
    ];

    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/users")
            .WithTags("Users")
            .WithOpenApi();

        // GET /api/v1/users
        group.MapGet("/", GetAll)
            .WithName("GetUsers")
            .WithSummary("Listar usuarios del sistema")
            .WithDescription(
                "Devuelve todos los usuarios (Security Officers y Client Users) " +
                "con sus roles asignados y resumen de perfil de comportamiento. " +
                "Soporta filtro por userType e isActive.");

        // GET /api/v1/users/{id}
        group.MapGet("/{id:guid}", GetById)
            .WithName("GetUserById")
            .WithSummary("Obtener detalle completo de un usuario por ID");

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetAll(
        int page = 1,
        int pageSize = 25,
        string? userType = null,
        bool? isActive = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 25;

        var filtered = MockUsers.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(userType))
            filtered = filtered.Where(u => u.UserType.Equals(userType, StringComparison.OrdinalIgnoreCase));

        if (isActive.HasValue)
            filtered = filtered.Where(u => u.IsActive == isActive.Value);

        var list = filtered.ToList();
        var data = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Results.Ok(new PagedResponse<UserDto>(page, pageSize, list.Count, data));
    }

    private static IResult GetById(Guid id)
    {
        var user = MockUsers.FirstOrDefault(u => u.Id == id);
        if (user is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Usuario '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.Ok(user);
    }
}
