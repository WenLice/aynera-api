using Aynera.Domain.Registration.Records;

namespace Aynera.Application.Features.Registration.Repositories;

/// <summary>
/// The in-progress registration answers. Absent means nothing has been answered yet, which is a
/// normal state, not an error — so reads return <see cref="RegistrationAnswers.Empty"/> rather
/// than null for a member who has only verified their phone.
/// </summary>
public interface IMemberRegistrationDraftRepository
{
    Task<RegistrationAnswers?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Writes the whole answer sheet, creating the row on first save. The service merges; this stores.</summary>
    Task<RegistrationAnswers> UpsertAsync(
        Guid userId,
        RegistrationAnswers answers,
        CancellationToken cancellationToken);

    /// <summary>Removes the draft once its answers live on the profile, so no field has two homes.</summary>
    Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
