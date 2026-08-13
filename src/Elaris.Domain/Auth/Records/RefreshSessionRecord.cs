namespace Elaris.Domain.Auth.Records;

public sealed record RefreshSessionRecord(
    Guid Id,
    Guid UserId,
    string Audience,
    string TokenHash,
    Guid FamilyId,
    string? DeviceLabel,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? ReplacedAtUtc,
    Guid? ReplacedBySessionId);
