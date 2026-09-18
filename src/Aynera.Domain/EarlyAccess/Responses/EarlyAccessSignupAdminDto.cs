namespace Aynera.Domain.EarlyAccess.Responses;

public sealed record EarlyAccessSignupAdminDto(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string City,
    string Interest,
    bool IsAdult,
    bool MarketingConsent,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? Intent,
    string? MeetPreference);
