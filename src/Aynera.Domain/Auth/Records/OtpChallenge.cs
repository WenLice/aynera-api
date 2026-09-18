namespace Aynera.Domain.Auth.Records;

public sealed record OtpChallenge(
    string Channel,
    string Destination,
    string CodeHash,
    int Attempts,
    DateTimeOffset ExpiresAtUtc,
    string Purpose,
    string Audience);
