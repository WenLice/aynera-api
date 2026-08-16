using Elaris.Domain.Auth.Enums;

namespace Elaris.Domain.Auth.Requests;

public sealed record CreateMemberRequest(
    string Phone,
    string FirstName,
    string LastName,
    Gender Gender,
    DateOnly DateOfBirth,
    string City,
    string Email,
    string? Religion = null,
    string? Password = null);
