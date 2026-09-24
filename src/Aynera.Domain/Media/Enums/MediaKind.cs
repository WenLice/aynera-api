namespace Aynera.Domain.Media.Enums;

/// <summary>What a stored media file is. One table holds all of them.</summary>
public enum MediaKind
{
    /// <summary>A profile photo in one of the numbered slots.</summary>
    Photo = 0,

    /// <summary>The member's introduction video. One per member.</summary>
    IntroVideo = 1,

    /// <summary>The reference selfie from the private liveness check. One per member; staff-only.</summary>
    Liveness = 2,

    /// <summary>A spoken answer to one conversation prompt. One per prompt the member holds.</summary>
    VoiceAnswer = 3,
}
