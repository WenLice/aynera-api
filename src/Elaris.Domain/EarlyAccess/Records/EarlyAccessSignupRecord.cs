namespace Elaris.Domain.EarlyAccess.Records;

public sealed record EarlyAccessSignupRecord(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string City,
    string Interest,
    bool IsAdult,
    bool MarketingConsent,
    string? ClientIp,
    string? UserAgent,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
