namespace Elaris.Domain.EarlyAccess.Responses;

public sealed record EarlyAccessSignupDto(
    Guid Id,
    string Email,
    string City,
    string Interest,
    bool Created);
