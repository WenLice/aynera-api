using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;

namespace Aynera.Application.Features.Auth.Services.Interfaces;

public interface IAuthService
{
    Task<AuthAccountDto> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken);

    Task<RequestMemberOtpResponse> RequestMemberOtpAsync(
        RequestMemberOtpRequest request,
        string? clientIp,
        CancellationToken cancellationToken);

    Task<TokenResponse> VerifyMemberOtpAsync(
        VerifyMemberOtpRequest request,
        CancellationToken cancellationToken);

    Task<TokenResponse> LoginWithPasswordAsync(
        MemberPasswordLoginRequest request,
        CancellationToken cancellationToken);

    Task SetPasswordAsync(
        Guid userId,
        SetMemberPasswordRequest request,
        CancellationToken cancellationToken);

    Task<RequestMemberOtpResponse> RequestPasswordResetAsync(
        ForgotMemberPasswordRequest request,
        string? clientIp,
        CancellationToken cancellationToken);

    Task<TokenResponse> ResetPasswordAsync(
        ResetMemberPasswordRequest request,
        CancellationToken cancellationToken);

    Task<TokenResponse> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken);

    Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken);

    Task<AuthAccountDto> GetMeAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a member access + refresh pair for an account whose ownership was just proven
    /// elsewhere (step-wise registration). Runs the same account-state checks as login.
    /// </summary>
    Task<TokenResponse> IssueMemberSessionAsync(Guid userId, string amr, CancellationToken cancellationToken);
}
