using Aynera.Domain.Preferences.Enums;

namespace Aynera.Domain.Preferences.Requests;

/// <summary>
/// The signed-in member's matching preferences. A full replace, like the profile write.
/// </summary>
/// <param name="Track">
/// The track the member picked first, `Fluid` or `Intent`. Sent as well as stored so the app's
/// two-stage choice is recorded as made; the validator refuses a track that does not own
/// <paramref name="Outcome"/>, so the stored pair can never contradict itself.
/// </param>
/// <param name="Outcome">The child of that track: `Platonic` | `Spontaneous` | `Prospect` | `Legacy`.</param>
/// <param name="MaxAge">
/// Omit or send null for an open upper end — "<c>MinAge</c> and older". The app sends null when the
/// slider sits at its ceiling, which is what makes members above that ceiling reachable at all.
/// </param>
public sealed record UpdateMemberPreferencesRequest(
    InterestedIn InterestedIn,
    int MinAge,
    int? MaxAge,
    RelationshipTrack Track,
    RelationshipOutcome Outcome,
    bool AgeIsFlexible = false);
