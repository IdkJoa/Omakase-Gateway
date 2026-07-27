namespace Application.Common.Security;

/// <summary>
/// Sanitiza strings de origen externo antes de devolverlos en respuestas JSON
/// que el Dashboard Angular renderiza (defensa en profundidad contra XSS).
/// </summary>
/// <remarks>
/// La implementación aplica HTML encoding a caracteres peligrosos
/// (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>"</c>, <c>'</c>) para neutralizar
/// cualquier payload que pudiese interpretarse como HTML/JavaScript si el frontend
/// usara <c>[innerHTML]</c> o un contexto no escapado.
/// <para>
/// HU-028 T-060 — SRS §6.3.
/// </para>
/// </remarks>
public interface IOutputSanitizer
{
    /// <summary>
    /// Codifica <paramref name="input"/> con HTML encoding para prevenir XSS.
    /// </summary>
    /// <param name="input">String a codificar. Null o vacío devuelve <see cref="string.Empty"/>.</param>
    /// <returns>String seguro para inclusión en respuestas JSON renderizadas en HTML.</returns>
    string Sanitize(string? input);
}
