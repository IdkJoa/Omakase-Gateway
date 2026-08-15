namespace OG.Dashboard.Api.Contracts.Common;

public sealed record PagedResponse<T>(
    int Page,
    int PageSize,
    int TotalRecords,
    IReadOnlyList<T> Data
)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalRecords / PageSize) : 0;
}
