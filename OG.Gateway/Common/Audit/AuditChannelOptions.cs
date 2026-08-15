namespace Application.Common.Audit;

public sealed class AuditChannelOptions
{
    public const string SectionName = "AuditChannel";

    // Al alcanzar la capacidad, TryWrite descarta el evento en vez de bloquear al escritor.
    public int Capacity { get; set; } = 10_000;
}
