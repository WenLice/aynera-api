using Aynera.Application.Features.Voice.Repositories;
using Aynera.Domain.Voice.Records;

namespace Aynera.Application.Tests;

/// <summary>In-memory spoken prompt answers, keyed by member and prompt.</summary>
internal sealed class FakeVoiceAnswerRepository : IVoiceAnswerRepository
{
    private readonly Dictionary<(Guid UserId, string PromptId), VoiceAnswerRecord> _rows = new();

    /// <summary>Records an answer directly, as if it had been uploaded.</summary>
    public void Add(Guid userId, string promptId) =>
        _rows[(userId, promptId)] = new VoiceAnswerRecord(
            userId, promptId, "audio/mp4", 3, [1, 2, 3], true, null, null, DateTimeOffset.UtcNow, null,
            $"{userId:D}/voice_{promptId}.m4a");

    public IReadOnlyList<string> PromptIds(Guid userId) =>
        _rows.Keys.Where(k => k.UserId == userId).Select(k => k.PromptId).ToList();

    public Task<IReadOnlyList<VoiceAnswerRecord>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VoiceAnswerRecord>>(
            _rows.Where(r => r.Key.UserId == userId).Select(r => r.Value with { Data = [] }).ToList());

    public Task<IReadOnlyList<string>> ListPromptIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(PromptIds(userId));

    public Task<VoiceAnswerRecord?> FindWithContentAsync(Guid userId, string promptId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault((userId, promptId)));

    public Task<VoiceAnswerRecord> UpsertAsync(VoiceAnswerRecord answer, CancellationToken cancellationToken)
    {
        var stored = answer with { StorageKey = $"{answer.UserId:D}/voice_{answer.PromptId}.m4a" };
        _rows[(answer.UserId, answer.PromptId)] = stored;
        return Task.FromResult(stored);
    }

    public Task<bool> SoftDeleteAsync(Guid userId, string promptId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.Remove((userId, promptId)));

    public Task SoftDeleteExceptAsync(Guid userId, IReadOnlyCollection<string> keepPromptIds, CancellationToken cancellationToken)
    {
        foreach (var key in _rows.Keys.Where(k => k.UserId == userId && !keepPromptIds.Contains(k.PromptId)).ToList())
        {
            _rows.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        SoftDeleteExceptAsync(userId, [], cancellationToken);
}
