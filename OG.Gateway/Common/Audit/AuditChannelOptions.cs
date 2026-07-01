namespace Application.Common.Audit;

/// <summary>
/// Opciones de configuración para <see cref="InMemoryAuditChannel"/>.
/// Sección en <c>appsettings.json</c>: <c>"AuditChannel"</c>.
/// </summary>
public sealed class AuditChannelOptions
{
    /// <summary>Nombre de la sección en <c>appsettings.json</c>.</summary>
    public const string SectionName = "AuditChannel";

    /// <summary>
    /// Capacidad máxima del canal acotado (número de <see cref="AuditEvent"/> en cola).
    /// Si se alcanza, <see cref="IAuditChannel.TryWrite"/> descarta el evento sin bloquear.
    /// </summary>
    /// <value>Por defecto: 10 000 eventos.</value>
    public int Capacity { get; set; } = 10_000;
}
