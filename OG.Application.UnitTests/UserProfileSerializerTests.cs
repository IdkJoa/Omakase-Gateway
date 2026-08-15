using System.Text.Json;
using Application.Common.RiskEngine.AnomalyDetection;
using Infrastructure.AnomalyDetection;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas de la serialización del perfil hacia/desde JSONB (HU-016 / T-033): round-trip fiel
/// y tolerancia a documentos nulos/malformados (fail-safe → cold-start, no excepción).
/// </summary>
public class UserProfileSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesWindowAndAccesses()
    {
        var window = new[]
        {
            new AnomalyFeatureVector(0.37f, 0.02f, 0.15f, 0.20f),
            new AnomalyFeatureVector(0.85f, 0.85f, 0.95f, 0.95f),
        };
        var recent = new[]
        {
            new UserAccessSample(new DateTimeOffset(2026, 7, 3, 13, 0, 0, TimeSpan.Zero), "/httpbin/get"),
            new UserAccessSample(new DateTimeOffset(2026, 7, 3, 13, 5, 0, TimeSpan.Zero), "/httpbin/post"),
        };

        using var json = UserProfileSerializer.Serialize(window, recent);
        var (w, r) = UserProfileSerializer.Deserialize(json);

        Assert.Equal(window, w);
        Assert.Equal(recent, r);
    }

    [Fact]
    public void CacheRoundTrip_PreservesProfileWithScalars()
    {
        var profile = new UserAnomalyProfile(
            TrainingWindow: new[] { new AnomalyFeatureVector(0.37f, 0.02f, 0.15f, 0.20f) },
            RecentAccesses: new[] { new UserAccessSample(new DateTimeOffset(2026, 7, 3, 13, 0, 0, TimeSpan.Zero), "/httpbin/get") },
            AccessCount: 42,
            IsColdStart: false);

        var json = UserProfileSerializer.SerializeForCache(profile);
        var restored = UserProfileSerializer.DeserializeFromCache(json);

        Assert.NotNull(restored);
        Assert.Equal(42, restored!.AccessCount);
        Assert.False(restored.IsColdStart);
        Assert.Equal(profile.TrainingWindow, restored.TrainingWindow);
        Assert.Equal(profile.RecentAccesses, restored.RecentAccesses);
    }

    [Fact]
    public void DeserializeFromCache_NullOrGarbage_ReturnsNull_NotThrow()
    {
        Assert.Null(UserProfileSerializer.DeserializeFromCache(null));
        Assert.Null(UserProfileSerializer.DeserializeFromCache("  "));
        Assert.Null(UserProfileSerializer.DeserializeFromCache("{not valid json"));
    }

    [Fact]
    public void RoundTrip_Empty_YieldsEmpty()
    {
        using var json = UserProfileSerializer.Serialize([], []);
        var (w, r) = UserProfileSerializer.Deserialize(json);

        Assert.Empty(w);
        Assert.Empty(r);
    }

    [Fact]
    public void Deserialize_Null_ReturnsEmpty_NotThrow()
    {
        var (w, r) = UserProfileSerializer.Deserialize(null);

        Assert.Empty(w);
        Assert.Empty(r);
    }

    [Fact]
    public void Deserialize_Malformed_ReturnsEmpty_NotThrow()
    {
        using var garbage = JsonDocument.Parse("""{"w":"not-an-array","totally":"wrong"}""");

        var (w, r) = UserProfileSerializer.Deserialize(garbage);

        Assert.Empty(w);
        Assert.Empty(r);
    }

    [Fact]
    public void Deserialize_SkipsVectorsWithWrongDimension()
    {
        // Un vector con 3 componentes (corrupto) se descarta; el válido de 4 sobrevive.
        using var doc = JsonDocument.Parse("""{"v":1,"w":[[0.1,0.2,0.3],[0.1,0.2,0.3,0.4]],"r":[]}""");

        var (w, _) = UserProfileSerializer.Deserialize(doc);

        Assert.Single(w);
        Assert.Equal(new AnomalyFeatureVector(0.1f, 0.2f, 0.3f, 0.4f), w[0]);
    }
}
