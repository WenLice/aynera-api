using Elaris.Domain.Common;

namespace Elaris.Domain.Audit.Requests;

/// <summary>
/// Admin audit list for a detail pane.
/// When <see cref="MemberId"/> or <see cref="SubjectId"/> is set, returns all actions for that subject
/// (unless <see cref="Action"/> is provided). Without a subject, defaults to member restrict/unrestrict.
/// </summary>
public sealed class AuditAdminListQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PagedQuery.DefaultPageSize;
    public string? Action { get; init; }
    public Guid? MemberId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }

    public int Skip => (Page - 1) * PageSize;
}
