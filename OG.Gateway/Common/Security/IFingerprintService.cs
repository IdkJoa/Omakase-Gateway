namespace Application.Common.Security;

public interface IFingerprintService
{
    string GenerateHash(string? userAgent, string? acceptLanguage, string? acceptEncoding);
}
