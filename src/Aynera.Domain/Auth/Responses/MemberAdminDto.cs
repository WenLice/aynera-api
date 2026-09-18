namespace Aynera.Domain.Auth.Responses;

/// <summary>Staff list row. Profile fields are null when the member has no profile row yet.</summary>
public sealed record MemberAdminDto(
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
    string? Nickname = null);
