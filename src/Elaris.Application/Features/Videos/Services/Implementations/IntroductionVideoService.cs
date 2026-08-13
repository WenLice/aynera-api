using AutoMapper;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Application.Features.Media.Services.Interfaces;
using Elaris.Application.Features.Photos.Repositories;
using Elaris.Application.Features.Photos.Services.Interfaces;
using Elaris.Application.Features.Videos.Models;
using Elaris.Application.Features.Videos.Repositories;
using Elaris.Application.Features.Videos.Services.Interfaces;
using Elaris.Domain.Audit.Records;
using Elaris.Domain.Audit.Statics;
using Elaris.Domain.Photos.Enums;
using Elaris.Domain.Videos.Records;
using Elaris.Domain.Videos.Responses;
using Elaris.Domain.Videos.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Features.Videos.Services.Implementations;

public sealed class IntroductionVideoService : IIntroductionVideoService
{
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
        IOptions<IntroductionVideoOptions> options)
    {
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

        var contentType = (file.ContentType ?? string.Empty).Split(';', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            throw new VideoException(
                "video_unsupported_type",
                "Only MP4, WebM, and QuickTime videos are allowed.");
        }

        var reference = await _photos.FindReferenceAsync(userId, cancellationToken)
            ?? throw new VideoException(
                "video_reference_photo_required",
                "Upload a reference profile photo before your introduction video.");

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
                face.Detail ?? "Introduction video does not appear to match your reference photo.");
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
                UpdatedAtUtc: existing is null ? null : now),
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

        return _mapper.Map<IntroductionVideoDto>(record);
    }

    public async Task<IntroductionVideoDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var video = await _videos.FindByUserIdAsync(userId, cancellationToken);
        return video is null ? null : _mapper.Map<IntroductionVideoDto>(video);
    }

    public async Task<IntroductionVideoBytes> GetBytesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var video = await _videos.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new VideoException("video_not_found", "Introduction video not found.", statusCode: 404);

        return new IntroductionVideoBytes(video.UserId, video.ContentType, video.Data);
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
}
