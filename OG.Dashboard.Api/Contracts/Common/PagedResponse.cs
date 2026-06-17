namespace OG.Dashboard.Api.Contracts.Common;

/// <summary>
/// Envelope genérico para respuestas paginadas en todos los endpoints de listado.
/// Contrato: { page, pageSize, totalRecords, data[] }
/// </summary>
/// <typeparam name="T">Tipo del ítem en la colección.</typeparam>
public sealed record PagedResponse<T>(
    int Page,
    int PageSize,
    int TotalRecords,
    IReadOnlyList<T> Data
)
{
    /// <summary>Número total de páginas calculado a partir de TotalRecords y PageSize.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalRecords / PageSize) : 0;
}
