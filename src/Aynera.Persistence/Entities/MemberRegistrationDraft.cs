namespace Aynera.Persistence.Entities;

/// <summary>
/// A member's registration answers while they are still being collected. One row per member, like
/// <see cref="MemberProfile"/> — but where a profile row means "complete and valid", a draft row
/// means "in progress", and that is the whole point of it existing separately.
/// <para>
/// Keeping the in-progress state here is what lets <see cref="MemberProfile"/> keep every column
/// NOT NULL. The alternative — relaxing those columns so a half-filled profile can be stored —
/// would weaken the permanent model for the sake of a state that lasts ten minutes, and would make
/// a name-only stub indistinguishable from a finished profile everywhere downstream.
/// </para>
/// <para>
/// The row is deleted the moment the answers are promoted, so no field ever has two homes.
/// </para>
/// </summary>
public sealed class MemberRegistrationDraft
{
    public Guid UserId { get; set; }

    /// <summary>
    /// The answers so far, as JSON (<c>jsonb</c>). Schemaless on purpose: the question set is still
    /// moving, and a new question should not cost a migration. The API contract over it stays typed
    /// — see <c>UpdateRegistrationRequest</c> — so "schemaless storage" never means "unvalidated".
    /// </summary>
    public string Data { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
