namespace Aynera.Application.Features.Liveness.Models;

/// <summary>The rules for passing the face check (<c>Aynera:Liveness</c>).</summary>
public sealed class LivenessOptions
{
    public const string SectionName = "Aynera:Liveness";

    /// <summary>
    /// The provider's liveness confidence (0–100) at or above which the member counts as live. The
    /// provider sets no threshold; this is ours. Below it the check is recorded, not silently passed.
    /// </summary>
    public decimal MinConfidence { get; set; } = 80m;

    /// <summary>The page that runs the check, served by this API. The session id is appended.</summary>
    public string PageUrl { get; set; } = "http://localhost:5057/liveness/";
}
