namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Muestra mínima de un acceso histórico del usuario (instante + endpoint), usada como
/// entrada al <see cref="IFeatureExtractor"/> para derivar frecuencia y diversidad (T-031).
/// <para>Se mantiene deliberadamente ligera: el extractor es puro y no accede a datos;
/// la ventana de accesos recientes la provee el almacén de perfil (Redis/DB).</para>
/// </summary>
public readonly record struct UserAccessSample(DateTimeOffset Timestamp, string Endpoint);
