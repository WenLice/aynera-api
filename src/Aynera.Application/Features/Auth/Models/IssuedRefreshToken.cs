namespace Aynera.Application.Features.Auth.Models;

public sealed record IssuedRefreshToken(
    Guid SessionId,
    Guid FamilyId,
    string RefreshToken,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc);
