using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;

namespace Aynera.Application.Features.Auth.Services.Interfaces;

public interface IAdminAuthService
{
    Task<RequestMemberOtpResponse> RequestOtpAsync(
        RequestAdminOtpRequest request,
        string? clientIp,
        CancellationToken cancellationToken);

    Task<TokenResponse> VerifyOtpAsync(
        VerifyAdminOtpRequest request,
        CancellationToken cancellationToken);

    Task<TokenResponse> LoginWithPasswordAsync(
        AdminPasswordLoginRequest request,
        CancellationToken cancellationToken);
}
