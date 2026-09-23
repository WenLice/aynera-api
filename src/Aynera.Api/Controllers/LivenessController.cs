using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Liveness.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// The private face check (AWS Rekognition Face Liveness). The app opens the page this returns; the
/// page streams video straight to AWS and only says "done"; the verdict is read here, from AWS.
/// </summary>
[Route("liveness")]
[Tags("Liveness")]
public sealed class LivenessController : BaseController
{
    private readonly ILivenessService _liveness;

    public LivenessController(ILivenessService liveness)
    {
        _liveness = liveness;
    }

    /// <summary>StartLiveness</summary>
    /// <remarks>
    /// Opens a face-liveness session. Open <c>pageUrl</c> in a WebView (phones) or an iframe (web);
    /// when the page posts <c>{ "type": "liveness", "status": "done" }</c>, call Complete. Requires the
    /// member's reference photo (<c>liveness_reference_photo_required</c>). 503 <c>liveness_unavailable</c>
    /// when AWS is not configured.
    /// </remarks>
    [HttpPost("Start")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<LivenessStartDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<LivenessStartDto>>> Start(CancellationToken cancellationToken) =>
        OkResponse(await _liveness.StartAsync(CurrentUser.UserId!.Value, cancellationToken));

    /// <summary>CompleteLiveness</summary>
    /// <remarks>
    /// Reads the session's result from AWS and records the verdict: <c>Passed</c> (live and the same face
    /// as the reference photo), <c>NotLive</c>, <c>FaceMismatch</c>, <c>Expired</c> or <c>Failed</c>.
    /// 409 <c>liveness_not_finished</c> if the member has not finished; 404 for a session that is not theirs.
    /// Repeating it for a finished session returns the same verdict.
    /// </remarks>
    [HttpPost("{sessionId}/Complete")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<LivenessResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<LivenessResultDto>>> Complete(
        string sessionId,
        CancellationToken cancellationToken) =>
        OkResponse(await _liveness.CompleteAsync(CurrentUser.UserId!.Value, sessionId, cancellationToken));

    /// <summary>GetMyLiveness</summary>
    /// <remarks>The member's latest finished face check, or 404 when there is none.</remarks>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<LivenessResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<LivenessResultDto>>> GetMine(CancellationToken cancellationToken)
    {
        var latest = await _liveness.GetLatestAsync(CurrentUser.UserId!.Value, cancellationToken);
        return latest is null
            ? FailResponse<LivenessResultDto>("liveness_not_found", StatusCodes.Status404NotFound)
            : OkResponse(latest);
    }
}
