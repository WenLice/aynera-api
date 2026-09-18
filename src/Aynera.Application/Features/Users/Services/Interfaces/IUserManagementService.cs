using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Common;
using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Videos.Responses;

namespace Aynera.Application.Features.Users.Services.Interfaces;

public interface IUserManagementService
{
    Task<AuthAccountDto> CreateAdminAsync(
        Guid actorId,
        CreateAdminRequest request,
        CancellationToken cancellationToken);

    Task<AuthAccountDto> GetAdminMeAsync(Guid userId, CancellationToken cancellationToken);

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
