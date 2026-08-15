using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Application.Common.Security;

// Usa Spans/stackalloc para minimizar asignaciones en heap: está en el pipeline crítico del Gateway.
public sealed class FingerprintService : IFingerprintService
{
    public string GenerateHash(string? userAgent, string? acceptLanguage, string? acceptEncoding)
    {
        ReadOnlySpan<char> ua = userAgent.AsSpan().Trim();
        ReadOnlySpan<char> lang = acceptLanguage.AsSpan().Trim();
        ReadOnlySpan<char> enc = acceptEncoding.AsSpan().Trim();

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

            Span<byte> hashBuffer = stackalloc byte[32];
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
