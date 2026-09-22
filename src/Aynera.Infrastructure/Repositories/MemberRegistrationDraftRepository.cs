using System.Text.Json;
using System.Text.Json.Serialization;
using Aynera.Application.Features.Registration.Repositories;
using Aynera.Domain.Registration.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

/// <summary>
/// Stores the registration answers as one <c>jsonb</c> document. The shape is typed at the API
/// boundary and only loosened here, so a new question costs no migration while nothing reaches
/// storage unvalidated.
/// </summary>
public sealed class MemberRegistrationDraftRepository : IMemberRegistrationDraftRepository
{
    /// <summary>
    /// Nulls are dropped so an untouched field is absent rather than stored as an explicit null —
    /// which keeps the document to what the member has actually answered.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AyneraDbContext _db;
    private readonly ILogger<MemberRegistrationDraftRepository> _logger;

    public MemberRegistrationDraftRepository(
        AyneraDbContext db,
        ILogger<MemberRegistrationDraftRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<RegistrationAnswers?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await _db.MemberRegistrationDrafts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        // A document this layer cannot read is a stored-data fault, not a missing draft. Returning
        // null would silently restart the member's registration, so it is raised instead.
        return JsonSerializer.Deserialize<RegistrationAnswers>(entity.Data, Json)
            ?? throw new InvalidOperationException(
                $"Registration draft for {userId:D} could not be read.");
    }

    public async Task<RegistrationAnswers> UpsertAsync(
        Guid userId,
        RegistrationAnswers answers,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberRegistrationDraft UpsertAsync user {UserId}", userId);

        var entity = await _db.MemberRegistrationDrafts
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        var data = JsonSerializer.Serialize(answers, Json);

        if (entity is null)
        {
            _db.MemberRegistrationDrafts.Add(new MemberRegistrationDraft
            {
                UserId = userId,
                Data = data,
            });
        }
        else
        {
            entity.Data = data;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return answers;
    }

    public async Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberRegistrationDraft DeleteByUserIdAsync user {UserId}", userId);

        // A hard delete, unlike the member tables: the answers now live on the profile, and keeping
        // a soft-deleted copy would leave the same field in two places.
        await _db.MemberRegistrationDrafts
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
