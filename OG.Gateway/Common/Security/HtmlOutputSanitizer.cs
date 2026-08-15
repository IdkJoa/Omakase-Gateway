using System.Buffers;
using System.Text.Encodings.Web;

namespace Application.Common.Security;

// Registrar como Singleton (thread-safe, sin estado mutable).
public sealed class HtmlOutputSanitizer : IOutputSanitizer
{
    private static readonly SearchValues<char> HtmlSpecialChars = SearchValues.Create("<>&\"'");

    public string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        ReadOnlySpan<char> span = input.AsSpan();

        // SIMD fast-path: sin caracteres especiales HTML, devuelve la cadena intacta sin asignar heap.
        if (!span.ContainsAny(HtmlSpecialChars))
            return input;

        return HtmlEncoder.Default.Encode(input);
    }
}
