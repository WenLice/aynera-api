namespace Aynera.Domain.Preferences.Enums;

/// <summary>
/// Who a member wants to meet. Mirrors <see cref="Auth.Enums.Gender"/> so the reciprocal
/// filter is set membership, plus a catch-all. Stored by name, so adding a value needs no
/// migration — and <see cref="Everyone"/> is expanded at evaluation time rather than frozen
/// into each row, so widening what "everyone" covers carries existing members with it.
/// </summary>
public enum InterestedIn
{
    Male = 0,
    Female = 1,

    /// <summary>Third gender / transgender.</summary>
    Other = 2,

    /// <summary>Every gender above.</summary>
    Everyone = 3
}

/// <summary>The two tracks on aynera.com/track. Matches the early-access API's own wording.</summary>
public enum RelationshipTrack
{
    Fluid = 0,
    Intent = 1
}

/// <summary>The two children of each track.</summary>
public enum IntentOutcome
{
    /// <summary>Fluid — friendship and networking, clearly labelled.</summary>
    Platonic = 0,

    /// <summary>Fluid — meet in the moment, part with grace.</summary>
    Spontaneous = 1,

    /// <summary>Intent — dating with the door to more left open.</summary>
    Prospect = 2,

    /// <summary>Intent — partnership meant to outlast the season.</summary>
    Legacy = 3
}
