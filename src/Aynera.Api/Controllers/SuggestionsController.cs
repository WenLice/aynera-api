using Aynera.Domain.Auth.Statics;
using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Suggestions.Services.Interfaces;
using Aynera.Domain.Common;
using Aynera.Domain.Suggestions.Requests;
using Aynera.Domain.Suggestions.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>Suggestions submissions and administration.</summary>
[Route("suggestions")]
[Tags("Suggestions")]
public sealed class SuggestionsController : BaseController
{
    private readonly ISuggestionService _suggestions;

    public SuggestionsController(ISuggestionService service)
    {
        _suggestions = service;
    }

    /// <summary>SubmitSuggestion</summary>
    /// <remarks>Product idea submissions from the marketing suggestion box.</remarks>
    [HttpPost("Create")]
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

    /// <summary>ListSuggestions</summary>
    /// <remarks>
    /// Product-idea submissions, newest first, including name, email, phone, and message.
    /// Query: <c>page</c> (default 1) and <c>pageSize</c> (default 15, max 50).
    /// </remarks>
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpGet("GetAll")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SuggestionAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<SuggestionAdminDto>>>> ListSuggestions(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await _suggestions.ListAsync(query, cancellationToken);
        return OkResponse(rows);
    }

}
