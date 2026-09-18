using Aynera.Domain.Preferences.Enums;

namespace Aynera.Domain.Preferences.Requests;

/// <summary>
/// The signed-in member's matching preferences. A full replace, like the profile write.
/// The track is not sent — it is implied by the outcome, and storing both would let them
/// disagree.
/// </summary>
public sealed record UpdateMemberPreferencesRequest(
    InterestedIn InterestedIn,
    int MinAge,
    int MaxAge,
    IntentOutcome IntentOutcome,
    bool AgeIsFlexible = false);
