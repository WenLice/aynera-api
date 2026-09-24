using System.Text.Json;
using Aynera.Application.Features.Answers.Repositories;
using Aynera.Domain.Answers.Records;
using Aynera.Domain.Settings.Statics;
using Aynera.Domain.Answers.Statics;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

/// <summary>
/// Reads and writes the <c>jsonb</c> answer categories. The shape is typed at the API boundary and
/// only loosened here, so adding a question costs no migration while nothing reaches storage
/// unvalidated.
/// </summary>
public sealed class MemberProfileAnswersRepository : IMemberProfileAnswersRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AyneraDbContext _db;
    private readonly ILogger<MemberProfileAnswersRepository> _logger;

    public MemberProfileAnswersRepository(
        AyneraDbContext db,
        ILogger<MemberProfileAnswersRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<MemberProfileAnswersRecord?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await _db.MemberProfileAnswers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var visibility = await MemberSettingsRows.ReadVisibilityAsync(_db, userId, cancellationToken);
        var record = ToRecord(entity);
        return record with
        {
            Lifestyle = WithVisibility(record.Lifestyle, visibility, VisibilityKeys.Lifestyle),
            Beliefs = WithVisibility(record.Beliefs, visibility, VisibilityKeys.Beliefs),
        };
    }

    private static Dictionary<string, StoredChoice> Choices(IReadOnlyDictionary<string, MemberAnswer> answers) =>
        answers.ToDictionary(a => a.Key, a => new StoredChoice(a.Value.Option), StringComparer.Ordinal);

    private static Dictionary<string, MemberAnswer> WithVisibility(
        IReadOnlyDictionary<string, MemberAnswer> answers,
        IReadOnlyDictionary<string, bool> visibility,
        Func<string, string> key) =>
        answers.ToDictionary(
            a => a.Key,
            a => a.Value with { Public = VisibilityKeys.IsVisible(visibility, key(a.Key)) },
            StringComparer.Ordinal);

    /// <summary>What an everyday or belief answer stores: the option only.</summary>
    private sealed record StoredChoice(string Option);

    public async Task<MemberProfileAnswersRecord> UpsertAsync(
        MemberProfileAnswersRecord answers,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberProfileAnswers UpsertAsync user {UserId}", answers.UserId);

        var entity = await _db.MemberProfileAnswers
            .FirstOrDefaultAsync(x => x.UserId == answers.UserId, cancellationToken);

        // The answers keep only the choice; whether each shows on the profile is a visibility
        // setting, stored with the member's other ones. "Prefer not to say" is never published, so
        // it has no visibility — nothing is written for it and any existing entry is left alone.
        var lifestyle = JsonSerializer.Serialize(Choices(answers.Lifestyle), Json);
        var beliefs = JsonSerializer.Serialize(Choices(answers.Beliefs), Json);
        await MemberSettingsRows.SetVisibilityAsync(
            _db,
            answers.UserId,
            answers.Lifestyle.Where(a => !AnswerRules.IsDeclineOption(a.Value.Option))
                .Select(a => new KeyValuePair<string, bool>(VisibilityKeys.Lifestyle(a.Key), a.Value.Public))
                .Concat(answers.Beliefs.Where(a => !AnswerRules.IsDeclineOption(a.Value.Option))
                    .Select(a => new KeyValuePair<string, bool>(VisibilityKeys.Beliefs(a.Key), a.Value.Public))),
            cancellationToken);
        var vibe = JsonSerializer.Serialize(answers.Vibe, Json);
        var prompts = JsonSerializer.Serialize(answers.Prompts, Json);
        var rhythm = JsonSerializer.Serialize(answers.Rhythm, Json);

        if (entity is null)
        {
            _db.MemberProfileAnswers.Add(new MemberProfileAnswers
            {
                UserId = answers.UserId,
                Lifestyle = lifestyle,
                Beliefs = beliefs,
                Vibe = vibe,
                Prompts = prompts,
                Dealbreaker = answers.Dealbreaker,
                Rhythm = rhythm,
            });
        }
        else
        {
            entity.Lifestyle = lifestyle;
            entity.Beliefs = beliefs;
            entity.Vibe = vibe;
            entity.Prompts = prompts;
            entity.Dealbreaker = answers.Dealbreaker;
            entity.Rhythm = rhythm;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return answers;
    }

    public async Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await _db.MemberProfileAnswers
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return;
        }

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static MemberProfileAnswersRecord ToRecord(MemberProfileAnswers entity) =>
        new(
            entity.UserId,
            Read<Dictionary<string, MemberAnswer>>(entity.Lifestyle, entity.UserId, nameof(entity.Lifestyle)),
            Read<Dictionary<string, MemberAnswer>>(entity.Beliefs, entity.UserId, nameof(entity.Beliefs)),
            Read<List<string>>(entity.Vibe, entity.UserId, nameof(entity.Vibe)),
            Read<List<MemberPromptAnswer>>(entity.Prompts, entity.UserId, nameof(entity.Prompts)),
            entity.Dealbreaker,
            Read<Dictionary<string, string>>(entity.Rhythm, entity.UserId, nameof(entity.Rhythm)));

    /// <summary>
    /// A document this layer cannot read is a stored-data fault, not an empty category. Returning
    /// an empty collection would quietly tell the member they had answered nothing, so it is raised.
    /// </summary>
    private static T Read<T>(string json, Guid userId, string column)
        where T : new() =>
        JsonSerializer.Deserialize<T>(json, Json)
        ?? throw new InvalidOperationException(
            $"{column} answers for {userId:D} could not be read.");
}
