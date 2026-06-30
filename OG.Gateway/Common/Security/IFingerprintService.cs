namespace Application.Common.Security;

/// <summary>
/// Servicio para generar hashes de huella digital (fingerprint) del navegador
/// a partir de cabeceras HTTP específicas del cliente (HU-013 / T-024).
/// </summary>
public interface IFingerprintService
{
    /// <summary>
    /// Genera un hash SHA-256 en formato hexadecimal de 64 caracteres
    /// combinando User-Agent, Accept-Language y Accept-Encoding de forma determinista.
    /// </summary>
    string GenerateHash(string? userAgent, string? acceptLanguage, string? acceptEncoding);
}
