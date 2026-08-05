using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Microsoft.Extensions.Logging;

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
/// <para>
/// El detector degrada a <see cref="NeutralScore"/> en TRES situaciones distintas, todas bajo la misma
/// política de RF-M9 —ante la duda, incertidumbre, nunca una señal inventada—: sin identidad, sin
/// historial suficiente para entrenar (cold-start, HU-017), sin muestras suficientes en la ventana para
/// medir volumen (HU-035), y ante un fallo de la dependencia de ML.NET (HU-032).
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
    private readonly Microsoft.Extensions.Logging.ILogger<RandomizedPcaAnomalyDetector>? _logger;

    public RandomizedPcaAnomalyDetector(
        IUserProfileStore store,
        IFeatureExtractor extractor,
        AnomalyModelTrainer trainer,
        IAnomalyModelCache modelCache,
        AnomalyDetectionOptions options,
        Microsoft.Extensions.Logging.ILogger<RandomizedPcaAnomalyDetector>? logger = null)
    {
        _store = store;
        _extractor = extractor;
        _trainer = trainer;
        _modelCache = modelCache;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<decimal> GetAnomalyScoreAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Sin identidad resuelta no hay a quién perfilar → incertidumbre (RF-M9).
            if (string.IsNullOrWhiteSpace(context.UserId))
                return NeutralScore;

            var profile = await _store.GetAsync(context.UserId, cancellationToken);

            // Historial insuficiente (cold-start, HU-017): no se entrena un modelo poco fiable. La elevación
            // de riesgo la aporta el ColdStartPenalty en el consolidador, no este score.
            if (profile is null || profile.TrainingWindow.Count < _options.MinTrainingSamples)
                return NeutralScore;

            // Dos de las cuatro features (frecuencia y diversidad) se miden SOBRE la ventana reciente. Con
            // pocos accesos dentro de ella no valen "poco": son estadísticamente inservibles. La diversidad
            // es un ratio únicos/total, así que con 1 acceso solo puede dar 1.0 y con 2 solo 0.5 o 1.0 —
            // valores que el baseline no contiene (allí ronda 0.2–0.3) y que disparan el percentil.
            //
            // Medido en vivo (2026-08-01), ambos casos con usuario y perfil legítimos:
            //   · sin accesos en la ventana  → el mismo acceso puntuaba 12.0 en una siembra y 89.5 en otra
            //   · con UN acceso previo       → 92.5, es decir la SEGUNDA petición de la hora habría sido
            //                                  desafiada, y quedaba pegada al ataque en ráfaga (98.0)
            // Con esa varianza no hay reparto de pesos que separe legítimo de ataque: no era calibración.
            //
            // Se aplica la MISMA política que ya rige para el cold-start (RF-M9): si los datos no alcanzan,
            // se devuelve incertidumbre en vez de inventar una señal. El riesgo por volumen solo se evalúa
            // cuando hay volumen medible; las reglas deterministas siguen actuando igual, y un ataque por
            // volumen supera el mínimo de sobra, así que no se pierde detección.
            if (CountInFrequencyWindow(context.Timestamp, profile.RecentAccesses) < _options.MinWindowSamples)
                return NeutralScore;

            var model = _modelCache.GetOrBuild(context.UserId, () => _trainer.Train(profile.TrainingWindow));
            var features = _extractor.Extract(context, profile.RecentAccesses);

            return (decimal)model.Score(features);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fallo en la inferencia de ML.NET (RandomizedPcaAnomalyDetector). Degradando a AnomalyScore neutro (50).");

            var activity = System.Diagnostics.Activity.Current;
            if (activity != null)
            {
                activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Dependency failure: ML.NET");
                activity.SetTag("error", true);
                activity.SetTag("dependency.name", "ML.NET");
            }

            return NeutralScore;
        }
    }

    /// <summary>
    /// Cuenta los accesos del usuario dentro de la ventana con la que se miden frecuencia y diversidad.
    /// Replica exactamente el criterio de <see cref="IFeatureExtractor"/> —misma ventana, mismos
    /// límites— para que "hay datos suficientes" signifique lo mismo en ambos sitios.
    /// </summary>
    private int CountInFrequencyWindow(
        DateTimeOffset now, IReadOnlyList<UserAccessSample> recentAccesses)
    {
        if (recentAccesses.Count == 0)
            return 0;

        var windowStart = now - TimeSpan.FromMinutes(_options.FrequencyWindowMinutes);
        var count = 0;

        foreach (var access in recentAccesses)
        {
            if (access.Timestamp > windowStart && access.Timestamp <= now)
                count++;
        }

        return count;
    }
}
