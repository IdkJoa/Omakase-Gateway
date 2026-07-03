using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Implementación real de <see cref="IAnomalyDetector"/> con RandomizedPCA de ML.NET (HU-016 / T-032).
/// Reemplaza al <c>StubAnomalyDetector</c> por swap de DI — el handler del motor de riesgo no cambia (Open/Closed).
/// <para>
/// El modelo entrenado por usuario vive en <see cref="IAnomalyModelCache"/> (Singleton) y se reconstruye
/// desde la ventana de features del perfil; el reentrenamiento periódico (T-035) invalida la caché. La
/// construcción del modelo está fuera del presupuesto de ≤15 ms (solo la inferencia lo está); el primer
/// request de un usuario tras arrancar el proceso paga el entrenamiento una vez (o lo pre-calienta T-035).
/// </para>
/// </summary>
public sealed class RandomizedPcaAnomalyDetector : IAnomalyDetector
{
    /// <summary>Score neutro (máxima incertidumbre) = fallback de RF-M9 y postura ante sin-identidad/cold-start.</summary>
    private const decimal NeutralScore = 50m;

    private readonly IUserProfileStore _store;
    private readonly IFeatureExtractor _extractor;
    private readonly AnomalyModelTrainer _trainer;
    private readonly IAnomalyModelCache _modelCache;
    private readonly AnomalyDetectionOptions _options;

    public RandomizedPcaAnomalyDetector(
        IUserProfileStore store,
        IFeatureExtractor extractor,
        AnomalyModelTrainer trainer,
        IAnomalyModelCache modelCache,
        AnomalyDetectionOptions options)
    {
        _store = store;
        _extractor = extractor;
        _trainer = trainer;
        _modelCache = modelCache;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<decimal> GetAnomalyScoreAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        // Sin identidad resuelta no hay a quién perfilar → incertidumbre (RF-M9).
        if (string.IsNullOrWhiteSpace(context.UserId))
            return NeutralScore;

        var profile = await _store.GetAsync(context.UserId, cancellationToken);

        // Historial insuficiente (cold-start, HU-017): no se entrena un modelo poco fiable. La elevación
        // de riesgo la aporta el ColdStartPenalty en el consolidador, no este score.
        if (profile is null || profile.TrainingWindow.Count < _options.MinTrainingSamples)
            return NeutralScore;

        var model = _modelCache.GetOrBuild(context.UserId, () => _trainer.Train(profile.TrainingWindow));
        var features = _extractor.Extract(context, profile.RecentAccesses);

        return (decimal)model.Score(features);
    }
}
