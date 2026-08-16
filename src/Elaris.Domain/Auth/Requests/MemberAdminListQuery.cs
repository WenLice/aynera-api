using Elaris.Domain.Common;

namespace Elaris.Domain.Auth.Requests;

/// <summary>Admin member list: paging plus optional search and status filters.</summary>
public sealed class MemberAdminListQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PagedQuery.DefaultPageSize;
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsRestricted { get; init; }

    public int Skip => (Page - 1) * PageSize;
}
