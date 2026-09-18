namespace Aynera.Domain.Auth.Records;

/// <summary>Application-facing member profile projection. <paramref name="CityId"/> is the shared city-catalog key (required).</summary>
public sealed record MemberProfileRecord(
    Guid UserId,
    string FirstName,
    string LastName,
    string Gender,
    DateOnly DateOfBirth,
    string City,
    string? Religion,
    Guid CityId);
