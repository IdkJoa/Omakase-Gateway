namespace Application.Common.Security;

// Defensa en profundidad contra XSS: neutraliza payloads que el Dashboard Angular pudiera
// interpretar como HTML/JavaScript si usara [innerHTML] o un contexto no escapado.
public interface IOutputSanitizer
{
    string Sanitize(string? input);
}
