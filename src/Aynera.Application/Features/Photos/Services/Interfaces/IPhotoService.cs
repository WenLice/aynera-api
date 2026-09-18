using Aynera.Application.Features.Photos.Models;
using Aynera.Domain.Photos.Responses;

namespace Aynera.Application.Features.Photos.Services.Interfaces;

public interface IPhotoService
{
    Task<IReadOnlyList<MemberPhotoDto>> UploadAsync(
        Guid userId,
        IReadOnlyList<PhotoUploadInput> files,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MemberPhotoDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberPhotoBytes> GetBytesAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken);
}
