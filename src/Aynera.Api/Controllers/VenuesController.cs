using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Venues.Services.Interfaces;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Venues.Requests;
using Aynera.Domain.Venues.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Partner café and event-place catalog. Admin-only CRUD. Full address and venue-contact
/// details are staff-only and are never exposed to members.
/// </summary>
[Route("venues")]
[Tags("Venues")]
public sealed class VenuesController : BaseController
{
    private readonly IVenueService _venues;

    public VenuesController(IVenueService venues)
    {
        _venues = venues;
    }

    /// <summary>ListVenues</summary>
    /// <remarks>Returns the full catalog (including inactive venues); soft-deleted rows are omitted.</remarks>
    [HttpGet("GetAll")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VenueDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VenueDto>>>> List(
        CancellationToken cancellationToken)
    {
        var venues = await _venues.ListAsync(cancellationToken);
        return OkResponse(venues);
    }

    /// <summary>CreateVenue</summary>
    [HttpPost("Create")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<VenueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<VenueDto>>> Create(
        [FromBody] CreateVenueRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await _venues.CreateAsync(request, cancellationToken);
        return OkResponse(venue);
    }

    /// <summary>UpdateVenue</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<VenueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<VenueDto>>> Update(
        Guid id,
        [FromBody] UpdateVenueRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await _venues.UpdateAsync(id, request, cancellationToken);
        return OkResponse(venue);
    }

    /// <summary>DeleteVenue</summary>
    /// <remarks>Soft-deletes the venue so it no longer appears in the catalog.</remarks>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _venues.SoftDeleteAsync(id, cancellationToken);
        return OkResponse();
    }

    /// <summary>SendVenueHeadsUp</summary>
    /// <remarks>
    /// Queues an email + SMS heads-up to the venue's contact that a party of members plans to visit.
    /// Delivery is durable and retried by a background worker; no member identities are sent.
    /// </remarks>
    [HttpPost("{id:guid}/notifications/Create")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VenueNotificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VenueNotificationDto>>>> SendHeadsUp(
        Guid id,
        [FromBody] SendVenueHeadsUpRequest request,
        CancellationToken cancellationToken)
    {
        var queued = await _venues.QueueHeadsUpAsync(
            id, request, CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(queued);
    }

    /// <summary>ListVenueNotifications</summary>
    /// <remarks>Heads-up history for a venue, newest first.</remarks>
    [HttpGet("{id:guid}/notifications/GetAll")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VenueNotificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VenueNotificationDto>>>> ListNotifications(
        Guid id,
        CancellationToken cancellationToken)
    {
        var notifications = await _venues.ListNotificationsAsync(id, cancellationToken);
        return OkResponse(notifications);
    }
}
