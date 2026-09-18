namespace Aynera.Domain.Preferences.Responses;

/// <summary>
/// A member's matching preferences. Never shown to anyone but the member and staff —
/// these are hard filters, not profile content.
/// </summary>
/// <param name="InterestedIn">`Male` | `Female` | `Other` | `Everyone`.</param>
/// <param name="AgeIsFlexible">When true the range is widened by two years at each end for matching.</param>
/// <param name="IntentOutcome">`Platonic` | `Spontaneous` | `Prospect` | `Legacy`.</param>
/// <param name="RelationshipTrack">`Fluid` | `Intent` — derived from the outcome, never stored separately.</param>
public sealed record MemberPreferencesDto(
    string InterestedIn,
    int MinAge,
    int MaxAge,
    bool AgeIsFlexible,
    string IntentOutcome,
    string RelationshipTrack);
