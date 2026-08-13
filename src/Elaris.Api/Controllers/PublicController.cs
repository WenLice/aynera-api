using Elaris.Api.Controllers.Base;
using Elaris.Application.Features.Feedback.Services.Interfaces;
using Elaris.Application.Features.Suggestions.Services.Interfaces;
using Elaris.Domain.Common;
using Elaris.Domain.Feedback.Requests;
using Elaris.Domain.Feedback.Responses;
using Elaris.Domain.Suggestions.Requests;
using Elaris.Domain.Suggestions.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elaris.Api.Controllers;

/// <summary>
/// Anonymous marketing forms: suggestions (ideas) and feedback (grievance).
/// </summary>
[Route("public")]
[Tags("Public")]
public sealed class PublicController : BaseController
{
    private readonly ISuggestionService _suggestions;
    private readonly IFeedbackService _feedback;

    public PublicController(ISuggestionService suggestions, IFeedbackService feedback)
    {
        _suggestions = suggestions;
        _feedback = feedback;
    }

    /// <summary>SubmitSuggestion</summary>
    /// <remarks>Product idea submissions from the marketing suggestion box.</remarks>
    [HttpPost("suggestions")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<SuggestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<SuggestionDto>>> SubmitSuggestion(
        [FromBody] SubmitSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _suggestions.SubmitAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);

        return OkResponse(result);
    }

    /// <summary>SubmitFeedback</summary>
    /// <remarks>
    /// Grievance / feedback channel (safety, support, intermediary requests).
    /// Separate from product suggestions.
    /// </remarks>
    [HttpPost("feedback")]
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
}
