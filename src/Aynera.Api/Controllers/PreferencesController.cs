using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Preferences.Services.Interfaces;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Preferences.Requests;
using Aynera.Domain.Preferences.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// The member's matching hard filters (MATCHMAKING-RULES §6). Private to the member and staff —
/// preferences are never shown on a profile.
/// </summary>
[Route("preferences")]
[Tags("Preferences")]
public sealed class PreferencesController : BaseController
{
    private readonly IMemberPreferencesService _preferences;

    public PreferencesController(IMemberPreferencesService preferences)
    {
        _preferences = preferences;
    }

    /// <summary>MyPreferences</summary>
    /// <remarks>
    /// Returns the authenticated member's matching preferences, or <c>null</c> in <c>data</c>
    /// if they have not saved them yet.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<MemberPreferencesDto?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<MemberPreferencesDto?>>> Me(
        CancellationToken cancellationToken)
    {
        var preferences = await _preferences.GetAsync(CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(preferences);
    }

    /// <summary>SavePreferences</summary>
    /// <remarks>
    /// Writes the authenticated member's matching preferences, creating them on the first save.
    /// A full replace, not a patch.
    /// Required: interestedIn (Male|Female|Other|Everyone), minAge, maxAge (18–45, min ≤ max),
    /// track (Fluid|Intent) and outcome (Platonic|Spontaneous|Prospect|Legacy).
    /// Optional: ageIsFlexible (default false), which widens the range by two years at each end
    /// when a pair is evaluated.
    /// The track must own the outcome — Platonic and Spontaneous are Fluid, Prospect and Legacy
    /// are Intent — or the write is refused with <c>validation_failed</c>.
    /// Preferences are required before <c>POST /admissions/me/submit</c> will accept a submission.
    /// </remarks>
    [HttpPut("me")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<MemberPreferencesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<MemberPreferencesDto>>> SavePreferences(
        [FromBody] UpdateMemberPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await _preferences.SaveAsync(
            CurrentUser.GetRequiredUserId(),
            request,
            cancellationToken);
        return OkResponse(saved);
    }
}
