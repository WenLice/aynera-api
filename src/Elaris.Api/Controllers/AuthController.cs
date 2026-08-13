using Elaris.Api.Controllers.Base;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Responses;
using Elaris.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elaris.Api.Controllers;

/// <summary>
/// Authentication (login, email/SMS verification, tokens).
/// </summary>
[Route("auth")]
[Tags("Auth")]
public sealed class AuthController : BaseController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>VerifyEmail</summary>
    /// <remarks>
    /// Confirms the email address using the token from the verification link sent at Register.
    /// There is no separate “send verification email” endpoint — the link is sent automatically on Register.
    /// Call with JSON body after the user opens the email link (typically from member-web).
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
    /// Starts phone login / SMS verification by sending an OTP to a registered Indian mobile number.
    /// Fails with user_not_found if the phone is not registered (Register first).
    /// Dev default is Console (no send). Set SMS provider to Textbelt for free-tier SMS, or wire a paid provider later.
    /// OTP codes are never written to logs.
    /// Next step: VerifySms with the same phone and the OTP code.
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
    /// Verifies the SMS OTP for an existing registered member, marks the phone confirmed, and issues tokens.
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
}
