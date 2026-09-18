namespace Aynera.Domain.Admissions.Enums;

/// <summary>
/// Policy documents a member must accept before entering the candidate pool. Each acceptance is
/// stored as its own versioned row, so bumping one document's version re-gates eligibility
/// without discarding the others. The required set was fixed with the founder on 2026-09-11.
/// </summary>
public enum ConsentPolicyKind
{
    Terms = 0,
    Privacy = 1,
    CommunityGuidelines = 2
}
