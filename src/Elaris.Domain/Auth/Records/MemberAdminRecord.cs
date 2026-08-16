namespace Elaris.Domain.Auth.Records;

public sealed record MemberAdminRecord(
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
