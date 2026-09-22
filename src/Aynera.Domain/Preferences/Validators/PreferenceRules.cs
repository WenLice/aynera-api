using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Preferences.Enums;

namespace Aynera.Domain.Preferences.Validators;

/// <summary>Bounds and mappings shared by every reader and writer of member preferences.</summary>
public static class PreferenceRules
{
    /// <summary>Ends of the age-range control. 18 is the platform's minimum age to join.</summary>
    public const int AgeMin = 18;

    public const int AgeMax = 45;

    /// <summary>What "flexible by a couple of years" widens the range by, at each end.</summary>
    public const int FlexibleYears = 2;

    /// <summary>The genders an <see cref="InterestedIn"/> choice stands for.</summary>
    public static IReadOnlyList<Gender> GendersFor(InterestedIn interestedIn) =>
        interestedIn switch
        {
            InterestedIn.Male => [Gender.Male],
            InterestedIn.Female => [Gender.Female],
            InterestedIn.ThirdGender => [Gender.ThirdGender],
            InterestedIn.Everyone => [Gender.Male, Gender.Female, Gender.ThirdGender],
            _ => []
        };

    public static bool Accepts(InterestedIn interestedIn, Gender gender) =>
        GendersFor(interestedIn).Contains(gender);

    /// <summary>
    /// The track an intention belongs to. Still the single source of truth even though the track
    /// is now stored: the write path validates the submitted track against this, so a stored pair
    /// can never disagree.
    /// </summary>
    public static RelationshipTrack TrackFor(RelationshipOutcome outcome) =>
        outcome is RelationshipOutcome.Platonic or RelationshipOutcome.Spontaneous
            ? RelationshipTrack.Fluid
            : RelationshipTrack.Intent;
}
