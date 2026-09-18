namespace Aynera.Domain.Admissions.Responses;

public sealed record MemberConsentDto(
    Guid Id,
    string PolicyKind,
    string Version,
    DateTimeOffset AcceptedAtUtc);
