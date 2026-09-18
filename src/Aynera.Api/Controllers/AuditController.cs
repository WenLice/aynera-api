using Aynera.Domain.Auth.Statics;
using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Domain.Audit.Requests;
using Aynera.Domain.Audit.Responses;
using Aynera.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>Admin audit event inbox (member restrict/unrestrict and related safety actions).</summary>
[Route("audit")]
[Tags("Audit")]
[Authorize(Policy = AuthPolicies.Admin)]
public sealed class AuditController : BaseController
{
    private readonly IAuditAdminService _audit;

    public AuditController(IAuditAdminService audit)
    {
        _audit = audit;
    }

    /// <summary>ListAuditEvents</summary>
    /// <remarks>
    /// When <c>memberId</c> or <c>subjectId</c> is set, returns all actions for that subject unless <c>action</c> is provided.
    /// </remarks>
    [HttpGet("events/GetAll")]
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
