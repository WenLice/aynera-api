namespace Elaris.Domain.Auth.Records;

/// <summary>Application-facing user projection (not the Identity/EF entity).</summary>
public sealed record UserRecord(
    Guid Id,
    string? Phone,
    bool PhoneConfirmed,
    string? Email,
    bool EmailConfirmed,
    string AccountKind,
    bool IsActive,
    bool IsDeleted,
    IReadOnlyList<string> Roles);
