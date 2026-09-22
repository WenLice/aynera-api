using Aynera.Domain.Answers.Records;

namespace Aynera.Application.Features.Answers.Repositories;

/// <summary>
/// The member's everyday, belief and vibe answers. Absent means nothing has been answered yet,
/// which is a normal state rather than an error.
/// </summary>
public interface IMemberProfileAnswersRepository
{
    Task<MemberProfileAnswersRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Writes the whole answer sheet, creating the row on first save. The service merges; this stores.</summary>
    Task<MemberProfileAnswersRecord> UpsertAsync(
        MemberProfileAnswersRecord answers,
        CancellationToken cancellationToken);

    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
