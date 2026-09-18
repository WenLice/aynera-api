using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;

namespace Aynera.Application.Features.Users.Services.Interfaces;

public interface IAccountLifecycleService
{
    Task DeleteMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task<RequestMemberOtpResponse> RequestReactivationAsync(
        RequestMemberReactivationRequest request,
        string? clientIp,
        CancellationToken cancellationToken);
    Task RecoverMemberAsync(RecoverMemberRequest request, CancellationToken cancellationToken);
}
