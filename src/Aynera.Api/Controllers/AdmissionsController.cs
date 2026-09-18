using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Admissions.Services.Interfaces;
using Aynera.Domain.Admissions.Requests;
using Aynera.Domain.Admissions.Responses;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Member admission review. Approval here is one input to match eligibility, never the whole of
/// it: eligibility is recomputed from live account, profile, consent and identity evidence on
/// every read. See MATCHMAKING-RULES section 4.
/// </summary>
[Route("admissions")]
[Tags("Admissions")]
public sealed class AdmissionsController : BaseController
{
    private readonly IMemberAdmissionService _admissions;

    public AdmissionsController(IMemberAdmissionService admissions)
    {
        _admissions = admissions;
    }

    /// <summary>ListAdmissions</summary>
    /// <remarks>Staff review queue, oldest submission first. Optionally filtered by state.</remarks>
    [HttpGet("GetAll")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MemberAdmissionSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<MemberAdmissionSummaryDto>>>> List(
        CancellationToken cancellationToken,
        [FromQuery] string? state = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await _admissions.ListAsync(state, page, pageSize, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>GetMyAdmission</summary>
    /// <remarks>The signed-in member's own admission, consents and eligibility verdict.</remarks>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdmissionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MemberAdmissionDto>>> GetMine(
        CancellationToken cancellationToken)
    {
        var admission = await _admissions.GetAsync(CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(admission);
    }

    /// <summary>SubmitMyAdmission</summary>
    /// <remarks>
    /// Moves the member's own admission from Draft (or Rejected) to Submitted. Requires a completed
    /// profile and the minimum age. Resubmitting after a rejection clears the previous decision on
    /// the row; the rejection itself stays in the audit trail. Submitted, InReview and Approved
    /// admissions are refused with 409.
    /// </remarks>
    [HttpPost("me/submit")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MemberAdmissionDto>>> SubmitMine(
        CancellationToken cancellationToken)
    {
        var admission = await _admissions.SubmitAsync(CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(admission);
    }

    /// <summary>AcceptConsent</summary>
    /// <remarks>
    /// Records the signed-in member's acceptance of one policy document at one version. Accepting
    /// the same version twice is idempotent and keeps the original acceptance timestamp.
    /// </remarks>
    [HttpPost("me/consents")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MemberAdmissionDto>>> AcceptConsent(
        [FromBody] AcceptConsentRequest request,
        CancellationToken cancellationToken)
    {
        var admission = await _admissions.AcceptConsentAsync(
            CurrentUser.GetRequiredUserId(), request, cancellationToken);
        return OkResponse(admission);
    }

    /// <summary>GetMemberAdmission</summary>
    [HttpGet("{userId:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberAdmissionDto>>> Get(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var admission = await _admissions.GetAsync(userId, cancellationToken);
        return OkResponse(admission);
    }

    /// <summary>DecideAdmission</summary>
    /// <remarks>
    /// Applies a staff decision: StartReview, Approve, Reject (reason required) or Reopen.
    /// Transitions outside the admission transition table are refused with 409.
    /// </remarks>
    [HttpPost("{userId:guid}/decision")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MemberAdmissionDto>>> Decide(
        Guid userId,
        [FromBody] AdmissionDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var admission = await _admissions.DecideAsync(
            userId, request, CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(admission);
    }

    /// <summary>GetMemberEligibility</summary>
    /// <remarks>The computed verdict on its own, with the reason codes currently blocking it.</remarks>
    [HttpGet("{userId:guid}/eligibility")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<MemberEligibilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberEligibilityDto>>> GetEligibility(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var admission = await _admissions.GetAsync(userId, cancellationToken);
        return OkResponse(admission.Eligibility);
    }
}
