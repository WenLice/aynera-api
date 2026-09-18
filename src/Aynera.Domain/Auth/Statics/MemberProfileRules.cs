namespace Aynera.Domain.Auth.Statics;

/// <summary>Bounds shared by every writer of a member's basic details.</summary>
public static class MemberProfileRules
{
    /// <summary>Deliberately wider than the app's height picker so a valid pick is never rejected.</summary>
    public const int HeightMinCm = 120;

    public const int HeightMaxCm = 250;
}
