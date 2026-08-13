namespace Elaris.Domain.EarlyAccess.Requests;

public sealed record JoinEarlyAccessRequest(
    string FullName,
    string Email,
    string City,
    string Interest,
    bool IsAdult,
    bool MarketingConsent,
    string Phone);
