using System.Text.RegularExpressions;

namespace Application.Common.Security;

/// <summary>
/// Implementación de <see cref="ILogSanitizer"/> basada en expresiones regulares compiladas.
/// Registrar como Singleton (sin estado mutable).
/// </summary>
public sealed class LogSanitizer : ILogSanitizer
{
    // Cubre ASCII 0x00-0x1F (control characters: NUL, BS, HT, LF, VT, FF, CR…) y 0x7F (DEL).
    private static readonly Regex ControlCharacters =
        new(@"[\x00-\x1F\x7F]", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <inheritdoc/>
    public string Sanitize(string? input, int maxLength = 512)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // 1. Reemplazar caracteres de control con espacio (previene log injection)
        var sanitized = ControlCharacters.Replace(input, " ");

        // 2. Truncar a longitud máxima
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength];

        return sanitized.Trim();
    }
}
