namespace Aynera.Application.Features.Auth.Models;

public sealed record AccessTokenResult(
    string AccessToken,
    string JwtId,
    DateTimeOffset ExpiresAtUtc);
