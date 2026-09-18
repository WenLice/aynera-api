using Aynera.Domain.Photos.Records;

namespace Aynera.Application.Features.Photos.Repositories;

public interface IMemberPhotoRepository
{
    Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemberPhotoRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<MemberPhotoRecord?> FindByIdAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken);

    Task<MemberPhotoRecord?> FindReferenceAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> NextSortOrderAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberPhotoRecord> AddAsync(MemberPhotoRecord photo, CancellationToken cancellationToken);

    Task SoftDeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken);

    Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task PromoteNextReferenceAsync(Guid userId, CancellationToken cancellationToken);
}
