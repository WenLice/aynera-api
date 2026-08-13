namespace Elaris.Domain.Auth.Records;

/// <summary>Application-facing member profile projection.</summary>
public sealed record MemberProfileRecord(
    Guid UserId,
    string FirstName,
    string LastName,
    string Gender,
    DateOnly DateOfBirth,
    string City,
    string? Religion);
