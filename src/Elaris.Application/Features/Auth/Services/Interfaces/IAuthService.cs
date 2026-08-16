using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Responses;

namespace Elaris.Application.Features.Auth.Services.Interfaces;

public interface IAuthService
{
    Task<AuthAccountDto> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken);

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

    Task DeleteMemberAsync(Guid userId, CancellationToken cancellationToken);

    Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken);

    Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken);

    Task<AuthAccountDto> GetMeAsync(Guid userId, CancellationToken cancellationToken);
}
