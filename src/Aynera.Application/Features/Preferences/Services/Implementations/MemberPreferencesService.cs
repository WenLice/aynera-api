using Aynera.Application.Common;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Preferences.Repositories;
using Aynera.Application.Features.Preferences.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Preferences.Records;
using Aynera.Domain.Preferences.Requests;
using Aynera.Domain.Preferences.Responses;
using Aynera.Domain.Preferences.Validators;
using Microsoft.Extensions.Logging;

namespace Aynera.Application.Features.Preferences.Services.Implementations;

public sealed class MemberPreferencesService(
    IMemberPreferencesRepository preferences,
    IUserRepository users,
    IWorkflowTransaction transaction,
    IAuditWriter audit,
    ILogger<MemberPreferencesService> logger) : IMemberPreferencesService
{
    public async Task<MemberPreferencesDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var record = await preferences.FindByUserIdAsync(userId, cancellationToken);
        return record is null ? null : ToDto(record);
    }

    public async Task<MemberPreferencesDto> SaveAsync(
        Guid userId,
        UpdateMemberPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("SavePreferences for user {UserId}", userId);

        var (before, after) = await transaction.ExecuteAsync(
            new[] { $"account:{userId:D}" },
            async ct =>
            {
                _ = await users.FindByIdAsync(userId, ct)
                    ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

                var previous = await preferences.FindByUserIdAsync(userId, ct);
                var saved = await preferences.UpsertAsync(
                    new MemberPreferencesRecord(
                        userId,
                        request.InterestedIn.ToString(),
                        request.MinAge,
                        request.MaxAge,
                        request.AgeIsFlexible,
                        request.Track.ToString(),
                        request.Outcome.ToString()),
                    ct);

                return (previous, saved);
            },
            cancellationToken);

        await audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberPreferencesSaved,
                Outcome: AuditOutcomes.Success,
                Message: before is null ? "Member preferences created." : "Member preferences updated.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: userId.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("interestedIn", before?.InterestedIn, after.InterestedIn),
                    ("minAge", before?.MinAge, after.MinAge),
                    ("maxAge", before?.MaxAge, after.MaxAge),
                    ("ageIsFlexible", before?.AgeIsFlexible, after.AgeIsFlexible),
                    ("track", before?.Track, after.Track),
                    ("outcome", before?.Outcome, after.Outcome)
                ])),
            cancellationToken);

        logger.LogInformation("SavePreferences succeeded for user {UserId}", userId);
        return ToDto(after);
    }

    // The track is read back from the row rather than recomputed: it is validated against the
    // outcome on every write, so the stored value is authoritative and a silent recompute here
    // would hide a row that had somehow drifted instead of surfacing it.
    private static MemberPreferencesDto ToDto(MemberPreferencesRecord record) =>
        new(
            record.InterestedIn,
            record.MinAge,
            record.MaxAge,
            record.AgeIsFlexible,
            record.Track,
            record.Outcome);
}
