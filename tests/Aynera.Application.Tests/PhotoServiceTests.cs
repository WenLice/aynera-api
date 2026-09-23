using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Photos.Services.Implementations;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Domain.Photos.Records;
using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Photos.Enums;
using Aynera.Domain.Photos.Exceptions;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Tests;

public class PhotoServiceTests
{
    [Fact]
    public async Task Upload_FirstPhoto_BecomesReference()
    {
        var photos = new InMemoryPhotoRepository();
        var service = CreateService(photos);

        await using var stream = new MemoryStream([1, 2, 3]);
        var result = await service.UploadAsync(
            Guid.NewGuid(),
            [new PhotoUploadInput(stream, "a.jpg", "image/jpeg", 3)],
            CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].IsReference);
        Assert.Equal(FaceMatchStatus.Matched.ToString(), result[0].FaceMatchStatus);
    }

    [Fact]
    public async Task Upload_ExceedsMaxCount_Throws()
    {
        var photos = new InMemoryPhotoRepository();
        var userId = Guid.NewGuid();
        var service = CreateService(photos, maxCount: 1);

        await using var first = new MemoryStream([1]);
        await service.UploadAsync(
            userId,
            [new PhotoUploadInput(first, "a.jpg", "image/jpeg", 1)],
            CancellationToken.None);

        await using var second = new MemoryStream([2]);
        var ex = await Assert.ThrowsAsync<PhotoException>(() =>
            service.UploadAsync(
                userId,
                [new PhotoUploadInput(second, "b.jpg", "image/jpeg", 1)],
                CancellationToken.None));

        Assert.Equal("photo_limit_exceeded", ex.ErrorCode);
    }

    [Fact]
    public async Task Upload_BeforeTheFaceCheck_IsRefused()
    {
        var service = CreateService(new InMemoryPhotoRepository(), verified: FixedVerifiedFace.NotVerified);

        await using var stream = new MemoryStream([1]);
        var ex = await Assert.ThrowsAsync<PhotoException>(() =>
            service.UploadAsync(Guid.NewGuid(), [new PhotoUploadInput(stream, "a.jpg", "image/jpeg", 1)], CancellationToken.None));

        Assert.Equal("photo_face_check_required", ex.ErrorCode);
    }

    /// <summary>Slots 1 and 2 must show the member; someone else is refused.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MustMatchSlot_SomeoneElse_IsRefused(int slot)
    {
        var service = CreateService(new InMemoryPhotoRepository(), stubStatus: FaceMatchStatus.Rejected);

        await using var stream = new MemoryStream([1]);
        var ex = await Assert.ThrowsAsync<PhotoException>(() =>
            service.UploadAsync(Guid.NewGuid(), [new PhotoUploadInput(stream, "a.jpg", "image/jpeg", 1, Slot: slot)], CancellationToken.None));

        Assert.Equal("photo_face_mismatch", ex.ErrorCode);
    }

    [Fact]
    public async Task MustMatchSlot_WithNoFace_AsksForAFace()
    {
        var service = CreateService(new InMemoryPhotoRepository(), stubStatus: FaceMatchStatus.Skipped);

        await using var stream = new MemoryStream([1]);
        var ex = await Assert.ThrowsAsync<PhotoException>(() =>
            service.UploadAsync(Guid.NewGuid(), [new PhotoUploadInput(stream, "a.jpg", "image/jpeg", 1, Slot: 1)], CancellationToken.None));

        Assert.Equal("photo_face_required", ex.ErrorCode);
    }

    /// <summary>
    /// "Your world", "something you love": a friend's face is allowed. It is recorded as Skipped, so a
    /// curator can see it is not the member without it counting as an identity rejection.
    /// </summary>
    [Theory]
    [InlineData(3, FaceMatchStatus.Rejected, FaceMatchStatus.Skipped)]
    [InlineData(4, FaceMatchStatus.Skipped, FaceMatchStatus.Skipped)]
    [InlineData(5, FaceMatchStatus.Matched, FaceMatchStatus.Matched)]
    public async Task FreeSlot_IsNeverBlocked_AndRecordsWhatItFound(int slot, FaceMatchStatus found, FaceMatchStatus recorded)
    {
        var service = CreateService(new InMemoryPhotoRepository(), stubStatus: found);

        await using var stream = new MemoryStream([1]);
        var result = await service.UploadAsync(
            Guid.NewGuid(), [new PhotoUploadInput(stream, "a.jpg", "image/jpeg", 1, Slot: slot)], CancellationToken.None);

        Assert.Equal(recorded.ToString(), Assert.Single(result).FaceMatchStatus);
    }

    [Fact]
    public async Task Upload_AiGenerated_Throws()
    {
        var photos = new InMemoryPhotoRepository();
        var service = CreateService(photos, authenticity: new RejectAiMediaService());

        await using var stream = new MemoryStream([1, 2, 3]);
        var ex = await Assert.ThrowsAsync<PhotoException>(() =>
            service.UploadAsync(
                Guid.NewGuid(),
                [new PhotoUploadInput(stream, "a.jpg", "image/jpeg", 3)],
                CancellationToken.None));

        Assert.Equal("photo_ai_generated", ex.ErrorCode);
    }

    private static PhotoService CreateService(
        IMemberPhotoRepository photos,
        int maxCount = 6,
        FaceMatchStatus stubStatus = FaceMatchStatus.Matched,
        Aynera.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService? authenticity = null,
        FixedVerifiedFace? verified = null) =>
        new(
            photos,
            new PassthroughImageProcessor(),
            new ConfigurableFaceMatchService(stubStatus),
            authenticity ?? new AlwaysAuthenticMediaService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<PhotoService>.Instance,
            Options.Create(new PhotoOptions
            {
                MaxCount = maxCount,
                MaxBytes = 5 * 1024 * 1024,
                StubFaceMatchStatus = stubStatus.ToString()
            }),
            NullMediaStorage.Instance,
            verified ?? FixedVerifiedFace.Verified);
}

