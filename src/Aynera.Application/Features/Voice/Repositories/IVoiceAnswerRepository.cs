using Aynera.Domain.Voice.Records;

namespace Aynera.Application.Features.Voice.Repositories;

/// <summary>
/// Spoken prompt answers — <c>MemberMedia</c> rows of kind <c>VoiceAnswer</c>, one live row per
/// prompt, with the bytes at <c>{userId}/voice_{promptId}.{ext}</c>.
/// </summary>
public interface IVoiceAnswerRepository
{
    /// <summary>Metadata for every stored answer; <c>Data</c> is empty.</summary>
    Task<IReadOnlyList<VoiceAnswerRecord>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The prompt ids that have a stored answer — what registration progress needs, and nothing more.</summary>
    Task<IReadOnlyList<string>> ListPromptIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The recording's bytes, or null when there is none for that prompt.</summary>
    Task<VoiceAnswerRecord?> FindWithContentAsync(Guid userId, string promptId, CancellationToken cancellationToken);

    /// <summary>Stores the file, then creates or revives the row for that prompt.</summary>
    Task<VoiceAnswerRecord> UpsertAsync(VoiceAnswerRecord answer, CancellationToken cancellationToken);

    /// <summary>Soft-deletes the answer to one prompt. Returns false when there was none.</summary>
    Task<bool> SoftDeleteAsync(Guid userId, string promptId, CancellationToken cancellationToken);

    /// <summary>Soft-deletes every answer whose prompt is not in <paramref name="keepPromptIds"/>.</summary>
    Task SoftDeleteExceptAsync(Guid userId, IReadOnlyCollection<string> keepPromptIds, CancellationToken cancellationToken);

    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
