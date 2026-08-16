using Elaris.Api.Controllers.Base;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Responses;
using Elaris.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elaris.Api.Controllers;

/// <summary>
/// Admin authentication (OTP and password), current admin, members, and creating additional admins.
/// Refresh and logout stay on <c>/auth</c>.
/// </summary>
[Route("admin")]
[Tags("Admin")]
public sealed class AdminController : BaseController
{
    private readonly IAdminAuthService _adminAuth;

    public AdminController(IAdminAuthService adminAuth)
    {
        _adminAuth = adminAuth;
    }

    /// <summary>OtpRequest</summary>
    /// <remarks>
    /// Starts admin login by sending an OTP to a registered admin phone or email.
    /// Member accounts are rejected as user_not_found. There is no public admin register.
    /// OTP codes are never written to logs.
    /// Next step: OtpVerify with the same identifier and code.
    /// </remarks>
    [HttpPost("otp/request")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RequestMemberOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RequestMemberOtpResponse>>> OtpRequest(
        [FromBody] RequestAdminOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminAuth.RequestOtpAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return OkResponse(result);
    }

    /// <summary>OtpVerify</summary>
    /// <remarks>
    /// Verifies the admin OTP and issues tokens with <c>aud=admin</c> (access 1 hour, refresh 24 hours).
    /// </remarks>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> OtpVerify(
        [FromBody] VerifyAdminOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminAuth.VerifyOtpAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>PasswordLogin</summary>
    /// <remarks>
    /// Signs in a seeded admin with phone or email plus password.
    /// Issues tokens with <c>aud=admin</c> and <c>amr=pwd</c>. Member accounts are rejected as user_not_found.
    /// </remarks>
    [HttpPost("password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> PasswordLogin(
        [FromBody] AdminPasswordLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminAuth.LoginWithPasswordAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>Me</summary>
    /// <remarks>
    /// Returns the authenticated admin account for the current access token.
    /// <c>isSuperAdmin</c> is read from the database, not the JWT claim. <c>profile</c> is null.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> Me(CancellationToken cancellationToken)
    {
        var account = await _adminAuth.GetMeAsync(CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(account);
    }

    /// <summary>CreateAdmin</summary>
    /// <remarks>
    /// Creates another admin with a password. Only a super-admin may call this.
    /// Super-admin is a database flag set outside this API; authorization reads that flag, not the JWT claim.
    /// </remarks>
    [HttpPost("admins")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> CreateAdmin(
        [FromBody] CreateAdminRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _adminAuth.CreateAdminAsync(
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
    [HttpGet("admins")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuthAccountDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuthAccountDto>>>> ListAdmins(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _adminAuth.ListAdminsAsync(
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
    [HttpPost("admins/{id:guid}/deactivate")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> DeactivateAdmin(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await _adminAuth.DeactivateAdminAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(account);
    }

    /// <summary>ActivateAdmin</summary>
    /// <remarks>Reactivates a deactivated admin. Super-admin only.</remarks>
    [HttpPost("admins/{id:guid}/activate")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> ActivateAdmin(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await _adminAuth.ActivateAdminAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(account);
    }

    /// <summary>ListMembers</summary>
    /// <remarks>
    /// Lists members, newest first. Any admin. Soft-deleted members are omitted.
    /// Query: <c>page</c>, <c>pageSize</c>, optional <c>search</c> (email, phone, first/last name),
    /// optional <c>isActive</c>, optional <c>isRestricted</c>.
    /// </remarks>
    [HttpGet("members")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MemberAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<MemberAdminDto>>>> ListMembers(
        [FromQuery] MemberAdminListQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _adminAuth.ListMembersAsync(
            CurrentUser.GetRequiredUserId(),
            query,
            cancellationToken);
        return OkResponse(page);
    }

    /// <summary>GetMember</summary>
    /// <remarks>
    /// Returns one member: profile fields plus photo and introduction-video metadata.
    /// Image and video bytes are on GetMemberPhoto and GetMemberVideo. Any admin. Soft-deleted members are omitted.
    /// </remarks>
    [HttpGet("members/{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<MemberAdminDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberAdminDetailDto>>> GetMember(
        Guid id,
        CancellationToken cancellationToken)
    {
        var member = await _adminAuth.GetMemberAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(member);
    }

    /// <summary>GetMemberPhoto</summary>
    /// <remarks>Returns image bytes for one photo on a member. Any admin.</remarks>
    [HttpGet("members/{id:guid}/photos/{photoId:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberPhoto(
        Guid id,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var photo = await _adminAuth.GetMemberPhotoAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            photoId,
            cancellationToken);
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>GetMemberVideo</summary>
    /// <remarks>Returns introduction-video bytes for a member. Any admin.</remarks>
    [HttpGet("members/{id:guid}/introduction-video/content")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberVideo(Guid id, CancellationToken cancellationToken)
    {
        var video = await _adminAuth.GetMemberVideoAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return File(video.Data, video.ContentType);
    }

    /// <summary>RestrictMember</summary>
    /// <remarks>
    /// Restricts a member account (blocks sign-in) and revokes refresh sessions. Super-admin only.
    /// Independent of member self-deactivate/activate. Soft-deleted members are omitted (404).
    /// </remarks>
    [HttpPost("members/{id:guid}/restrict")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<MemberAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberAdminDto>>> RestrictMember(
        Guid id,
        CancellationToken cancellationToken)
    {
        var member = await _adminAuth.RestrictMemberAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(member);
    }

    /// <summary>UnrestrictMember</summary>
    /// <remarks>
    /// Removes a super-admin restriction from a member. Super-admin only.
    /// Soft-deleted members are omitted (404). Already unrestricted members are returned unchanged.
    /// </remarks>
    [HttpPost("members/{id:guid}/unrestrict")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<MemberAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberAdminDto>>> UnrestrictMember(
        Guid id,
        CancellationToken cancellationToken)
    {
        var member = await _adminAuth.UnrestrictMemberAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(member);
    }
}
