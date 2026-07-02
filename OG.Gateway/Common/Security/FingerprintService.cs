using System;
using System.Security.Cryptography;
using System.Text;

namespace Application.Common.Security;

/// <summary>
/// Implementación por defecto del servicio de hashing de huella digital de navegador (HU-013 / T-024).
/// </summary>
public sealed class FingerprintService : IFingerprintService
{
    public string GenerateHash(string? userAgent, string? acceptLanguage, string? acceptEncoding)
    {
        var ua = userAgent?.Trim() ?? string.Empty;
        var lang = acceptLanguage?.Trim() ?? string.Empty;
        var encoding = acceptEncoding?.Trim() ?? string.Empty;

        // Concatenación delimitada estructurada para evitar colisiones sencillas
        var rawString = $"UA:{ua}|LANG:{lang}|ENC:{encoding}";

        var bytes = Encoding.UTF8.GetBytes(rawString);
        var hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
