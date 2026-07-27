namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Resumen representativo del perfil de comportamiento aprendido de un usuario (HU-026 / T-054).
/// Promedia la ventana de entrenamiento (<see cref="AnomalyFeatureVector"/>) en un centroide y
/// decodifica la hora habitual del día a partir de la codificación cíclica seno/coseno.
/// </summary>
/// <param name="HourSin">Media (en [0,1]) de la componente seno de la hora.</param>
/// <param name="HourCos">Media (en [0,1]) de la componente coseno de la hora.</param>
/// <param name="Frequency">Media (en [0,1]) de la frecuencia de peticiones.</param>
/// <param name="Diversity">Media (en [0,1]) de la diversidad de endpoints.</param>
/// <param name="TypicalHour">Hora habitual decodificada en [0,24) (media circular de la hora).</param>
public readonly record struct BehaviorProfileSummary(
    float HourSin,
    float HourCos,
    float Frequency,
    float Diversity,
    double TypicalHour);

/// <summary>
/// Proyecta la ventana de entrenamiento de un perfil a un resumen visualizable. Función pura
/// (sin estado ni dependencias) — la usa el Dashboard (T-054) para exponer el feature_vector
/// decodificado y es directamente testeable.
/// </summary>
public static class BehaviorProfileProjection
{
    /// <summary>
    /// Devuelve el resumen representativo (centroide promedio + hora habitual decodificada), o
    /// <c>null</c> si la ventana está vacía (aún no hay comportamiento aprendido → cold-start),
    /// para que el consumidor muestre el estado de arranque en frío en vez de un vector en cero.
    /// <para>
    /// La hora se codifica como <c>(sin(2π·h/24)+1)/2</c> y <c>(cos(...)+1)/2</c> (ver
    /// <c>FeatureExtractor</c>). Para decodificarla se revierte la normalización (<c>x·2−1</c>) y
    /// se toma la media circular con <see cref="System.Math.Atan2(double,double)"/>: promediar en
    /// el espacio seno/coseno y luego decodificar respeta la continuidad cíclica (23:00 y 01:00
    /// promedian a 00:00, no a 12:00).
    /// </para>
    /// </summary>
    public static BehaviorProfileSummary? Summarize(IReadOnlyList<AnomalyFeatureVector> window)
    {
        if (window is null || window.Count == 0)
            return null;

        double sumSin = 0, sumCos = 0, sumFreq = 0, sumDiv = 0;
        foreach (var v in window)
        {
            sumSin += v.HourSin;
            sumCos += v.HourCos;
            sumFreq += v.Frequency;
            sumDiv += v.Diversity;
        }

        var n = window.Count;
        var avgSin = sumSin / n; // en [0,1]
        var avgCos = sumCos / n; // en [0,1]

        var angle = Math.Atan2(2.0 * avgSin - 1.0, 2.0 * avgCos - 1.0);
        if (angle < 0) angle += 2.0 * Math.PI;
        var typicalHour = angle / (2.0 * Math.PI) * 24.0;

        return new BehaviorProfileSummary(
            (float)avgSin, (float)avgCos, (float)(sumFreq / n), (float)(sumDiv / n), typicalHour);
    }
}
