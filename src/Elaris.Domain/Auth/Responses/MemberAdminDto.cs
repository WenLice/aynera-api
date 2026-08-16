namespace Elaris.Domain.Auth.Responses;

public sealed record MemberAdminDto(
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
    string? Religion);
