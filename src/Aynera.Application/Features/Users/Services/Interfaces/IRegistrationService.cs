using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;

namespace Aynera.Application.Features.Users.Services.Interfaces;

public interface IRegistrationService
{
    /// <summary>One-shot registration (website / API clients): every field at once, no OTP.</summary>
    Task<AuthAccountDto> RegisterAsync(CreateMemberRequest request, CancellationToken cancellationToken);

    /// <summary>App step 1: sends an SMS code to a number that has no account yet.</summary>
    Task<RequestMemberOtpResponse> StartPhoneRegistrationAsync(
        StartPhoneRegistrationRequest request,
        string? clientIp,
        CancellationToken cancellationToken);

    /// <summary>App step 2: consumes the SMS code, creates the Draft member account and signs it in.</summary>
    Task<TokenResponse> VerifyPhoneRegistrationAsync(
        VerifyPhoneRegistrationRequest request,
        CancellationToken cancellationToken);

    /// <summary>App step 3: emails a code to the address the signed-in member wants on the account.</summary>
    Task<RequestMemberOtpResponse> StartEmailVerificationAsync(
        Guid userId,
        StartEmailVerificationRequest request,
        string? clientIp,
        CancellationToken cancellationToken);

    /// <summary>App step 4: consumes the email code, sets the email on the account and confirms it.</summary>
    Task<AuthAccountDto> VerifyEmailCodeAsync(
        Guid userId,
        VerifyEmailCodeRequest request,
        CancellationToken cancellationToken);
}
