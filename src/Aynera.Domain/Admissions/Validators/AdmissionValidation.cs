using Aynera.Domain.Admissions.Enums;

namespace Aynera.Domain.Admissions.Validators;

public static class AdmissionValidation
{
    /// <summary>MATCHMAKING-RULES section 4 age eligibility. Confirmed with the founder on 2026-09-10.</summary>
    public const int MinimumAgeYears = 18;

    public static bool TryParseState(string? value, out AdmissionState state) =>
        Enum.TryParse(value, ignoreCase: true, out state) && Enum.IsDefined(state);

    public static bool TryParseDecision(string? value, out AdmissionDecision decision) =>
        Enum.TryParse(value, ignoreCase: true, out decision) && Enum.IsDefined(decision);

    public static bool TryParseConsentKind(string? value, out ConsentPolicyKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind);

    public static bool BeKnownDecision(string? value) => TryParseDecision(value, out _);

    public static bool BeKnownConsentKind(string? value) => TryParseConsentKind(value, out _);

    /// <summary>
    /// The member-side transition: a member may submit a fresh draft or resubmit after a rejection
    /// (decided with the founder on 2026-09-11). Submitted, InReview and Approved are not resubmittable.
    /// </summary>
    public static bool CanSubmit(AdmissionState from) =>
        from is AdmissionState.Draft or AdmissionState.Rejected;

    /// <summary>
    /// The complete staff transition table. Any edge absent here is rejected, so a state can
    /// never be reached by a path review has not accounted for. Member submission is
    /// <see cref="CanSubmit"/>, not a staff decision.
    /// </summary>
    public static bool TryTransition(AdmissionState from, AdmissionDecision decision, out AdmissionState to)
    {
        to = from;

        switch (decision)
        {
            case AdmissionDecision.StartReview when from == AdmissionState.Submitted:
                to = AdmissionState.InReview;
                return true;

            // Staff may approve or reject straight from Submitted without first claiming the review.
            case AdmissionDecision.Approve when from is AdmissionState.Submitted or AdmissionState.InReview:
                to = AdmissionState.Approved;
                return true;

            case AdmissionDecision.Reject when from is AdmissionState.Submitted or AdmissionState.InReview:
                to = AdmissionState.Rejected;
                return true;

            // Staff may pull an approval or rejection back into review; members reach the same
            // place from Rejected by resubmitting (see CanSubmit).
            case AdmissionDecision.Reopen when from is AdmissionState.Approved or AdmissionState.Rejected:
                to = AdmissionState.InReview;
                return true;

            default:
                return false;
        }
    }

    /// <summary>Completed years of age on <paramref name="onDate"/>, not rounded.</summary>
    public static int AgeInYearsOn(DateOnly dateOfBirth, DateOnly onDate)
    {
        var age = onDate.Year - dateOfBirth.Year;
        if (onDate < dateOfBirth.AddYears(age))
        {
            age--;
        }

        return age;
    }

    public static bool IsAtLeastMinimumAge(DateOnly dateOfBirth, DateOnly onDate) =>
        AgeInYearsOn(dateOfBirth, onDate) >= MinimumAgeYears;
}

/// <summary>
/// Stable codes describing why a member is not match-eligible. Returned to staff surfaces and to
/// the member's own admission view; safe to display and to assert on in tests.
/// </summary>
public static class EligibilityReasons
{
    public const string AdmissionNotApproved = "admission_not_approved";
    public const string AccountNotFound = "account_not_found";
    public const string NotMember = "not_member";
    public const string AccountDeleted = "account_deleted";
    public const string AccountInactive = "account_inactive";
    public const string AccountRestricted = "account_restricted";
    public const string PhoneUnverified = "phone_unverified";
    public const string EmailUnverified = "email_unverified";
    public const string ProfileMissing = "profile_missing";
    public const string PreferencesMissing = "preferences_missing";
    public const string Underage = "underage";
    public const string IdentityRejected = "identity_rejected";

    /// <summary>e.g. <c>consent_missing:Privacy</c> — one per required document not accepted at the current version.</summary>
    public static string ConsentMissing(string policyKind) => $"consent_missing:{policyKind}";
}
