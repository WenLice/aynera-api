using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

/// <summary>
/// A member's answers to the everyday, belief and vibe questions. One row per member, like
/// <see cref="MemberProfile"/>.
/// <para>
/// One <c>jsonb</c> column per category rather than one document. The categories are a
/// product-level split that changes rarely, while the questions inside them change freely — and a
/// column costs nothing to add a question to, so the migration a new category would need is a price
/// paid almost never.
/// </para>
/// <para>
/// What it buys: a narrower GIN index per category when filtering arrives, shorter expression
/// paths, and a key collision that cannot happen — <c>movement</c> is both an everyday question and
/// a vibe group, and separate columns keep them apart by construction.
/// </para>
/// </summary>
public sealed class MemberProfileAnswers : ISoftDeletable
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Everyday answers, keyed by question: <c>{"drink":{"option":"sometimes","public":true}}</c>.
    /// Each carries its own visibility, because answering is not the same as publishing.
    /// </summary>
    public string Lifestyle { get; set; } = "{}";

    /// <summary>The quieter questions — faith, family, children — in the same shape.</summary>
    public string Beliefs { get; set; } = "{}";

    /// <summary>
    /// The vibe chips a member picked, as a flat set of keys: <c>["reading","night-owl"]</c>.
    /// Flat rather than grouped because the app holds them that way and, with the question list
    /// living in the app, the server has no way to know which group a chip belongs to.
    /// </summary>
    public string Vibe { get; set; } = "[]";

    /// <summary>
    /// The chosen conversation prompts in order, with any typed answer:
    /// <c>[{"promptId":"know","text":"…"}]</c>. A spoken answer lives in <c>MemberMedia</c>.
    /// </summary>
    public string Prompts { get; set; } = "[]";

    /// <summary>"Something I don't usually say first" — free text from the profile editor.</summary>
    public string? Dealbreaker { get; set; }

    /// <summary>The rhythm answers, keyed by question: <c>{"socialEnergy":"…","weekends":"…"}</c>.</summary>
    public string Rhythm { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
