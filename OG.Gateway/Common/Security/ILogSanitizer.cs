namespace Application.Common.Security;

// Protege contra dos vectores: log injection (caracteres de control que falsifican entradas de
// log) y exposición de PII cruda (trunca a maxLength).
public interface ILogSanitizer
{
    string Sanitize(string? input, int maxLength = 512);
}
