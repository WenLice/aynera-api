using Elaris.Api.Controllers.Base;
using Elaris.Application.Features.Feedback.Services.Interfaces;
using Elaris.Application.Features.Suggestions.Services.Interfaces;
using Elaris.Domain.Common;
using Elaris.Domain.Feedback.Responses;
using Elaris.Domain.Suggestions.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elaris.Api.Controllers;

/// <summary>
/// Admin read inboxes for suggestions and grievances.
/// </summary>
[Route("admin")]
[Tags("Admin")]
[Authorize(Policy = "Admin")]
public sealed class AdminInboxController : BaseController
{
    private readonly ISuggestionService _suggestions;
    private readonly IFeedbackService _feedback;

    public AdminInboxController(ISuggestionService suggestions, IFeedbackService feedback)
    {
        _suggestions = suggestions;
        _feedback = feedback;
    }

    /// <summary>ListSuggestions</summary>
    /// <remarks>
    /// Product-idea submissions, newest first, including name, email, phone, and message.
    /// Query: <c>page</c> (default 1) and <c>pageSize</c> (default 15, max 50).
    /// </remarks>
    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SuggestionAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<SuggestionAdminDto>>>> ListSuggestions(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await _suggestions.ListAsync(query, cancellationToken);
        return OkResponse(rows);
    }

    /// <summary>ListFeedback</summary>
    /// <remarks>
    /// Grievance / support submissions, newest first, including name, email, phone, and message.
    /// Query: <c>page</c> (default 1) and <c>pageSize</c> (default 15, max 50).
    /// </remarks>
    [HttpGet("feedback")]
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
