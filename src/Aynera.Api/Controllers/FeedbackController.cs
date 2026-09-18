using Aynera.Domain.Auth.Statics;
using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Feedback.Services.Interfaces;
using Aynera.Domain.Common;
using Aynera.Domain.Feedback.Requests;
using Aynera.Domain.Feedback.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>Feedback submissions and administration.</summary>
[Route("feedback")]
[Tags("Feedback")]
public sealed class FeedbackController : BaseController
{
    private readonly IFeedbackService _feedback;

    public FeedbackController(IFeedbackService service)
    {
        _feedback = service;
    }

    /// <summary>SubmitFeedback</summary>
    /// <remarks>
    /// Grievance / feedback channel (safety, support, intermediary requests).
    /// Separate from product suggestions.
    /// </remarks>
    [HttpPost("Create")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<FeedbackSubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<FeedbackSubmissionDto>>> SubmitFeedback(
        [FromBody] SubmitFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _feedback.SubmitAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);

        return OkResponse(result);
    }
    /// <summary>ListFeedback</summary>
    /// <remarks>
    /// Grievance / support submissions, newest first, including name, email, phone, and message.
    /// Query: <c>page</c> (default 1) and <c>pageSize</c> (default 15, max 50).
    /// </remarks>
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpGet("GetAll")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<FeedbackSubmissionAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<FeedbackSubmissionAdminDto>>>> ListFeedback(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await _feedback.ListAsync(query, cancellationToken);
        return OkResponse(rows);
    }
}