file sealed class AlwaysAuthenticMediaService : Aynera.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService
{
    public Task<Aynera.Domain.Media.Responses.MediaAuthenticityResult> AssessImageAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Aynera.Domain.Media.Responses.MediaAuthenticityResult(true));

    public Task<Aynera.Domain.Media.Responses.MediaAuthenticityResult> AssessVideoAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Aynera.Domain.Media.Responses.MediaAuthenticityResult(true));
}

file sealed class RejectAiMediaService : Aynera.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService
{
    public Task<Aynera.Domain.Media.Responses.MediaAuthenticityResult> AssessImageAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Aynera.Domain.Media.Responses.MediaAuthenticityResult(
            false,
            "AI-generated photos are not allowed.",
            "test"));

    public Task<Aynera.Domain.Media.Responses.MediaAuthenticityResult> AssessVideoAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Aynera.Domain.Media.Responses.MediaAuthenticityResult(
            false,
            "AI-generated videos are not allowed.",
            "test"));
}

file sealed class PassthroughImageProcessor : IImageProcessor
{
    public Task<ProcessedImage> ProcessAsync(
        Stream input,
        string? contentType,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        var bytes = ms.ToArray();
        return Task.FromResult(new ProcessedImage(bytes, "image/jpeg", bytes.Length));
    }
}

file sealed class ConfigurableFaceMatchService(FaceMatchStatus status) : IFaceMatchService
{
    public Task<FaceMatchResult> CompareAsync(
        byte[] referenceImage,
        string referenceContentType,
        byte[] candidateImage,
        string candidateContentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FaceMatchResult(
            status.ToString(),
            status == FaceMatchStatus.Rejected ? 10m : 99m,
            status == FaceMatchStatus.Rejected ? "rejected" : null));
}

file sealed class InMemoryPhotoRepository : IMemberPhotoRepository
{
    public Task UpdateCaptionAsync(Guid userId, Guid photoId, string? caption, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private readonly List<MemberPhotoRecord> _photos = [];

    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_photos.Count(x => x.UserId == userId));

    public Task<IReadOnlyList<MemberPhotoRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemberPhotoRecord>>(
            _photos.Where(x => x.UserId == userId).OrderBy(x => x.SortOrder).ToList());

    public Task<MemberPhotoRecord?> FindByIdAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_photos.FirstOrDefault(x => x.UserId == userId && x.Id == photoId));

    public Task<MemberPhotoRecord?> FindReferenceAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_photos.FirstOrDefault(x => x.UserId == userId && x.IsReference));

    public Task<int> NextSortOrderAsync(Guid userId, CancellationToken cancellationToken)
    {
        var max = _photos.Where(x => x.UserId == userId).Select(x => x.SortOrder).DefaultIfEmpty(-1).Max();
        return Task.FromResult(max + 1);
    }

    public Task<MemberPhotoRecord> AddAsync(MemberPhotoRecord photo, CancellationToken cancellationToken)
    {
        _photos.Add(photo);
        return Task.FromResult(photo);
    }

    public Task SoftDeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken)
    {
        _photos.RemoveAll(x => x.UserId == userId && x.Id == photoId);
        return Task.CompletedTask;
    }

    public Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        _photos.RemoveAll(x => x.UserId == userId);
        return Task.CompletedTask;
    }

    public Task PromoteNextReferenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var next = _photos.Where(x => x.UserId == userId).OrderBy(x => x.SortOrder).FirstOrDefault();
        if (next is null)
        {
            return Task.CompletedTask;
        }

        var idx = _photos.FindIndex(x => x.Id == next.Id);
        _photos[idx] = next with { IsReference = true };
        return Task.CompletedTask;
    }
}
