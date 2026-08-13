using Elaris.Application.Features.Photos.Models;
using Elaris.Application.Features.Photos.Repositories;
using Elaris.Application.Features.Photos.Services.Implementations;
using Elaris.Application.Features.Photos.Services.Interfaces;
using Elaris.Domain.Photos.Records;
using Elaris.Domain.Photos.Responses;
using Elaris.Domain.Photos.Enums;
using Elaris.Domain.Photos.Exceptions;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Tests;

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
        Assert.Equal(FaceMatchStatus.Pending.ToString(), result[0].FaceMatchStatus);
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
    public async Task Upload_RejectedFaceMatch_Throws()
    {
        var photos = new InMemoryPhotoRepository();
        var userId = Guid.NewGuid();
        var service = CreateService(photos, stubStatus: FaceMatchStatus.Rejected);

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

        Assert.Equal("photo_face_mismatch", ex.ErrorCode);
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
        Elaris.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService? authenticity = null) =>
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
            }));
}

file sealed class AlwaysAuthenticMediaService : Elaris.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService
{
    public Task<Elaris.Domain.Media.Responses.MediaAuthenticityResult> AssessImageAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Elaris.Domain.Media.Responses.MediaAuthenticityResult(true));

    public Task<Elaris.Domain.Media.Responses.MediaAuthenticityResult> AssessVideoAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Elaris.Domain.Media.Responses.MediaAuthenticityResult(true));
}

file sealed class RejectAiMediaService : Elaris.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService
{
    public Task<Elaris.Domain.Media.Responses.MediaAuthenticityResult> AssessImageAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Elaris.Domain.Media.Responses.MediaAuthenticityResult(
            false,
            "AI-generated photos are not allowed.",
            "test"));

    public Task<Elaris.Domain.Media.Responses.MediaAuthenticityResult> AssessVideoAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Elaris.Domain.Media.Responses.MediaAuthenticityResult(
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
