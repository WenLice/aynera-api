namespace Aynera.Domain.Auth.Responses;

/// <summary><paramref name="CityId"/> is the shared city-catalog id (same key as venues); every member has exactly one.</summary>
public sealed record MemberProfileDto(
    string FirstName,
    string LastName,
    string Gender,
    DateOnly DateOfBirth,
    string City,
    string? Religion,
    Guid CityId);
