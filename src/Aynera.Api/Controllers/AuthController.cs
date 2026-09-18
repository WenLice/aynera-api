using Aynera.Api.Controllers.Base;
using Aynera.Api.OpenApi;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Authentication (login, email/SMS verification, tokens).
/// </summary>
[Route("auth")]
[Tags("Auth")]
public sealed class AuthController : BaseController
{
    private readonly IAdminAuthService _adminAuth;
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService, IAdminAuthService adminAuth)
    {
        _authService = authService;
        _adminAuth = adminAuth;
    }

    /// <summary>VerifyEmail</summary>
    /// <remarks>
    /// Confirms the email address using the token from the verification link sent at Register.
    /// There is no separate “send verification email” endpoint — the link is sent automatically on Register.
    /// Call with JSON body after the user opens the email link (typically from the member app).
    /// Idempotent if the email is already confirmed.
    /// </remarks>
    [HttpPost("verifyemail")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> VerifyEmail(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _authService.ConfirmEmailAsync(request, cancellationToken);
        return OkResponse(account);
    }

    /// <summary>Login</summary>
    /// <remarks>
    /// Starts member login by sending an OTP to a registered Indian mobile number or email.
    /// Fails with user_not_found if the identifier is not a member account (Register first).
    /// Admin accounts are rejected with the same user_not_found response.
    /// Dev default is Console (no send). OTP codes are never written to logs.
    /// Next step: VerifySms with the same identifier and the OTP code (SMS or email).
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RequestMemberOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RequestMemberOtpResponse>>> Login(
        [FromBody] RequestMemberOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RequestMemberOtpAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return OkResponse(result);
    }

    /// <summary>VerifySms</summary>
    /// <remarks>
    /// Verifies the SMS or email OTP for an existing registered member, marks that identifier confirmed, and issues tokens.
    /// Does not create accounts — Register first, then Login, then VerifySms.
    /// Deactivated accounts cannot complete login until Reactivate.
    /// </remarks>
    [HttpPost("verifysms")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> VerifySms(
        [FromBody] VerifyMemberOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.VerifyMemberOtpAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>PasswordLogin</summary>
    /// <remarks>
    /// Signs in a registered member with phone or email plus password. Issues member tokens (<c>aud=member</c>, <c>amr=pwd</c>).
    /// Fails with password_not_set if the account has no password yet — use OTP login, then SetPassword, or register with a password.
    /// Admin accounts are rejected as user_not_found.
    /// </remarks>
    [HttpPost("password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> PasswordLogin(
        [FromBody] MemberPasswordLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginWithPasswordAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>ForgotPassword</summary>
    /// <remarks>
    /// Sends a password-reset OTP to the given phone or email when a matching active member exists.
    /// Always returns 200 with the same shape as Login so callers cannot probe whether an account exists.
    /// OTP codes are never written to logs.
    /// Next step: ResetPassword with the same identifier, the code, and a new password.
    /// </remarks>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RequestMemberOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RequestMemberOtpResponse>>> ForgotPassword(
        [FromBody] ForgotMemberPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RequestPasswordResetAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return OkResponse(result);
    }

    /// <summary>ResetPassword</summary>
    /// <remarks>
    /// Verifies the password-reset OTP, sets a new password, and issues member tokens.
    /// </remarks>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> ResetPassword(
        [FromBody] ResetMemberPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>Refresh</summary>
    /// <remarks>
    /// Rotates the refresh token and returns a new access + refresh pair.
    /// Reuse of an already-rotated refresh token revokes the whole session family.
    /// </remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>Logout</summary>
    /// <remarks>
    /// Revokes the refresh session for the given refresh token.
    /// Safe to call if the token is already unknown or revoked.
    /// </remarks>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request, cancellationToken);
        return OkResponse();
    }
    /// <summary>AdminOtpRequest</summary>
    /// <remarks>
    /// Starts admin login by sending an OTP to a registered admin phone or email.
    /// Member accounts are rejected as user_not_found. There is no public admin register.
    /// OTP codes are never written to logs.
    /// Next step: OtpVerify with the same identifier and code.
    /// </remarks>
    [HttpPost("admin/otp/request")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = ApiAudience.Admin)]
    [ProducesResponseType(typeof(ApiResponse<RequestMemberOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RequestMemberOtpResponse>>> AdminOtpRequest(
        [FromBody] RequestAdminOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminAuth.RequestOtpAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return OkResponse(result);
    }

    /// <summary>AdminOtpVerify</summary>
    /// <remarks>
    /// Verifies the admin OTP and issues tokens with <c>aud=admin</c> (access 1 hour, refresh 24 hours).
    /// </remarks>
    [HttpPost("admin/otp/verify")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = ApiAudience.Admin)]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> AdminOtpVerify(
        [FromBody] VerifyAdminOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminAuth.VerifyOtpAsync(request, cancellationToken);
        return OkResponse(result);
    }

    /// <summary>AdminPasswordLogin</summary>
    /// <remarks>
    /// Signs in a seeded admin with phone or email plus password.
    /// Issues tokens with <c>aud=admin</c> and <c>amr=pwd</c>. Member accounts are rejected as user_not_found.
    /// </remarks>
    [HttpPost("admin/password")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = ApiAudience.Admin)]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> AdminPasswordLogin(
        [FromBody] AdminPasswordLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminAuth.LoginWithPasswordAsync(request, cancellationToken);
        return OkResponse(result);
    }

}
