using Elaris.Application.Features.Audit.Repositories;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Domain.Audit.Requests;
using Elaris.Domain.Audit.Responses;
using Elaris.Domain.Audit.Statics;
using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Elaris.Application.Features.Audit.Services.Implementations;

public sealed class AuditAdminService : IAuditAdminService
{
    private static readonly IReadOnlyList<string> DefaultMemberSafetyActions =
    [
        AuditActions.MemberRestricted,
        AuditActions.MemberUnrestricted
    ];

    private readonly IAuditEventRepository _events;
    private readonly IUserRepository _users;
    private readonly ILogger<AuditAdminService> _logger;

    public AuditAdminService(
        IAuditEventRepository events,
        IUserRepository users,
        ILogger<AuditAdminService> logger)
    {
        _events = events;
        _users = users;
        _logger = logger;
    }

    public async Task<PagedResult<AuditEventAdminDto>> ListAsync(
        Guid actorId,
        AuditAdminListQuery query,
        CancellationToken cancellationToken)
    {
        await RequireAdminActorAsync(actorId, cancellationToken);

        var hasSubject = query.MemberId is not null || !string.IsNullOrWhiteSpace(query.SubjectId);
        IReadOnlyList<string>? actions = null;
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            actions = [query.Action.Trim()];
        }
        else if (!hasSubject)
        {
            actions = DefaultMemberSafetyActions;
        }

        _logger.LogInformation(
            "ListAuditEvents page {Page} size {PageSize} action {Action} member {MemberId} subject {SubjectType}/{SubjectId}",
            query.Page,
            query.PageSize,
            query.Action,
            query.MemberId,
            query.SubjectType,
            query.SubjectId);

        var (rows, totalCount) = await _events.ListPageAsync(
            query.Skip,
            query.PageSize,
            actions,
            query.MemberId,
            string.IsNullOrWhiteSpace(query.SubjectType) ? null : query.SubjectType.Trim(),
            string.IsNullOrWhiteSpace(query.SubjectId) ? null : query.SubjectId.Trim(),
            cancellationToken);

        var items = rows.Select(row => new AuditEventAdminDto(
            row.Id,
            row.OccurredAtUtc,
            row.Action,
            row.Outcome,
            row.Message,
            row.ActorUserId,
            row.SubjectUserId,
            row.ChangesJson,
            row.CorrelationId)).ToList();

        return new PagedResult<AuditEventAdminDto>(items, query.Page, query.PageSize, totalCount);
    }

    private async Task RequireAdminActorAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await _users.FindByIdAsync(actorId, cancellationToken);
        if (actor is null
            || actor.IsDeleted
            || !actor.IsActive
            || !string.Equals(actor.AccountKind, nameof(AccountKind.Admin), StringComparison.Ordinal))
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }
    }
}
