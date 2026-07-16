using System.Text.RegularExpressions;

namespace Application.Common.Security;

/// <summary>
/// Implementacion de <see cref="ILogSanitizer"/> basada en expresiones regulares compiladas.
/// Registrar como Singleton (sin estado mutable).
/// </summary>
public sealed class LogSanitizer : ILogSanitizer
{
    // Neutraliza caracteres que pueden falsificar entradas de log (log injection):
    //   0x00-0x1F      controles ASCII (NUL, BS, HT, LF, VT, FF, CR),
    //   0x7F-0x9F      DEL y controles C1 (incluye NEL, U+0085),
    //   U+2028/U+2029  separadores de linea y de parrafo Unicode.
    private static readonly Regex ControlCharacters =
        new(@"[\x00-\x1F\x7F-\x9F\u2028\u2029]", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <inheritdoc/>
    public string Sanitize(string? input, int maxLength = 512)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // 1. Reemplazar caracteres de control con espacio (previene log injection)
        var sanitized = ControlCharacters.Replace(input, " ");

        // 2. Truncar a longitud maxima
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength];

        return sanitized.Trim();
    }
}
