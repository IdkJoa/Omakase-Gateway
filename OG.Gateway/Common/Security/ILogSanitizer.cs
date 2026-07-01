namespace Application.Common.Security;

/// <summary>
/// Sanitiza strings antes de escribirlos en logs o registros de auditoría.
/// </summary>
/// <remarks>
/// Protege contra dos vectores:
/// <list type="bullet">
///   <item><description>Log injection: caracteres de control que falsifican entradas de log (\n, \r, \t…).</description></item>
///   <item><description>PII cruda: trunca a <paramref name="maxLength"/> para limitar la exposición.</description></item>
/// </list>
/// </remarks>
public interface ILogSanitizer
{
    /// <summary>
    /// Sanitiza <paramref name="input"/> eliminando caracteres de control y truncando al límite indicado.
    /// </summary>
    /// <param name="input">String a sanitizar. Null o vacío devuelve <see cref="string.Empty"/>.</param>
    /// <param name="maxLength">Longitud máxima del resultado. Por defecto 512 caracteres.</param>
    /// <returns>String seguro para escritura en logs y audit records.</returns>
    string Sanitize(string? input, int maxLength = 512);
}
