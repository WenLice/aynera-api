using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Administrator accounts: the authenticated admin's own account and super-admin management of other admins.
/// Admin sign-in lives under <c>auth/admin/*</c>.
/// </summary>
[Route("admins")]
[Tags("Admins")]
public sealed class AdminsController : BaseController
{
    private readonly IUserManagementService _userManagement;

    public AdminsController(IUserManagementService userManagement)
    {
        _userManagement = userManagement;
    }

    /// <summary>AdminMe</summary>
    /// <remarks>
    /// Returns the authenticated admin account for the current access token.
    /// <c>isSuperAdmin</c> is read from the database, not the JWT claim. <c>profile</c> is null.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> AdminMe(CancellationToken cancellationToken)
    {
        var account = await _userManagement.GetAdminMeAsync(CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(account);
    }

    /// <summary>CreateAdmin</summary>
    /// <remarks>
    /// Creates another admin with a password. Only a super-admin may call this.
    /// Super-admin is a database flag set outside this API; authorization reads that flag, not the JWT claim.
    /// </remarks>
    [HttpPost("Create")]
    [Authorize(Policy = AuthPolicies.SuperAdmin)]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> CreateAdmin(
        [FromBody] CreateAdminRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _userManagement.CreateAdminAsync(
            CurrentUser.GetRequiredUserId(),
            request,
            cancellationToken);
        return OkResponse(account);
    }

    /// <summary>ListAdmins</summary>
    /// <remarks>
    /// Lists admin accounts. Super-admin only. Includes inactive admins.
    /// Query: <c>page</c> (default 1) and <c>pageSize</c> (default 15, max 50).
    /// </remarks>
    [HttpGet("GetAll")]
    [Authorize(Policy = AuthPolicies.SuperAdmin)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuthAccountDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuthAccountDto>>>> ListAdmins(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _userManagement.ListAdminsAsync(
            CurrentUser.GetRequiredUserId(),
            query,
            cancellationToken);
        return OkResponse(page);
    }

    /// <summary>DeactivateAdmin</summary>
    /// <remarks>
    /// Deactivates another admin and revokes their refresh sessions. Super-admin only.
    /// Cannot deactivate yourself, the last active admin, or the last active super-admin.
    /// </remarks>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = AuthPolicies.SuperAdmin)]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> DeactivateAdmin(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await _userManagement.DeactivateAdminAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(account);
    }

    /// <summary>ActivateAdmin</summary>
    /// <remarks>Reactivates a deactivated admin. Super-admin only.</remarks>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = AuthPolicies.SuperAdmin)]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> ActivateAdmin(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await _userManagement.ActivateAdminAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(account);
    }
}
