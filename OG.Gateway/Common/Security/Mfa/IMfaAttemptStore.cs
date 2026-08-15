namespace Application.Common.Security.Mfa;

// Contador anti fuerza bruta de verificaciones TOTP. Al superar el máximo por ventana, la
// cuenta se bloquea (HTTP 423).
public interface IMfaAttemptStore
{
    Task<long> IncrementAsync(string userId, TimeSpan window);

    Task ResetAsync(string userId);
}
