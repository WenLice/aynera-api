namespace Elaris.Domain.Auth.Records;

public sealed record OtpChallenge(
    string PhoneE164,
    string CodeHash,
    int Attempts,
    DateTimeOffset ExpiresAtUtc,
    string Purpose,
    string Audience);
