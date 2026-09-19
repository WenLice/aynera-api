namespace Aynera.Domain.Preferences.Responses;

/// <summary>
/// A member's matching preferences. Never shown to anyone but the member and staff —
/// these are hard filters, not profile content.
/// </summary>
/// <param name="InterestedIn">`Male` | `Female` | `Other` | `Everyone`.</param>
/// <param name="MaxAge">Null is an open upper end — the member will meet anyone at or above `MinAge`.</param>
/// <param name="AgeIsFlexible">When true the range is widened by two years at each end for matching. An open upper end has nothing to widen.</param>
/// <param name="Track">`Fluid` | `Intent` — the track that owns <paramref name="Outcome"/>.</param>
/// <param name="Outcome">`Platonic` | `Spontaneous` (Fluid) | `Prospect` | `Legacy` (Intent).</param>
public sealed record MemberPreferencesDto(
    string InterestedIn,
    int MinAge,
    int? MaxAge,
    bool AgeIsFlexible,
    string Track,
    string Outcome);
