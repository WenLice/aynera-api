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
            InterestedIn.Other => [Gender.Other],
            InterestedIn.Everyone => [Gender.Male, Gender.Female, Gender.Other],
            _ => []
        };

    public static bool Accepts(InterestedIn interestedIn, Gender gender) =>
        GendersFor(interestedIn).Contains(gender);

    public static RelationshipTrack TrackFor(IntentOutcome outcome) =>
        outcome is IntentOutcome.Platonic or IntentOutcome.Spontaneous
            ? RelationshipTrack.Fluid
            : RelationshipTrack.Intent;
}
