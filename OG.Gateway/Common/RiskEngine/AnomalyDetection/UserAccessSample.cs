namespace Application.Common.RiskEngine.AnomalyDetection;

// Deliberadamente ligera: el extractor es puro y no accede a datos; la ventana de accesos
// recientes la provee el almacén de perfil (Redis/DB).
public readonly record struct UserAccessSample(DateTimeOffset Timestamp, string Endpoint);
