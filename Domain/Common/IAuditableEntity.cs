namespace Domain.Common;

/// <summary>
/// Entidad cuya última modificación se registra en la columna <c>updated_at</c>
/// (SRS §7.1, §7.2, §7.5 y §7.9).
/// <para>
/// El sello lo pone <c>OmakaseDbContext.SaveChangesAsync</c> de forma centralizada:
/// ningún manejador necesita acordarse de asignarlo, y añadir una entidad auditable
/// nueva se reduce a implementar esta interfaz.
/// </para>
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset? UpdatedAt { get; set; }
}
