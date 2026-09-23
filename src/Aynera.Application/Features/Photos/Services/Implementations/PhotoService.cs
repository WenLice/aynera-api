using AutoMapper;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Media.Services.Interfaces;
using Aynera.Application.Features.Media.Storage;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Media.Statics;
using Aynera.Domain.Photos.Records;
using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Photos.Enums;
using Aynera.Domain.Photos.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Photos.Services.Implementations;

public sealed class PhotoService : IPhotoService
{
    /// <summary>How long a signed image link stays valid. Long enough to render a screen, short enough not to share.</summary>
    private static readonly TimeSpan ReadUrlLifetime = TimeSpan.FromHours(1);

    private readonly IMediaStorage _storage;
    private readonly IVerifiedFaceProvider _verifiedFace;
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
        IOptions<PhotoOptions> options,
        IMediaStorage storage,
        IVerifiedFaceProvider verifiedFace)
    {
        _storage = storage;
        _verifiedFace = verifiedFace;
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

        foreach (var file in files)
        {
            if (file.Slot is int slot && (slot < 1 || slot > _options.MaxCount))
            {
                throw new PhotoException(
                    "photo_slot_invalid",
                    $"Photo slots run from 1 to {_options.MaxCount}.");
            }

            if (file.Caption is { Length: > MemberMediaRules.CaptionMaxLength })
            {
                throw new PhotoException(
                    "photo_caption_too_long",
                    $"A caption can be at most {MemberMediaRules.CaptionMaxLength} characters.");
            }
        }

        // A file aimed at a filled slot replaces that photo, so it does not count towards the limit.
        var existing = await _photos.ListByUserIdAsync(userId, cancellationToken);
        var adding = files.Count(f => f.Slot is not int slot || existing.All(p => p.SortOrder != slot));
        if (existing.Count + adding > _options.MaxCount)
        {
            throw new PhotoException(
                "photo_limit_exceeded",
                $"You can upload at most {_options.MaxCount} photos.");
        }

        // Every photo is matched against the face the member proved live, not against another
        // upload: an uploaded photo could be anyone's, the face-check frame cannot.
        var verified = await _verifiedFace.GetAsync(userId, cancellationToken)
            ?? throw new PhotoException(
                "photo_face_check_required",
                "Complete the face check first — your photos are matched to it.");

        var reference = await _photos.FindReferenceAsync(userId, cancellationToken);
        var created = new List<MemberPhotoDto>(files.Count);
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

            var replaced = file.Slot is int target ? existing.FirstOrDefault(p => p.SortOrder == target) : null;

            // The first photo is the one shown first; it no longer anchors identity — the verified
            // face does.
            var isReference = (reference is null && created.Count == 0) || replaced?.IsReference == true;

            // Known before matching, because which slot it is decides how strict the match is.
            var slot = file.Slot ?? await _photos.NextSortOrderAsync(userId, cancellationToken);
            var match = await _faceMatch.CompareAsync(
                verified.Data,
                verified.ContentType,
                processed.Data,
                processed.ContentType,
                cancellationToken);
            var matched = string.Equals(match.Status, FaceMatchStatus.Matched.ToString(), StringComparison.OrdinalIgnoreCase);

            string faceStatus;
            if (_options.MustMatchSlots.Contains(slot))
            {
                if (!matched)
                {
                    var noFace = string.Equals(match.Status, FaceMatchStatus.Skipped.ToString(), StringComparison.OrdinalIgnoreCase);
                    throw noFace
                        ? new PhotoException("photo_face_required", "This photo needs to clearly show your face.")
                        : new PhotoException("photo_face_mismatch", "This doesn't look like you — use a photo of yourself.");
                }

                faceStatus = FaceMatchStatus.Matched.ToString();
            }
            else
            {
                // "Your world", "something you love": a place, a plate, a friend. Never blocked; a
                // stranger's face is recorded as Skipped with its score so it reads as "not the member"
                // to a curator without counting as an identity rejection in eligibility.
                faceStatus = matched ? FaceMatchStatus.Matched.ToString() : FaceMatchStatus.Skipped.ToString();
            }

            var faceScore = match.Score;

            if (replaced is not null)
            {
                // Only after the new photo passed every check. The slot is unique among live photos,
                // so the old one steps aside first; the new file then overwrites it in the bucket.
                await _photos.SoftDeleteAsync(userId, replaced.Id, cancellationToken);
            }

            var record = await _photos.AddAsync(
                new MemberPhotoRecord(
                    Id: Guid.NewGuid(),
                    UserId: userId,
                    // Asked per file, not counted up: the slot is the lowest free one, so a
                    // gap left by a deleted photo is filled before the next number is used.
                    SortOrder: slot,
                    ContentType: processed.ContentType,
                    ByteSize: processed.ByteSize,
                    Data: processed.Data,
                    IsReference: isReference,
                    FaceMatchStatus: faceStatus,
                    FaceMatchScore: faceScore,
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    Caption: NormalizeCaption(file.Caption)),
                cancellationToken);

            if (isReference)
            {
                reference = record;
            }

            created.Add(ToDto(record));
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
        return photos.Select(ToDto).ToList();
    }

    public async Task<MemberPhotoBytes> GetBytesAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var photo = await _photos.FindByIdAsync(userId, photoId, cancellationToken)
            ?? throw new PhotoException("photo_not_found", "Photo not found.", statusCode: 404);

        // The row can outlive its file (a bucket cleared by hand); an empty image is worse than a 404.
        if (photo.Data.Length == 0)
        {
            throw new PhotoException("photo_not_found", "Photo not found.", statusCode: 404);
        }

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

    public async Task UpdateCaptionAsync(
        Guid userId,
        Guid photoId,
        string? caption,
        CancellationToken cancellationToken)
    {
        if (caption is { Length: > MemberMediaRules.CaptionMaxLength })
        {
            throw new PhotoException(
                "photo_caption_too_long",
                $"A caption can be at most {MemberMediaRules.CaptionMaxLength} characters.");
        }

        await _photos.UpdateCaptionAsync(userId, photoId, NormalizeCaption(caption), cancellationToken);
    }

    private MemberPhotoDto ToDto(MemberPhotoRecord record) =>
        _mapper.Map<MemberPhotoDto>(record) with
        {
            Url = string.IsNullOrEmpty(record.StorageKey)
                ? null
                : _storage.GetReadUrl(record.StorageKey, ReadUrlLifetime),
        };

    /// <summary>Blank means no caption; stored as null so "never written" and "cleared" read the same.</summary>
    private static string? NormalizeCaption(string? caption) =>
        string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
}
