namespace Aynera.Domain.Liveness.Enums;

/// <summary>Where one face-liveness session ended up.</summary>
public enum LivenessOutcome
{
    /// <summary>Started; the member has not finished the check yet.</summary>
    Pending = 0,

    /// <summary>A live person, and the same face as the member's reference photo.</summary>
    Passed = 1,

    /// <summary>The provider was not confident this was a live person (a photo, a screen, a mask).</summary>
    NotLive = 2,

    /// <summary>Live, but not the face in the member's photos.</summary>
    FaceMismatch = 3,

    /// <summary>The session timed out before the member finished.</summary>
    Expired = 4,

    /// <summary>The provider could not complete the check (camera, lighting, network).</summary>
    Failed = 5,
}
