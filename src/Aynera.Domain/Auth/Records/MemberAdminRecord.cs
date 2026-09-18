namespace Aynera.Domain.Auth.Records;

/// <summary>Staff-facing member projection. Every profile field is null when the member has no profile row yet.</summary>
public sealed record MemberAdminRecord(
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
    Guid? CityId = null,
    string? Nickname = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? Work = null,
    bool? GenderIsPublic = null);
