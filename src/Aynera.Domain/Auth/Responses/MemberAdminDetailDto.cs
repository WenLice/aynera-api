using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Videos.Responses;

namespace Aynera.Domain.Auth.Responses;

/// <summary>Staff member detail, including every basic-detail field. Profile fields are null when there is no profile row yet.</summary>
public sealed record MemberAdminDetailDto(
    Guid Id,
    string? Phone,
    bool PhoneConfirmed,
    string? Email,
    bool EmailConfirmed,
    bool IsActive,
    bool IsRestricted,
    DateTimeOffset CreatedAtUtc,
    string? Name,
    string? Gender,
    DateOnly? DateOfBirth,
    string? City,
    string? Religion,
    IReadOnlyList<MemberPhotoDto> Photos,
    IntroductionVideoDto? IntroductionVideo,
    Guid? CityId = null,
    string? Nickname = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? Work = null);
