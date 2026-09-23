using Aynera.Application.Features.Answers.Repositories;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Preferences.Repositories;
using Aynera.Application.Features.Preferences.Services.Interfaces;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Application.Features.Registration.Repositories;
using Aynera.Application.Features.Registration.Services.Interfaces;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Answers.Records;
using Aynera.Domain.Answers.Responses;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Preferences.Enums;
using Aynera.Domain.Preferences.Records;
using Aynera.Domain.Preferences.Requests;
using Aynera.Domain.Preferences.Responses;
using Aynera.Domain.Registration.Records;
using Aynera.Domain.Registration.Requests;
using Aynera.Domain.Registration.Responses;
using Aynera.Domain.Registration.Statics;
using AutoMapper;
using Aynera.Application.Features.Admissions.Models;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Liveness.Repositories;
using Aynera.Domain.Liveness.Enums;
using Aynera.Domain.Admissions.Statics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Registration.Services.Implementations;

/// <summary>
/// The registration walk's single write path. The app calls the same endpoint on every page and
/// never learns where an answer landed, so the completeness rules live here and nowhere else.
/// <para>
/// The draft holds two groups that promote to two different tables at two different moments — the
/// personal details to <c>MemberProfile</c>, the matching preferences to <c>MemberPreferences</c> —
/// and neither waits for the other. Once a group is promoted its own table owns it, and a later
/// page edits that table directly.
/// </para>
/// </summary>
public sealed class RegistrationDraftService : IRegistrationDraftService
{
    private readonly IUserRepository _users;
    private readonly IMemberProfileRepository _profiles;
    private readonly IMemberPreferencesRepository _preferences;
    private readonly IMemberProfileAnswersRepository _answers;
    private readonly IMemberRegistrationDraftRepository _drafts;
    private readonly IRegistrationService _registration;
    private readonly IMemberPreferencesService _preferencesService;
    private readonly IMemberPhotoRepository _photos;
    private readonly IMemberConsentRepository _consents;
    private readonly ILivenessRepository _liveness;
    private readonly PhotoOptions _photoOptions;
    private readonly AdmissionOptions _admissionOptions;
    private readonly IMapper _mapper;
    private readonly ILogger<RegistrationDraftService> _logger;

    public RegistrationDraftService(
        IUserRepository users,
        IMemberProfileRepository profiles,
        IMemberPreferencesRepository preferences,
        IMemberProfileAnswersRepository answers,
        IMemberRegistrationDraftRepository drafts,
        IRegistrationService registration,
        IMemberPreferencesService preferencesService,
        IMemberPhotoRepository photos,
        IMemberConsentRepository consents,
        ILivenessRepository liveness,
        IOptions<PhotoOptions> photoOptions,
        IOptions<AdmissionOptions> admissionOptions,
        IMapper mapper,
        ILogger<RegistrationDraftService> logger)
    {
        _users = users;
        _profiles = profiles;
        _preferences = preferences;
        _answers = answers;
        _drafts = drafts;
        _registration = registration;
        _preferencesService = preferencesService;
        _photos = photos;
        _consents = consents;
        _liveness = liveness;
        _photoOptions = photoOptions.Value;
        _admissionOptions = admissionOptions.Value;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<RegistrationProgressDto> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireMemberAsync(userId, cancellationToken);
        var state = await LoadAsync(userId, cancellationToken);

        return Progress(user, state);
    }

    public async Task<RegistrationProgressDto> PatchAsync(
        Guid userId,
        UpdateRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await RequireMemberAsync(userId, cancellationToken);
        var state = await LoadAsync(userId, cancellationToken);
        var merged = Merge(state.Answers, request);

        var profile = state.Profile;
        var preferences = state.Preferences;

        if (profile is not null)
        {
            // The profile already holds every required field, so merging a page over it always
            // yields a complete request — no partial row can be written from this branch.
            _logger.LogInformation("Registration patch onto an existing profile for {UserId}", userId);
            profile = (await _registration.SaveProfileAsync(
                userId, ToProfileRequest(merged), cancellationToken)).Profile;
        }
        else if (RegistrationProgress.IsProfileComplete(merged))
        {
            _logger.LogInformation("Personal details complete — promoting profile for {UserId}", userId);

            // SaveProfileAsync owns the city lookup, the account lock and the audit entry, so
            // promotion reuses the one-shot path instead of growing a second way to create one.
            profile = (await _registration.SaveProfileAsync(
                userId, ToProfileRequest(merged), cancellationToken)).Profile;
        }

        if (preferences is not null)
        {
            _logger.LogInformation("Registration patch onto existing preferences for {UserId}", userId);
            preferences = await _preferencesService.SaveAsync(
                userId, ToPreferencesRequest(merged), cancellationToken);
        }
        else if (RegistrationProgress.IsPreferencesComplete(merged))
        {
            _logger.LogInformation("Preferences complete — promoting for {UserId}", userId);
            preferences = await _preferencesService.SaveAsync(
                userId, ToPreferencesRequest(merged), cancellationToken);
        }

        // The everyday, belief and vibe answers never pass through the draft. The draft exists to
        // hold data that cannot be stored until it is complete, and every one of these questions is
        // optional — so their own table can take them straight away, one page at a time.
        var profileAnswers = state.ProfileAnswers;
        if (request.Lifestyle is not null || request.Beliefs is not null || request.Vibe is not null)
        {
            profileAnswers = await _answers.UpsertAsync(
                MergeAnswers(profileAnswers ?? MemberProfileAnswersRecord.Empty(userId), request),
                cancellationToken);
        }

        // Only what has not been promoted stays in the draft. Dropping the whole row on the first
        // promotion would discard the other group's answers, which is what makes this a subtraction
        // rather than a delete — and the subtraction happens after the writes, so a failure leaves
        // the member's answers recoverable.
        var remaining = merged;
        if (profile is not null) remaining = remaining.WithoutProfile();
        if (preferences is not null) remaining = remaining.WithoutPreferences();

        if (remaining.IsEmpty)
        {
            await _drafts.DeleteByUserIdAsync(userId, cancellationToken);
        }
        else
        {
            await _drafts.UpsertAsync(userId, remaining, cancellationToken);
        }

        // A page write never touches photos or consents, so their state carries over as loaded.
        return Progress(
            user,
            new State(
                remaining, profile, preferences, profileAnswers,
                state.PhotosComplete, state.ConsentsAccepted, state.LivenessPassed));
    }

