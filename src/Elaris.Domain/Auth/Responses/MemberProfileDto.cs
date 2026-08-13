namespace Elaris.Domain.Auth.Responses;

public sealed record MemberProfileDto(
    string FirstName,
    string LastName,
    string Gender,
    DateOnly DateOfBirth,
    string City,
    string? Religion);
