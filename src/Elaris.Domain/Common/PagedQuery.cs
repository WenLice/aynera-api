namespace Elaris.Domain.Common;

public sealed record PagedQuery
{
    public const int DefaultPageSize = 15;
    public const int MaxPageSize = 50;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    public int Skip => (Page - 1) * PageSize;
}
