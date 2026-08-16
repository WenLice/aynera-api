using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Responses;
using Elaris.Domain.Common;
using Elaris.Domain.Photos.Responses;
using Elaris.Domain.Videos.Responses;

namespace Elaris.Application.Features.Auth.Services.Interfaces;

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

    Task<AuthAccountDto> CreateAdminAsync(
        Guid actorId,
        CreateAdminRequest request,
        CancellationToken cancellationToken);

    Task<AuthAccountDto> GetMeAsync(Guid userId, CancellationToken cancellationToken);

    Task<PagedResult<AuthAccountDto>> ListAdminsAsync(
        Guid actorId,
        PagedQuery query,
        CancellationToken cancellationToken);

    Task<PagedResult<MemberAdminDto>> ListMembersAsync(
        Guid actorId,
        MemberAdminListQuery query,
        CancellationToken cancellationToken);

    Task<MemberAdminDetailDto> GetMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken);

    Task<MemberPhotoBytes> GetMemberPhotoAsync(
        Guid actorId,
        Guid memberId,
        Guid photoId,
        CancellationToken cancellationToken);

    Task<IntroductionVideoBytes> GetMemberVideoAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken);

    Task<MemberAdminDto> RestrictMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken);

    Task<MemberAdminDto> UnrestrictMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken);

    Task<AuthAccountDto> DeactivateAdminAsync(
        Guid actorId,
        Guid targetId,
        CancellationToken cancellationToken);

    Task<AuthAccountDto> ActivateAdminAsync(
        Guid actorId,
        Guid targetId,
        CancellationToken cancellationToken);
}
