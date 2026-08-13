namespace Elaris.Domain.Auth.Responses;

public sealed record AuthAccountDto(
    Guid Id,
    string? Phone,
    bool PhoneConfirmed,
    string? Email,
    bool EmailConfirmed,
    string AccountKind,
    bool IsActive,
    bool IsDeleted,
    IReadOnlyList<string> Roles,
    MemberProfileDto? Profile);
