using System.Text.Encodings.Web;

namespace Application.Common.Security;

/// <summary>
/// Implementación de <see cref="IOutputSanitizer"/> basada en
/// <see cref="HtmlEncoder.Default"/> de .NET (HU-028 T-060).
/// <para>
/// Registrar como Singleton (sin estado mutable, thread-safe).
/// </para>
/// </summary>
/// <remarks>
/// Caracteres codificados:
/// <list type="bullet">
///   <item><c>&lt;</c> → <c>&amp;lt;</c></item>
///   <item><c>&gt;</c> → <c>&amp;gt;</c></item>
///   <item><c>&amp;</c> → <c>&amp;amp;</c></item>
///   <item><c>"</c> → <c>&amp;quot;</c></item>
///   <item><c>'</c> → <c>&amp;#x27;</c></item>
/// </list>
/// Strings seguros (alfanuméricos, espacios, puntos, etc.) pasan sin modificación.
/// </remarks>
public sealed class HtmlOutputSanitizer : IOutputSanitizer
{
    /// <inheritdoc/>
    public string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return HtmlEncoder.Default.Encode(input);
    }
}
