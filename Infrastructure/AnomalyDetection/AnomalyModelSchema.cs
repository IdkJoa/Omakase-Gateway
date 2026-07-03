using Application.Common.RiskEngine.AnomalyDetection;
using Microsoft.ML.Data;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Fila de entrada al pipeline de ML.NET (HU-016 / T-032). Envuelve el
/// <see cref="AnomalyFeatureVector"/> en el formato de columna vectorial que espera el trainer.
/// Interno a Infrastructure: los tipos de ML.NET no se filtran a la capa de aplicación.
/// </summary>
internal sealed class AnomalyFeatureRow
{
    [VectorType(AnomalyFeatureVector.Dimension)]
    public float[] Features { get; set; } = new float[AnomalyFeatureVector.Dimension];

    public static AnomalyFeatureRow From(AnomalyFeatureVector vector) =>
        new() { Features = vector.ToArray() };
}

/// <summary>
/// Salida cruda del RandomizedPCA: <see cref="Score"/> es el error de reconstrucción normalizado
/// (mayor = más anómalo). Se calibra antes de exponerse como Anomaly Score 0–100.
/// </summary>
internal sealed class AnomalyRawPrediction
{
    [ColumnName("PredictedLabel")]
    public bool IsAnomaly { get; set; }

    public float Score { get; set; }
}
