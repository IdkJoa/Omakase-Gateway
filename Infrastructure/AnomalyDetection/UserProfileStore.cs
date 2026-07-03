using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.Security;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Implementación de dos niveles del <see cref="IUserProfileStore"/> (HU-016 / T-033): resuelve el perfil
/// primero desde la caché Redis (<c>profile:{userId}</c>, caliente) y solo cae a PostgreSQL en fallo de
/// caché, calentándola después. Usa <see cref="OmakaseDbContext"/> (Scoped) → se registra como Scoped.
/// <para>Consume el <see cref="IRedisService"/> del equipo (HU-005); no toca su implementación.</para>
/// </summary>
public sealed class UserProfileStore : IUserProfileStore
{
    private readonly IRedisService _redis;
    private readonly OmakaseDbContext _db;
    private readonly AnomalyDetectionOptions _options;

    public UserProfileStore(IRedisService redis, OmakaseDbContext db, AnomalyDetectionOptions options)
    {
        _redis = redis;
        _db = db;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<UserAnomalyProfile?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        // 1. Ruta caliente: Redis, sin acceso a disco.
        var cached = await _redis.GetCachedProfileAsync(userId);
        var fromCache = UserProfileSerializer.DeserializeFromCache(cached);
        if (fromCache is not null)
            return fromCache;

        // 2. Ruta fría: PostgreSQL. El user_id (claim sub) es el GUID del UserId tipado.
        if (!Guid.TryParse(userId, out var guid))
            return null;

        var domainId = new UserId(guid);
        var entity = await _db.UserBehaviorProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == domainId, cancellationToken);

        if (entity is null)
            return null;

        var (window, recent) = UserProfileSerializer.Deserialize(entity.FeatureVector);
        var profile = new UserAnomalyProfile(window, recent, entity.AccessCount, entity.IsColdStart);

        // 3. Calentar Redis para próximas peticiones (TTL 1h por spec).
        await _redis.CacheProfileAsync(
            userId,
            UserProfileSerializer.SerializeForCache(profile),
            TimeSpan.FromMinutes(_options.ProfileCacheTtlMinutes));

        return profile;
    }
}
