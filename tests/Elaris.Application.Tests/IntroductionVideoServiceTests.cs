using Elaris.Application.Features.Photos.Repositories;
using Elaris.Application.Features.Photos.Services.Interfaces;
using Elaris.Application.Features.Videos.Models;
using Elaris.Application.Features.Videos.Repositories;
using Elaris.Application.Features.Videos.Services.Implementations;
using Elaris.Application.Features.Videos.Services.Interfaces;
using Elaris.Domain.Photos.Records;
using Elaris.Domain.Photos.Responses;
using Elaris.Domain.Photos.Enums;
using Elaris.Domain.Videos.Records;
using Elaris.Domain.Videos.Exceptions;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Tests;

public class IntroductionVideoServiceTests
{
    [Fact]
    public async Task Upload_WithoutReferencePhoto_Throws()
    {
        var service = CreateService(new InMemoryPhotoRepo(), new InMemoryVideoRepo());
        await using var stream = new MemoryStream([1, 2, 3, 4]);

        var ex = await Assert.ThrowsAsync<VideoException>(() =>
            service.UploadAsync(
                Guid.NewGuid(),
                new VideoUploadInput(stream, "intro.mp4", "video/mp4", 4),
                CancellationToken.None));

        Assert.Equal("video_reference_photo_required", ex.ErrorCode);
    }

