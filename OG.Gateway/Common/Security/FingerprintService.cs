using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Application.Common.Security;

/// <summary>
/// Implementación optimizada de alto rendimiento del servicio de hashing de huella digital de navegador (HU-013 / T-024).
/// Minimiza las asignaciones en la Heap utilizando Spans y stackalloc para el pipeline crítico del Gateway.
/// </summary>
public sealed class FingerprintService : IFingerprintService
{
    public string GenerateHash(string? userAgent, string? acceptLanguage, string? acceptEncoding)
    {
        ReadOnlySpan<char> ua = userAgent.AsSpan().Trim();
        ReadOnlySpan<char> lang = acceptLanguage.AsSpan().Trim();
        ReadOnlySpan<char> enc = acceptEncoding.AsSpan().Trim();

        // Estimar tamaño máximo de bytes UTF-8 para "UA:{ua}|LANG:{lang}|ENC:{enc}"
        int maxByteCount = 14 + Encoding.UTF8.GetByteCount(ua) + Encoding.UTF8.GetByteCount(lang) + Encoding.UTF8.GetByteCount(enc);

        byte[]? rented = null;
        Span<byte> buffer = maxByteCount <= 1024
            ? stackalloc byte[maxByteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(maxByteCount));

        try
        {
            int bytesWritten = 0;
            bytesWritten += Encoding.UTF8.GetBytes("UA:", buffer[bytesWritten..]);
            bytesWritten += Encoding.UTF8.GetBytes(ua, buffer[bytesWritten..]);
            bytesWritten += Encoding.UTF8.GetBytes("|LANG:", buffer[bytesWritten..]);
            bytesWritten += Encoding.UTF8.GetBytes(lang, buffer[bytesWritten..]);
            bytesWritten += Encoding.UTF8.GetBytes("|ENC:", buffer[bytesWritten..]);
            bytesWritten += Encoding.UTF8.GetBytes(enc, buffer[bytesWritten..]);

            Span<byte> hashBuffer = stackalloc byte[32]; // SHA256 son 32 bytes
            SHA256.HashData(buffer[..bytesWritten], hashBuffer);

            return Convert.ToHexStringLower(hashBuffer);
        }
        finally
        {
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
