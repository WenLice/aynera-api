using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Videos.Responses;

namespace Aynera.Domain.Auth.Responses;

public sealed record MemberAdminDetailDto(
    Guid Id,
    string? Phone,
    bool PhoneConfirmed,
    string? Email,
    bool EmailConfirmed,
    bool IsActive,
    bool IsRestricted,
    DateTimeOffset CreatedAtUtc,
    string? FirstName,
    string? LastName,
    string? Gender,
    DateOnly? DateOfBirth,
    string? City,
    string? Religion,
    IReadOnlyList<MemberPhotoDto> Photos,
    IntroductionVideoDto? IntroductionVideo,
    Guid? CityId = null);
