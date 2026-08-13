using AutoMapper;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Application.Features.Media.Services.Interfaces;
using Elaris.Application.Features.Photos.Models;
using Elaris.Application.Features.Photos.Repositories;
using Elaris.Application.Features.Photos.Services.Interfaces;
using Elaris.Domain.Audit.Records;
using Elaris.Domain.Audit.Statics;
using Elaris.Domain.Photos.Records;
using Elaris.Domain.Photos.Responses;
using Elaris.Domain.Photos.Enums;
using Elaris.Domain.Photos.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Features.Photos.Services.Implementations;

public sealed class PhotoService : IPhotoService
{
    private readonly IMemberPhotoRepository _photos;
    private readonly IImageProcessor _images;
    private readonly IFaceMatchService _faceMatch;
    private readonly IMediaAuthenticityService _authenticity;
    private readonly IMapper _mapper;
    private readonly IAuditWriter _audit;
    private readonly ILogger<PhotoService> _logger;
    private readonly PhotoOptions _options;

    public PhotoService(
        IMemberPhotoRepository photos,
        IImageProcessor images,
        IFaceMatchService faceMatch,
        IMediaAuthenticityService authenticity,
        IMapper mapper,
        IAuditWriter audit,
        ILogger<PhotoService> logger,
        IOptions<PhotoOptions> options)
    {
        _photos = photos;
        _images = images;
        _faceMatch = faceMatch;
        _authenticity = authenticity;
        _mapper = mapper;
        _audit = audit;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MemberPhotoDto>> UploadAsync(
        Guid userId,
        IReadOnlyList<PhotoUploadInput> files,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("UploadPhotos user {UserId} count {Count}", userId, files.Count);
        if (files.Count == 0)
        {
            throw new PhotoException("photo_required", "At least one photo is required.");
        }

        var existingCount = await _photos.CountByUserIdAsync(userId, cancellationToken);
        if (existingCount + files.Count > _options.MaxCount)
        {
            throw new PhotoException(
                "photo_limit_exceeded",
                $"You can upload at most {_options.MaxCount} photos.");
        }

        var reference = await _photos.FindReferenceAsync(userId, cancellationToken);
        var created = new List<MemberPhotoDto>(files.Count);
        var nextSort = await _photos.NextSortOrderAsync(userId, cancellationToken);

        foreach (var file in files)
        {
            if (file.Length <= 0)
            {
                throw new PhotoException("photo_empty", "Photo file is empty.");
            }

            if (file.Length > _options.MaxBytes)
            {
                throw new PhotoException(
                    "photo_too_large",
                    $"Each photo must be at most {_options.MaxBytes / (1024 * 1024)} MB.");
            }

            await using var original = new MemoryStream();
            await file.Content.CopyToAsync(original, cancellationToken);
            original.Position = 0;

            var authenticity = await _authenticity.AssessImageAsync(
                original,
                file.ContentType,
                cancellationToken);
            if (!authenticity.IsLikelyAuthentic)
            {
                throw new PhotoException(
                    "photo_ai_generated",
                    authenticity.Detail
                    ?? "AI-generated photos are not allowed. Please upload a real photo of yourself.");
            }

            original.Position = 0;
            var processed = await _images.ProcessAsync(original, file.ContentType, cancellationToken);

            var isReference = reference is null && created.Count == 0;
            string faceStatus;
            decimal? faceScore;

            if (isReference)
            {
                faceStatus = FaceMatchStatus.Pending.ToString();
                faceScore = null;
            }
            else
            {
                var refPhoto = reference
                    ?? throw new PhotoException(
                        "photo_reference_missing",
                        "A reference photo is required before uploading additional photos.",
                        statusCode: 400);

                var match = await _faceMatch.CompareAsync(
                    refPhoto.Data,
                    refPhoto.ContentType,
                    processed.Data,
                    processed.ContentType,
                    cancellationToken);

                faceStatus = match.Status;
                faceScore = match.Score;

                if (string.Equals(faceStatus, FaceMatchStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new PhotoException(
                        "photo_face_mismatch",
                        match.Detail ?? "Photo does not appear to match your reference photo.");
                }
            }

            var record = await _photos.AddAsync(
                new MemberPhotoRecord(
                    Id: Guid.NewGuid(),
                    UserId: userId,
                    SortOrder: nextSort++,
                    ContentType: processed.ContentType,
                    ByteSize: processed.ByteSize,
                    Data: processed.Data,
                    IsReference: isReference,
                    FaceMatchStatus: faceStatus,
                    FaceMatchScore: faceScore,
                    CreatedAtUtc: DateTimeOffset.UtcNow),
                cancellationToken);

            if (isReference)
            {
                reference = record;
            }

            created.Add(_mapper.Map<MemberPhotoDto>(record));
        }

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.PhotosUploaded,
                Outcome: AuditOutcomes.Success,
                Message: $"Uploaded {created.Count} photo(s).",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: userId.ToString("D"),
                Metadata: new
                {
                    count = created.Count,
                    photoIds = created.Select(p => p.Id).ToArray()
                }),
            cancellationToken);

        return created;
    }

    public async Task<IReadOnlyList<MemberPhotoDto>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var photos = await _photos.ListByUserIdAsync(userId, cancellationToken);
        return _mapper.Map<List<MemberPhotoDto>>(photos);
    }

    public async Task<MemberPhotoBytes> GetBytesAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var photo = await _photos.FindByIdAsync(userId, photoId, cancellationToken)
            ?? throw new PhotoException("photo_not_found", "Photo not found.", statusCode: 404);

        return new MemberPhotoBytes(photo.Id, photo.ContentType, photo.Data);
    }

    public async Task DeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await _photos.FindByIdAsync(userId, photoId, cancellationToken)
            ?? throw new PhotoException("photo_not_found", "Photo not found.", statusCode: 404);

        await _photos.SoftDeleteAsync(userId, photoId, cancellationToken);

        if (photo.IsReference)
        {
            await _photos.PromoteNextReferenceAsync(userId, cancellationToken);
        }

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.PhotoDeleted,
                Outcome: AuditOutcomes.Success,
                Message: "Photo deleted.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.MemberPhoto,
                SubjectId: photoId.ToString("D"),
                Metadata: new { photoId, wasReference = photo.IsReference }),
            cancellationToken);
    }
}
