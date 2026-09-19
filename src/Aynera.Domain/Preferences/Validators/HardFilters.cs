using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Preferences.Enums;

namespace Aynera.Domain.Preferences.Validators;

/// <summary>One side of a candidate pair, as the hard filters need to see it.</summary>
/// <param name="Gender">Used whether or not the member shows it on their profile.</param>
public sealed record MatchCandidate(
    Guid UserId,
    Gender Gender,
    int Age,
    Guid CityId,
    InterestedIn InterestedIn,
    int MinAge,
    /// <summary>Null means an open upper end — the member will meet anyone at or above MinAge.</summary>
    int? MaxAge,
    bool AgeIsFlexible,
    RelationshipOutcome Outcome);

/// <summary>Why a pair was removed. One code per filter, so §6.2 analytics can tell them apart.</summary>
public static class HardFilterReasons
{
    public const string GenderNotReciprocal = "gender_not_reciprocal";
    public const string AgeNotReciprocal = "age_not_reciprocal";
    public const string DifferentCity = "different_city";
    public const string IntentNotCompatible = "intent_not_compatible";
    public const string SameMember = "same_member";
}

/// <param name="Reasons">Empty when the pair passes. Every failing filter is listed, not just the first.</param>
public sealed record HardFilterResult(bool Passes, IReadOnlyList<string> Reasons);

/// <summary>
/// MATCHMAKING-RULES §6.1, the four filters we hold data for. Pure and reciprocal (M-01):
/// evaluating (a, b) and (b, a) always agree, because every check is symmetric.
///
/// Cohort eligibility, block history, dealbreakers and relationship-state availability are also
/// §6.1 filters but have no model yet, so a pass here is "not excluded by what we know", not
/// "introducible".
/// </summary>
public static class HardFilters
{
    public static HardFilterResult Evaluate(MatchCandidate a, MatchCandidate b)
    {
        var reasons = new List<string>();

        if (a.UserId == b.UserId)
        {
            return new HardFilterResult(false, [HardFilterReasons.SameMember]);
        }

        // Each must be open to the other's gender. Hiding a gender from the profile does not
        // withdraw it from matching, so this reads the stored value either way.
        if (!PreferenceRules.Accepts(a.InterestedIn, b.Gender)
            || !PreferenceRules.Accepts(b.InterestedIn, a.Gender))
        {
            reasons.Add(HardFilterReasons.GenderNotReciprocal);
        }

        if (!AgeAccepts(a, b.Age) || !AgeAccepts(b, a.Age))
        {
            reasons.Add(HardFilterReasons.AgeNotReciprocal);
        }

        if (a.CityId != b.CityId)
        {
            reasons.Add(HardFilterReasons.DifferentCity);
        }

        // Exact outcome: Platonic meets only Platonic, Legacy only Legacy. Comparing outcomes
        // rather than tracks is deliberate — two Fluid members wanting different things are not
        // a match, so the stored track is never the thing filtered on.
        if (a.Outcome != b.Outcome)
        {
            reasons.Add(HardFilterReasons.IntentNotCompatible);
        }

        return new HardFilterResult(reasons.Count == 0, reasons);
    }

    /// <summary>
    /// The member's stated range, widened at both ends when they marked it flexible. A null
    /// <see cref="MatchCandidate.MaxAge"/> is an open upper end — "45 and older" — which exists so
    /// that members above the slider's ceiling are reachable at all; before it, anyone over
    /// <see cref="PreferenceRules.AgeMax"/> plus flexibility passed nobody's filter and was
    /// guaranteed zero introductions. Flexibility has nothing to widen on an open end.
    /// </summary>
    public static bool AgeAccepts(MatchCandidate side, int otherAge)
    {
        var slack = side.AgeIsFlexible ? PreferenceRules.FlexibleYears : 0;

        if (otherAge < side.MinAge - slack)
        {
            return false;
        }

        return side.MaxAge is null || otherAge <= side.MaxAge.Value + slack;
    }
}
