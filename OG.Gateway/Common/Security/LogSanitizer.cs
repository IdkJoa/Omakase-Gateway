using System.Buffers;

namespace Application.Common.Security;

/// <summary>
/// Implementación optimizada de <see cref="ILogSanitizer"/> con Fast-Path Zero-Allocation (HU-028 T-059).
/// Neutraliza caracteres de inyección en logs sin generar objetos innecesarios en la Heap.
/// Registrar como Singleton (thread-safe, sin estado mutable).
/// </summary>
public sealed class LogSanitizer : ILogSanitizer
{
    private static bool IsControlChar(char c) =>
        c <= 0x1F || (c >= 0x7F && c <= 0x9F) || c == '\u2028' || c == '\u2029';

    /// <inheritdoc/>
    public string Sanitize(string? input, int maxLength = 512)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        ReadOnlySpan<char> span = input.AsSpan().Trim();
        if (span.IsEmpty)
            return string.Empty;

        if (span.Length > maxLength)
            span = span[..maxLength];

        // 1. Fast-Path: Escanear si la cadena requiere modificación
        bool needsSanitization = false;
        foreach (char c in span)
        {
            if (IsControlChar(c))
            {
                needsSanitization = true;
                break;
            }
        }

        // Si la cadena no contiene caracteres de control y no requiere recortado respecto a la entrada:
        if (!needsSanitization && span.Length == input.Length)
            return input;

        if (!needsSanitization)
            return span.ToString();

        // 2. Slow-Path: Reemplazar caracteres de control usando stackalloc char[]
        char[]? rented = null;
        Span<char> buffer = span.Length <= 512
            ? stackalloc char[span.Length]
            : (rented = ArrayPool<char>.Shared.Rent(span.Length));

        try
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = span[i];
                buffer[i] = IsControlChar(c) ? ' ' : c;
            }

            return buffer[..span.Length].Trim().ToString();
        }
        finally
        {
            if (rented != null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }
}
