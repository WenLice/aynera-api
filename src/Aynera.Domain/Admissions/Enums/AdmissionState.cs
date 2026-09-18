namespace Aynera.Domain.Admissions.Enums;

/// <summary>
/// Member admission review state. Approval is a recorded staff decision; it is never inferred
/// from evidence being present. See <c>.ai/weekly-surprise-flow.md</c>.
/// </summary>
public enum AdmissionState
{
    Draft = 0,
    Submitted = 1,
    InReview = 2,
    Approved = 3,
    Rejected = 4
}