    /// <summary>
    /// What the member has answered, wherever it currently lives. A promoted group is read back
    /// from its own table rather than the draft — there should be nothing left in the draft for it,
    /// and reporting from two places invites drift.
    /// </summary>
    private async Task<State> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FindByUserIdAsync(userId, cancellationToken);
        var preferences = await _preferences.FindByUserIdAsync(userId, cancellationToken);
        var draft = await _drafts.FindByUserIdAsync(userId, cancellationToken) ?? RegistrationAnswers.Empty;

        return new State(
            draft,
            profile is null ? null : _mapper.Map<MemberProfileDto>(profile),
            preferences is null ? null : ToDto(preferences),
            await _answers.FindByUserIdAsync(userId, cancellationToken),
            await _photos.CountByUserIdAsync(userId, cancellationToken) >= _photoOptions.MaxCount,
            ConsentRules.Missing(
                _admissionOptions.RequiredConsentVersions,
                await _consents.ListByUserIdAsync(userId, cancellationToken)).Count == 0,
            (await _liveness.FindLatestCompletedAsync(userId, cancellationToken))?.Outcome
                == LivenessOutcome.Passed.ToString());
    }

    private sealed record State(
        RegistrationAnswers Draft,
        MemberProfileDto? Profile,
        MemberPreferencesDto? Preferences,
        MemberProfileAnswersRecord? ProfileAnswers,
        bool PhotosComplete,
        bool ConsentsAccepted,
        bool LivenessPassed)
    {
        /// <summary>The draft plus whatever has already been promoted, as one answer sheet.</summary>
        public RegistrationAnswers Answers => FromPreferences(FromProfile(Draft, Profile), Preferences);
    }

    private async Task<UserRecord> RequireMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken)
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        if (!string.Equals(user.AccountKind, nameof(AccountKind.Member), StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException("not_a_member", "This account is not a member account.", statusCode: 403);
        }

        return user;
    }

    private static RegistrationProgressDto Progress(UserRecord user, State state)
    {
        var completed = RegistrationProgress.Completed(
            user.PhoneConfirmed,
            user.EmailConfirmed,
            state.Answers,
            state.ProfileAnswers,
            state.PhotosComplete,
            state.ConsentsAccepted,
            state.LivenessPassed);

        return new RegistrationProgressDto(
            state.Draft,
            completed,
            RegistrationProgress.NextStep(completed),
            state.Profile,
            state.Preferences,
            state.ProfileAnswers is null
                ? null
                : new MemberProfileAnswersDto(
                    state.ProfileAnswers.Lifestyle,
                    state.ProfileAnswers.Beliefs,
                    state.ProfileAnswers.Vibe));
    }

    /// <summary>
    /// Applies one page's everyday, belief or vibe answers.
    /// <para>
    /// The two keyed categories merge per question, so a page carrying one answer leaves the rest
    /// alone. <c>Vibe</c> replaces wholesale, because it is a set the member edits as a whole —
    /// merging it would make deselecting a chip impossible, and the app already holds the full
    /// selection to send.
    /// </para>
    /// </summary>
    private static MemberProfileAnswersRecord MergeAnswers(
        MemberProfileAnswersRecord current,
        UpdateRegistrationRequest request) =>
        current with
        {
            Lifestyle = MergeCategory(current.Lifestyle, request.Lifestyle),
            Beliefs = MergeCategory(current.Beliefs, request.Beliefs),
            Vibe = request.Vibe is null ? current.Vibe : [.. request.Vibe],
        };

    private static IReadOnlyDictionary<string, MemberAnswer> MergeCategory(
        IReadOnlyDictionary<string, MemberAnswer> current,
        IReadOnlyDictionary<string, MemberAnswer>? sent)
    {
        if (sent is null)
        {
            return current;
        }

        var merged = new Dictionary<string, MemberAnswer>(current, StringComparer.Ordinal);
        foreach (var (question, answer) in sent)
        {
            merged[question] = answer;
        }

        return merged;
    }

    /// <summary>
    /// Applies one page's answers. A null field was not sent and is left alone; an empty string
    /// clears an optional value, which is how the app turns a nickname back into an initial. The
    /// validator refuses an empty string on a required field, so this can never blank one.
    /// </summary>
    private static RegistrationAnswers Merge(RegistrationAnswers current, UpdateRegistrationRequest request) =>
        current with
        {
            Name = request.Name ?? current.Name,
            Nickname = Optional(request.Nickname, current.Nickname),
            Gender = request.Gender?.ToString() ?? current.Gender,
            GenderIsPublic = request.GenderIsPublic ?? current.GenderIsPublic,
            DateOfBirth = request.DateOfBirth ?? current.DateOfBirth,
            HeightCm = request.HeightCm ?? current.HeightCm,
            Hometown = request.Hometown ?? current.Hometown,
            City = request.City ?? current.City,
            Work = Optional(request.Work, current.Work),
            InterestedIn = request.InterestedIn?.ToString() ?? current.InterestedIn,
            MinAge = request.MinAge ?? current.MinAge,
            // An explicit open end wins over both the value sent and the one stored; that flag is
            // the only way to move an upper end back to "and older" under partial-write rules.
            MaxAge = request.MaxAgeIsOpen == true ? null : request.MaxAge ?? current.MaxAge,
            AgeIsFlexible = request.AgeIsFlexible ?? current.AgeIsFlexible,
            Track = request.Track?.ToString() ?? current.Track,
            Outcome = request.Outcome?.ToString() ?? current.Outcome,
        };

    private static UpdateMemberProfileRequest ToProfileRequest(RegistrationAnswers answers) =>
        new(
            Name: answers.Name!,
            Gender: Parse<Gender>(answers.Gender, "gender_invalid", "Gender is invalid."),
            DateOfBirth: answers.DateOfBirth!.Value,
            City: answers.City!,
            Nickname: answers.Nickname,
            HeightCm: answers.HeightCm,
            Hometown: answers.Hometown,
            Work: answers.Work,
            Religion: null,
            GenderIsPublic: answers.GenderIsPublic ?? true);

    private static UpdateMemberPreferencesRequest ToPreferencesRequest(RegistrationAnswers answers) =>
        new(
            InterestedIn: Parse<InterestedIn>(
                answers.InterestedIn, "interested_in_invalid", "Who you'd like to meet is invalid."),
            MinAge: answers.MinAge!.Value,
            MaxAge: answers.MaxAge,
            Track: Parse<RelationshipTrack>(answers.Track, "track_invalid", "Track is invalid."),
            Outcome: Parse<RelationshipOutcome>(answers.Outcome, "outcome_invalid", "Outcome is invalid."),
            AgeIsFlexible: answers.AgeIsFlexible ?? false);

    private static RegistrationAnswers FromProfile(RegistrationAnswers draft, MemberProfileDto? profile) =>
        profile is null
            ? draft
            : draft with
            {
                Name = profile.Name,
                Nickname = profile.Nickname,
                Gender = profile.Gender,
                GenderIsPublic = profile.GenderIsPublic,
                DateOfBirth = profile.DateOfBirth,
                HeightCm = profile.HeightCm,
                Hometown = profile.Hometown,
                City = profile.City,
                Work = profile.Work,
            };

    private static RegistrationAnswers FromPreferences(
        RegistrationAnswers draft,
        MemberPreferencesDto? preferences) =>
        preferences is null
            ? draft
            : draft with
            {
                InterestedIn = preferences.InterestedIn,
                MinAge = preferences.MinAge,
                MaxAge = preferences.MaxAge,
                AgeIsFlexible = preferences.AgeIsFlexible,
                Track = preferences.Track,
                Outcome = preferences.Outcome,
            };

    private static MemberPreferencesDto ToDto(MemberPreferencesRecord record) =>
        new(
            record.InterestedIn,
            record.MinAge,
            record.MaxAge,
            record.AgeIsFlexible,
            record.Track,
            record.Outcome);

    /// <summary>Null means the field was not sent; an empty string means clear it.</summary>
    private static string? Optional(string? sent, string? current) =>
        sent is null ? current : string.IsNullOrWhiteSpace(sent) ? null : sent;

    /// <summary>
    /// A stored value the enum no longer knows is a data problem, not a request problem. It is
    /// raised rather than quietly mapped to the enum's zero value, which would silently change what
    /// the member chose.
    /// </summary>
    private static TEnum Parse<TEnum>(string? stored, string errorCode, string message)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(stored, ignoreCase: true, out var value)
            ? value
            : throw new AuthException(errorCode, message);
}
