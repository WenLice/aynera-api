using System.Text.Json;
using Aynera.Application.Features.Answers.Repositories;
using Aynera.Domain.Answers.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

/// <summary>
/// Reads and writes the three <c>jsonb</c> categories. The shape is typed at the API boundary and
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

        return entity is null ? null : ToRecord(entity);
    }

    public async Task<MemberProfileAnswersRecord> UpsertAsync(
        MemberProfileAnswersRecord answers,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberProfileAnswers UpsertAsync user {UserId}", answers.UserId);

        var entity = await _db.MemberProfileAnswers
            .FirstOrDefaultAsync(x => x.UserId == answers.UserId, cancellationToken);

        var lifestyle = JsonSerializer.Serialize(answers.Lifestyle, Json);
        var beliefs = JsonSerializer.Serialize(answers.Beliefs, Json);
        var vibe = JsonSerializer.Serialize(answers.Vibe, Json);

        if (entity is null)
        {
            _db.MemberProfileAnswers.Add(new MemberProfileAnswers
            {
                UserId = answers.UserId,
                Lifestyle = lifestyle,
                Beliefs = beliefs,
                Vibe = vibe,
            });
        }
        else
        {
            entity.Lifestyle = lifestyle;
            entity.Beliefs = beliefs;
            entity.Vibe = vibe;
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
            Read<List<string>>(entity.Vibe, entity.UserId, nameof(entity.Vibe)));

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
