using Aynera.Domain.Admissions.Enums;

namespace Aynera.Domain.Admissions.Records;

/// <summary>One accepted policy document at one version.</summary>
public sealed record MemberConsentRecord(
    Guid Id,
    Guid UserId,
    ConsentPolicyKind PolicyKind,
    string Version,
    DateTimeOffset AcceptedAtUtc);
