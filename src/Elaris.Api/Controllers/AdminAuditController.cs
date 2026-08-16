using Elaris.Api.Controllers.Base;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Domain.Audit.Requests;
using Elaris.Domain.Audit.Responses;
using Elaris.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elaris.Api.Controllers;

/// <summary>Admin audit event inbox (member restrict/unrestrict and related safety actions).</summary>
[Route("admin/audit")]
[Tags("Admin")]
[Authorize(Policy = "Admin")]
public sealed class AdminAuditController : BaseController
{
    private readonly IAuditAdminService _audit;

    public AdminAuditController(IAuditAdminService audit)
    {
        _audit = audit;
    }

    /// <summary>ListAuditEvents</summary>
    /// <remarks>
    /// When <c>memberId</c> or <c>subjectId</c> is set, returns all actions for that subject unless <c>action</c> is provided.
    /// </remarks>
    [HttpGet("events")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditEventAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditEventAdminDto>>>> ListEvents(
        [FromQuery] AuditAdminListQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _audit.ListAsync(CurrentUser.GetRequiredUserId(), query, cancellationToken);
        return OkResponse(page);
    }
}
