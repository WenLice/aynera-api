using Elaris.Api.Controllers.Base;
using Elaris.Application.Features.EarlyAccess.Services.Interfaces;
using Elaris.Domain.Auth.Statics;
using Elaris.Domain.Common;
using Elaris.Domain.EarlyAccess.Requests;
using Elaris.Domain.EarlyAccess.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elaris.Api.Controllers;

/// <summary>
/// Early-access waitlist and city catalog. Public read/register; city writes and waitlist list require Admin.
/// </summary>
[Route("early-access")]
[Tags("EarlyAccess")]
public sealed class EarlyAccessController : BaseController
{
    private readonly IEarlyAccessService _earlyAccess;
    private readonly IEarlyAccessCityService _cities;

    public EarlyAccessController(IEarlyAccessService earlyAccess, IEarlyAccessCityService cities)
    {
        _earlyAccess = earlyAccess;
        _cities = cities;
    }

    /// <summary>ListEarlyAccessCities</summary>
    /// <remarks>
    /// Anonymous callers receive active cities for /apply.
    /// Admin callers receive the full catalog (including inactive).
    /// </remarks>
    [HttpGet("cities")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EarlyAccessCityDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EarlyAccessCityDto>>>> ListCities(
        CancellationToken cancellationToken)
    {
        if (User.IsInRole(AuthRoles.Admin))
        {
            var all = await _cities.ListAsync(cancellationToken);
            return OkResponse(all);
        }

        var open = await _earlyAccess.ListOpenCitiesAsync(cancellationToken);
        return OkResponse(open);
    }

    /// <summary>CreateEarlyAccessCity</summary>
    [HttpPost("cities")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<EarlyAccessCityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EarlyAccessCityDto>>> CreateCity(
        [FromBody] CreateEarlyAccessCityRequest request,
        CancellationToken cancellationToken)
    {
        var city = await _cities.CreateAsync(request, cancellationToken);
        return OkResponse(city);
    }

    /// <summary>UpdateEarlyAccessCity</summary>
    [HttpPatch("cities/{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<EarlyAccessCityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EarlyAccessCityDto>>> UpdateCity(
        Guid id,
        [FromBody] UpdateEarlyAccessCityRequest request,
        CancellationToken cancellationToken)
    {
        var city = await _cities.UpdateAsync(id, request, cancellationToken);
        return OkResponse(city);
    }

    /// <summary>DeleteEarlyAccessCity</summary>
    /// <remarks>Soft-deletes the city so it no longer appears for anonymous GET /early-access/cities.</remarks>
    [HttpDelete("cities/{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteCity(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _cities.SoftDeleteAsync(id, cancellationToken);
        return OkResponse();
    }

    /// <summary>ListEarlyAccessSignups</summary>
    /// <remarks>
    /// Waitlist inbox for admins. Newest first. Public register responses stay thin; this list includes contact fields.
    /// Soft-deleted rows are omitted. Query: <c>page</c> (default 1) and <c>pageSize</c> (default 15, max 50).
    /// </remarks>
    [HttpGet("signups")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EarlyAccessSignupAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<EarlyAccessSignupAdminDto>>>> ListSignups(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await _earlyAccess.ListSignupsAsync(query, cancellationToken);
        return OkResponse(rows);
    }

    /// <summary>RegisterEarlyAccess</summary>
    /// <remarks>
    /// Early Register: joins or updates the waitlist by email.
    /// City must be an open (active) early-access city.
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<EarlyAccessSignupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<EarlyAccessSignupDto>>> Register(
        [FromBody] JoinEarlyAccessRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _earlyAccess.RegisterAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);

        return OkResponse(result);
    }
}
