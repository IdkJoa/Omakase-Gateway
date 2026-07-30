using System.Buffers;
using System.Text.Encodings.Web;

namespace Application.Common.Security;

/// <summary>
/// Implementación de <see cref="IOutputSanitizer"/> optimizada con Fast-Path Zero-Allocation (HU-028 T-060).
/// Basada en <see cref="HtmlEncoder.Default"/> de .NET y SIMD <see cref="SearchValues{T}"/>.
/// Registrar como Singleton (thread-safe, sin estado mutable).
/// </summary>
public sealed class HtmlOutputSanitizer : IOutputSanitizer
{
    private static readonly SearchValues<char> HtmlSpecialChars = SearchValues.Create("<>&\"'");

    /// <inheritdoc/>
    public string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        ReadOnlySpan<char> span = input.AsSpan();

        // Fast-path con SearchValues (SIMD): si la cadena no contiene caracteres especiales HTML (<, >, &, ", '), la devuelve intacta sin asignación heap
        if (!span.ContainsAny(HtmlSpecialChars))
            return input;

        return HtmlEncoder.Default.Encode(input);
    }
}
