namespace Application.Common.Security.Mfa;

public sealed record StepUpData(string? FingerprintHash, DateTimeOffset GrantedAt);

// Dentro de la ventana, el consolidador degrada Challenge->Allow para el mismo usuario y dispositivo.
public interface IStepUpStore
{
    Task SetAsync(string userId, StepUpData data, TimeSpan ttl);

    Task<StepUpData?> GetAsync(string userId);
}
