namespace Aynera.Domain.Admissions.Enums;

/// <summary>Staff action applied to a member's admission.</summary>
public enum AdmissionDecision
{
    /// <summary>Claim a submitted admission for review.</summary>
    StartReview = 0,
    Approve = 1,
    Reject = 2,
    /// <summary>Return an approved or rejected admission to review without waiting for the member.</summary>
    Reopen = 3
}
