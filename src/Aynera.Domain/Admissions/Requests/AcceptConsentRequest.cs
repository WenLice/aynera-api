namespace Aynera.Domain.Admissions.Requests;

/// <summary>Member acceptance of one policy document. <paramref name="PolicyKind"/> parses to <c>ConsentPolicyKind</c>.</summary>
public sealed record AcceptConsentRequest(
    string PolicyKind,
    string Version);
