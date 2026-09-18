namespace Aynera.Domain.Admissions.Requests;

/// <summary>Staff action on a member's admission. <paramref name="Decision"/> parses to <c>AdmissionDecision</c>.</summary>
public sealed record AdmissionDecisionRequest(
    string Decision,
    string? Reason,
    string? ReviewNote);
