using Aynera.Application.Features.Settings.Repositories;
using Aynera.Application.Features.Settings.Services.Interfaces;
using Aynera.Domain.Settings.Records;
using Aynera.Domain.Settings.Requests;
using Aynera.Domain.Settings.Responses;
using Microsoft.Extensions.Logging;

namespace Aynera.Application.Features.Settings.Services.Implementations;

/// <summary>
/// The member's own settings: notification switches, pause, and which profile fields show. Every
/// change is partial — a field that is not sent keeps its value — and visibility merges per field,
/// so hiding the gender never touches anything else.
/// </summary>
public sealed class MemberSettingsService : IMemberSettingsService
{
    private readonly IMemberSettingsRepository _settings;
    private readonly ILogger<MemberSettingsService> _logger;

    public MemberSettingsService(IMemberSettingsRepository settings, ILogger<MemberSettingsService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<MemberSettingsDto> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        ToDto(await _settings.FindByUserIdAsync(userId, cancellationToken) ?? MemberSettingsRecord.Empty(userId));

    public async Task<MemberSettingsDto> UpdateAsync(
        Guid userId,
        UpdateMemberSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var current = await _settings.FindByUserIdAsync(userId, cancellationToken) ?? MemberSettingsRecord.Empty(userId);
        var updated = Apply(current, request, Microseconds(DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "UpdateSettings {UserId} paused {Paused} notify {Introductions}/{Replies}/{WeekendSurprise} visibility {Count}",
            userId, updated.IntroductionsPaused, updated.NotifyIntroductions, updated.NotifyReplies,
            updated.NotifyWeekendSurprise, request.Visibility?.Count ?? 0);

        return ToDto(await _settings.UpsertAsync(updated, cancellationToken));
    }

    /// <summary>
    /// Merges a change into the stored settings. The pause keeps the moment it began, and only a
    /// real change of state moves it — pausing twice does not restart the clock.
    /// </summary>
    public static MemberSettingsRecord Apply(
        MemberSettingsRecord current,
        UpdateMemberSettingsRequest request,
        DateTimeOffset now)
    {
        var paused = request.IntroductionsPaused ?? current.IntroductionsPaused;
        var visibility = new Dictionary<string, bool>(current.Visibility, StringComparer.Ordinal);
        foreach (var (field, shown) in request.Visibility ?? new Dictionary<string, bool>())
        {
            visibility[field] = shown;
        }

        return current with
        {
            NotifyIntroductions = request.NotifyIntroductions ?? current.NotifyIntroductions,
            NotifyReplies = request.NotifyReplies ?? current.NotifyReplies,
            NotifyWeekendSurprise = request.NotifyWeekendSurprise ?? current.NotifyWeekendSurprise,
            IntroductionsPaused = paused,
            PausedAtUtc = !paused ? null : current.IntroductionsPaused ? current.PausedAtUtc : now,
            Visibility = visibility,
        };
    }

    /// <summary>
    /// Postgres keeps microseconds; rounding first means the value returned now is the value read
    /// back later, so "when did the pause begin" never appears to move.
    /// </summary>
    public static DateTimeOffset Microseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, value.Offset);

    public static MemberSettingsDto ToDto(MemberSettingsRecord record) =>
        new(
            record.NotifyIntroductions,
            record.NotifyReplies,
            record.NotifyWeekendSurprise,
            record.IntroductionsPaused,
            record.PausedAtUtc,
            record.Visibility);
}