    [Fact]
    public async Task Upload_BannedWords_Throws()
    {
        var userId = Guid.NewGuid();
        var photos = new InMemoryPhotoRepo();
        photos.AddReference(userId);
        var service = CreateService(
            photos,
            new InMemoryVideoRepo(),
            stubTranscript: "I will kill anyone who disagrees");

        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var ex = await Assert.ThrowsAsync<VideoException>(() =>
            service.UploadAsync(
                userId,
                new VideoUploadInput(stream, "intro.mp4", "video/mp4", 4),
                CancellationToken.None));

        Assert.Equal("video_guideline_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task Upload_CleanTranscript_SucceedsAndReplaces()
    {
        var userId = Guid.NewGuid();
        var photos = new InMemoryPhotoRepo();
        photos.AddReference(userId);
        var videos = new InMemoryVideoRepo();
        var service = CreateService(photos, videos, stubTranscript: "Hello, I am looking for a serious relationship.");

        await using var first = new MemoryStream([1, 2, 3, 4]);
        var created = await service.UploadAsync(
            userId,
            new VideoUploadInput(first, "intro.mp4", "video/mp4", 4),
            CancellationToken.None);

        Assert.True(created.GuidelinePassed);
        Assert.Equal(FaceMatchStatus.Matched.ToString(), created.FaceMatchStatus);

        await using var second = new MemoryStream([9, 9, 9, 9, 9]);
        var replaced = await service.UploadAsync(
            userId,
            new VideoUploadInput(second, "intro2.mp4", "video/mp4", 5),
            CancellationToken.None);

        Assert.Equal(5, replaced.ByteSize);
        Assert.Single(videos.All);
    }

    [Fact]
    public async Task Upload_AiGenerated_Throws()
    {
        var userId = Guid.NewGuid();
        var photos = new InMemoryPhotoRepo();
        photos.AddReference(userId);
        var service = CreateService(
            photos,
            new InMemoryVideoRepo(),
            authenticity: new RejectAiVideoMediaService());

        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var ex = await Assert.ThrowsAsync<VideoException>(() =>
            service.UploadAsync(
                userId,
                new VideoUploadInput(stream, "intro.mp4", "video/mp4", 4),
                CancellationToken.None));

        Assert.Equal("video_ai_generated", ex.ErrorCode);
    }

    private static IntroductionVideoService CreateService(
        IMemberPhotoRepository photos,
        IIntroductionVideoRepository videos,
        string stubTranscript = "",
        Elaris.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService? authenticity = null) =>
        new(
            videos,
            photos,
            new AlwaysMatchFaceService(),
            new FakeFrameExtractor(),
            new FixedTranscriptService(stubTranscript),
            new BannedWordsGuidelineService(Options.Create(new IntroductionVideoOptions
            {
                MaxBytes = 25 * 1024 * 1024,
                StubTranscript = stubTranscript,
                BannedWords = ["kill", "suicide", "terrorist", "nude", "porn", "rape", "molest"]
            })),
            authenticity ?? new AlwaysAuthenticVideoMediaService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<IntroductionVideoService>.Instance,
            Options.Create(new IntroductionVideoOptions
            {
                MaxBytes = 25 * 1024 * 1024,
                StubTranscript = stubTranscript,
                BannedWords = ["kill", "suicide", "terrorist", "nude", "porn", "rape", "molest"]
            }));
}

file sealed class AlwaysAuthenticVideoMediaService : Elaris.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService
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

file sealed class RejectAiVideoMediaService : Elaris.Application.Features.Media.Services.Interfaces.IMediaAuthenticityService
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
        Task.FromResult(new Elaris.Domain.Media.Responses.MediaAuthenticityResult(
            false,
            "AI-generated videos are not allowed.",
            "test"));
}

file sealed class AlwaysMatchFaceService : IFaceMatchService
{
    public Task<FaceMatchResult> CompareAsync(
        byte[] referenceImage,
        string referenceContentType,
        byte[] candidateImage,
        string candidateContentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new FaceMatchResult(FaceMatchStatus.Matched.ToString(), 99m));
}

file sealed class FakeFrameExtractor : IVideoFrameExtractor
{
    public Task<byte[]> ExtractFaceFrameJpegAsync(
        Stream video,
        string contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
}

file sealed class FixedTranscriptService(string transcript) : ISpeechTranscriptionService
{
    public Task<string> TranscribeAsync(
        Stream video,
        string contentType,
        CancellationToken cancellationToken) =>
        Task.FromResult(transcript);
}

file sealed class InMemoryPhotoRepo : IMemberPhotoRepository
{
    private readonly Dictionary<Guid, MemberPhotoRecord> _refs = new();

    public void AddReference(Guid userId) =>
        _refs[userId] = new MemberPhotoRecord(
            Guid.NewGuid(),
            userId,
            0,
            "image/jpeg",
            3,
            [1, 2, 3],
            true,
            FaceMatchStatus.Pending.ToString(),
            null,
            DateTimeOffset.UtcNow);

    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_refs.ContainsKey(userId) ? 1 : 0);

    public Task<IReadOnlyList<MemberPhotoRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemberPhotoRecord>>(
            _refs.TryGetValue(userId, out var p) ? [p] : []);

    public Task<MemberPhotoRecord?> FindByIdAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_refs.TryGetValue(userId, out var p) && p.Id == photoId ? p : null);

    public Task<MemberPhotoRecord?> FindReferenceAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_refs.TryGetValue(userId, out var p) ? p : null);

    public Task<int> NextSortOrderAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<MemberPhotoRecord> AddAsync(MemberPhotoRecord photo, CancellationToken cancellationToken)
    {
        _refs[photo.UserId] = photo;
        return Task.FromResult(photo);
    }

    public Task SoftDeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken)
    {
        _refs.Remove(userId);
        return Task.CompletedTask;
    }

    public Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        _refs.Remove(userId);
        return Task.CompletedTask;
    }

    public Task PromoteNextReferenceAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

file sealed class InMemoryVideoRepo : IIntroductionVideoRepository
{
    public List<IntroductionVideoRecord> All { get; } = [];

    public Task<IntroductionVideoRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(All.FirstOrDefault(x => x.UserId == userId));

    public Task<IntroductionVideoRecord> UpsertAsync(
        IntroductionVideoRecord video,
        CancellationToken cancellationToken)
    {
        All.RemoveAll(x => x.UserId == video.UserId);
        All.Add(video);
        return Task.FromResult(video);
    }

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        All.RemoveAll(x => x.UserId == userId);
        return Task.CompletedTask;
    }
}
