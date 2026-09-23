using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Application.Features.Videos.Models;
using Aynera.Application.Features.Videos.Services.Interfaces;
using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Videos.Responses;

namespace Aynera.Application.Tests;

internal sealed class FakePhotoReads : IPhotoService
{
    public Task UpdateCaptionAsync(Guid userId, Guid photoId, string? caption, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<MemberPhotoDto>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemberPhotoDto>>([]);

    public Task<MemberPhotoBytes> GetBytesAsync(Guid userId, Guid photoId, CancellationToken cancellationToken) =>
        Task.FromResult(new MemberPhotoBytes(photoId, "image/jpeg", [1, 2, 3]));

    public Task<IReadOnlyList<MemberPhotoDto>> UploadAsync(Guid userId, IReadOnlyList<PhotoUploadInput> files, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task DeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeVideoReads : IIntroductionVideoService
{
    public Task UpdateCaptionAsync(Guid userId, string? caption, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IntroductionVideoDto?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IntroductionVideoDto?>(null);

    public Task<IntroductionVideoBytes> GetBytesAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(new IntroductionVideoBytes(userId, "video/mp4", [4, 5, 6]));

    public Task<IntroductionVideoDto> UploadAsync(Guid userId, VideoUploadInput file, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
