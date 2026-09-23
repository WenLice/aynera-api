using Aynera.Application.Features.Media.Storage;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Domain.Media.Statics;
using AutoMapper;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Media.Services.Interfaces;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Application.Features.Videos.Models;
using Aynera.Application.Features.Videos.Repositories;
using Aynera.Application.Features.Videos.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Photos.Enums;
using Aynera.Domain.Videos.Records;
using Aynera.Domain.Videos.Responses;
using Aynera.Domain.Videos.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Videos.Services.Implementations;

public sealed class IntroductionVideoService : IIntroductionVideoService
{
    /// <summary>How long a signed playback link stays valid.</summary>
    private static readonly TimeSpan ReadUrlLifetime = TimeSpan.FromHours(1);

    private readonly IMediaStorage _storage;
    private readonly IVerifiedFaceProvider _verifiedFace;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4",
        "video/webm",
        "video/quicktime"
    };

    private readonly IIntroductionVideoRepository _videos;
    private readonly IMemberPhotoRepository _photos;
    private readonly IFaceMatchService _faceMatch;
    private readonly IVideoFrameExtractor _frames;
    private readonly ISpeechTranscriptionService _transcription;
    private readonly ISpeechGuidelineService _guidelines;
    private readonly IMediaAuthenticityService _authenticity;
    private readonly IMapper _mapper;
    private readonly IAuditWriter _audit;
    private readonly ILogger<IntroductionVideoService> _logger;
    private readonly IntroductionVideoOptions _options;

    public IntroductionVideoService(
        IIntroductionVideoRepository videos,
        IMemberPhotoRepository photos,
        IFaceMatchService faceMatch,
        IVideoFrameExtractor frames,
        ISpeechTranscriptionService transcription,
        ISpeechGuidelineService guidelines,
        IMediaAuthenticityService authenticity,
        IMapper mapper,
        IAuditWriter audit,
        ILogger<IntroductionVideoService> logger,
        IOptions<IntroductionVideoOptions> options,
        IMediaStorage storage,
        IVerifiedFaceProvider verifiedFace)
    {
        _storage = storage;
        _verifiedFace = verifiedFace;
        _videos = videos;
        _photos = photos;
        _faceMatch = faceMatch;
        _frames = frames;
        _transcription = transcription;
        _guidelines = guidelines;
        _authenticity = authenticity;
        _mapper = mapper;
        _audit = audit;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IntroductionVideoDto> UploadAsync(
        Guid userId,
        VideoUploadInput file,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("UploadIntroductionVideo user {UserId} bytes {Length}", userId, file.Length);
        if (file.Length <= 0)
        {
            throw new VideoException("video_empty", "Video file is empty.");
        }

        if (file.Length > _options.MaxBytes)
        {
            throw new VideoException(
                "video_too_large",
                $"Introduction video must be at most {_options.MaxBytes / (1024 * 1024)} MB.");
        }

        if (file.Caption is { Length: > MemberMediaRules.CaptionMaxLength })
        {
            throw new VideoException(
                "video_caption_too_long",
                $"A caption can be at most {MemberMediaRules.CaptionMaxLength} characters.");
        }

        var contentType = (file.ContentType ?? string.Empty).Split(';', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            throw new VideoException(
                "video_unsupported_type",
                "Only MP4, WebM, and QuickTime videos are allowed.");
        }

        // Matched against the face the member proved live, like the photos.
        var reference = await _verifiedFace.GetAsync(userId, cancellationToken)
            ?? throw new VideoException(
                "video_face_check_required",
                "Complete the face check first — your video is matched to it.");

        await using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        buffer.Position = 0;
        var authenticity = await _authenticity.AssessVideoAsync(buffer, contentType, cancellationToken);
        if (!authenticity.IsLikelyAuthentic)
        {
            throw new VideoException(
                "video_ai_generated",
                authenticity.Detail
                ?? "AI-generated videos are not allowed. Please upload a real recording of yourself.");
        }

        buffer.Position = 0;
        var frameJpeg = await _frames.ExtractFaceFrameJpegAsync(buffer, contentType, cancellationToken);
        var face = await _faceMatch.CompareAsync(
            reference.Data,
            reference.ContentType,
            frameJpeg,
            "image/jpeg",
            cancellationToken);

        if (string.Equals(face.Status, FaceMatchStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new VideoException(
                "video_face_mismatch",
                face.Detail ?? "Introduction video does not appear to match your verified face.");
        }

        buffer.Position = 0;
        var transcript = await _transcription.TranscribeAsync(buffer, contentType, cancellationToken);
        var guideline = _guidelines.Evaluate(transcript);
        if (!guideline.Passed)
        {
            throw new VideoException(
                "video_guideline_failed",
                guideline.Detail ?? "Introduction video does not meet community guidelines.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _videos.FindByUserIdAsync(userId, cancellationToken);
        var record = await _videos.UpsertAsync(
            new IntroductionVideoRecord(
                UserId: userId,
                ContentType: contentType,
                ByteSize: bytes.Length,
                Data: bytes,
                FaceMatchStatus: face.Status,
                FaceMatchScore: face.Score,
                GuidelinePassed: true,
                GuidelineDetail: guideline.Detail,
                Transcript: string.IsNullOrWhiteSpace(transcript) ? null : transcript.Trim(),
                CreatedAtUtc: existing?.CreatedAtUtc ?? now,
                UpdatedAtUtc: existing is null ? null : now,
                Caption: NormalizeCaption(file.Caption)),
            cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.IntroVideoUploaded,
                Outcome: AuditOutcomes.Success,
                Message: "Introduction video uploaded.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.IntroductionVideo,
                SubjectId: userId.ToString("D"),
                Metadata: new
                {
                    contentType = record.ContentType,
                    byteSize = record.ByteSize,
                    faceMatchStatus = record.FaceMatchStatus
                }),
            cancellationToken);

        return ToDto(record);
    }

    public async Task<IntroductionVideoDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var video = await _videos.FindByUserIdAsync(userId, cancellationToken);
        return video is null ? null : ToDto(video);
    }

    public async Task<IntroductionVideoBytes> GetBytesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var video = await _videos.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new VideoException("video_not_found", "Introduction video not found.", statusCode: 404);

        // A row whose file is missing from the bucket is reported as not found rather than served
        // as an empty video the player would fail on silently.
        var data = await _videos.ReadContentAsync(userId, cancellationToken);
        if (data is null || data.Length == 0)
        {
            throw new VideoException("video_not_found", "Introduction video not found.", statusCode: 404);
        }

        return new IntroductionVideoBytes(video.UserId, video.ContentType, data);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        _ = await _videos.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new VideoException("video_not_found", "Introduction video not found.", statusCode: 404);

        await _videos.SoftDeleteByUserIdAsync(userId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.IntroVideoDeleted,
                Outcome: AuditOutcomes.Success,
                Message: "Introduction video deleted.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.IntroductionVideo,
                SubjectId: userId.ToString("D")),
            cancellationToken);
    }

    public async Task UpdateCaptionAsync(Guid userId, string? caption, CancellationToken cancellationToken)
    {
        if (caption is { Length: > MemberMediaRules.CaptionMaxLength })
        {
            throw new VideoException(
                "video_caption_too_long",
                $"A caption can be at most {MemberMediaRules.CaptionMaxLength} characters.");
        }

        await _videos.UpdateCaptionAsync(userId, NormalizeCaption(caption), cancellationToken);
    }

    private IntroductionVideoDto ToDto(IntroductionVideoRecord record) =>
        _mapper.Map<IntroductionVideoDto>(record) with
        {
            Url = string.IsNullOrEmpty(record.StorageKey)
                ? null
                : _storage.GetReadUrl(record.StorageKey, ReadUrlLifetime),
        };

    private static string? NormalizeCaption(string? caption) =>
        string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
}
