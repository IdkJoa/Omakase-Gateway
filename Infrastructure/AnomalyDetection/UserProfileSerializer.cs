using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Common.RiskEngine.AnomalyDetection;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Serializa el perfil de anomalías del usuario hacia/desde el <c>feature_vector</c> (JSONB) de
/// <c>user_behavior_profiles</c> (HU-016 / T-033). Es la única pieza que conoce la forma del JSON,
/// aislada y pura para poder versionarla y probarla en round-trip.
/// <para><c>access_count</c>/<c>is_cold_start</c> NO viven aquí: son columnas propias de la entidad.</para>
/// </summary>
public static class UserProfileSerializer
{
    /// <summary>Versión del esquema del payload, para migraciones futuras sin romper perfiles viejos.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonDocument Serialize(
        IReadOnlyList<AnomalyFeatureVector> trainingWindow,
        IReadOnlyList<UserAccessSample> recentAccesses)
    {
        var payload = new ProfilePayload
        {
            Version = CurrentVersion,
            Window = trainingWindow.Select(v => v.ToArray()).ToList(),
            Recent = recentAccesses
                .Select(a => new AccessDto { Timestamp = a.Timestamp, Endpoint = a.Endpoint })
                .ToList(),
        };

        return JsonSerializer.SerializeToDocument(payload, SerializerOptions);
    }

    /// <summary>
    /// Reconstruye la ventana y los accesos desde el JSONB. Ante un documento nulo o malformado
    /// devuelve colecciones vacías (fail-safe: el detector lo tratará como cold-start, no revienta).
    /// </summary>
    public static (IReadOnlyList<AnomalyFeatureVector> Window, IReadOnlyList<UserAccessSample> Recent) Deserialize(
        JsonDocument? document)
    {
        if (document is null)
            return (Array.Empty<AnomalyFeatureVector>(), Array.Empty<UserAccessSample>());

        try
        {
            var payload = document.Deserialize<ProfilePayload>(SerializerOptions);
            if (payload is null)
                return (Array.Empty<AnomalyFeatureVector>(), Array.Empty<UserAccessSample>());

            var window = payload.Window
                .Where(a => a is { Length: AnomalyFeatureVector.Dimension })
                .Select(a => new AnomalyFeatureVector(a[0], a[1], a[2], a[3]))
                .ToArray();

            var recent = payload.Recent
                .Select(a => new UserAccessSample(a.Timestamp, a.Endpoint ?? string.Empty))
                .ToArray();

            return (window, recent);
        }
        catch (JsonException)
        {
            return (Array.Empty<AnomalyFeatureVector>(), Array.Empty<UserAccessSample>());
        }
    }

    /// <summary>
    /// Serializa el perfil COMPLETO (incluye <c>access_count</c>/<c>is_cold_start</c>) a string para la
    /// caché Redis <c>profile:{userId}</c>. A diferencia del JSONB de columna, aquí sí se incluyen esos
    /// escalares porque Redis guarda el perfil íntegro para servir la ruta crítica sin pegar a PostgreSQL.
    /// </summary>
    public static string SerializeForCache(UserAnomalyProfile profile)
    {
        var payload = new CachePayload
        {
            Version = CurrentVersion,
            AccessCount = profile.AccessCount,
            IsColdStart = profile.IsColdStart,
            Window = profile.TrainingWindow.Select(v => v.ToArray()).ToList(),
            Recent = profile.RecentAccesses
                .Select(a => new AccessDto { Timestamp = a.Timestamp, Endpoint = a.Endpoint })
                .ToList(),
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static UserAnomalyProfile? DeserializeFromCache(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<CachePayload>(json, SerializerOptions);
            if (payload is null)
                return null;

            var window = payload.Window
                .Where(a => a is { Length: AnomalyFeatureVector.Dimension })
                .Select(a => new AnomalyFeatureVector(a[0], a[1], a[2], a[3]))
                .ToArray();

            var recent = payload.Recent
                .Select(a => new UserAccessSample(a.Timestamp, a.Endpoint ?? string.Empty))
                .ToArray();

            return new UserAnomalyProfile(window, recent, payload.AccessCount, payload.IsColdStart);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Forma del JSONB de columna (nombres cortos: es la fila más voluminosa junto con audit_logs).</summary>
    private sealed class ProfilePayload
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("w")] public List<float[]> Window { get; set; } = new();
        [JsonPropertyName("r")] public List<AccessDto> Recent { get; set; } = new();
    }

    /// <summary>Forma del perfil íntegro cacheado en Redis (JSONB de columna + escalares de la entidad).</summary>
    private sealed class CachePayload
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("ac")] public int AccessCount { get; set; }
        [JsonPropertyName("cs")] public bool IsColdStart { get; set; }
        [JsonPropertyName("w")] public List<float[]> Window { get; set; } = new();
        [JsonPropertyName("r")] public List<AccessDto> Recent { get; set; } = new();
    }

    private sealed class AccessDto
    {
        [JsonPropertyName("t")] public DateTimeOffset Timestamp { get; set; }
        [JsonPropertyName("e")] public string? Endpoint { get; set; }
    }
}
