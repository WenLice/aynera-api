using Aynera.Application.Features.Admissions.Models;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Application.Features.Answers.Repositories;
using Aynera.Application.Features.Liveness.Repositories;
using Aynera.Domain.Liveness.Enums;
using Aynera.Domain.Liveness.Records;
using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Records;
using Aynera.Domain.Photos.Records;
using Microsoft.Extensions.Options;
using Aynera.Application.Features.Registration.Repositories;
using Aynera.Application.Features.Registration.Services.Implementations;
using Aynera.Application.Features.Preferences.Services.Interfaces;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Answers.Records;
using Aynera.Domain.Answers.Responses;
using Aynera.Domain.Preferences.Enums;
using Aynera.Domain.Preferences.Records;
using Aynera.Domain.Preferences.Requests;
using Aynera.Domain.Preferences.Responses;
using Aynera.Domain.Preferences.Validators;
using Aynera.Domain.Registration.Records;
using Aynera.Domain.Registration.Requests;
using Aynera.Domain.Registration.Statics;

namespace Aynera.Application.Tests;

/// <summary>
/// The behaviour that makes per-page saving safe: answers accumulate in the draft, the profile is
/// created only once every required answer is present, and from then on the same call edits the
/// profile — which can never be written incomplete.
/// </summary>
public class RegistrationDraftServiceTests
{
    private static readonly DateOnly AdultDob = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25);

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public AdmissionProfileRepo Profiles { get; } = new();
        public FakeMemberPreferencesRepository Preferences { get; } = new();
        public FakeRegistrationDraftRepository Drafts { get; } = new();
        public FakeProfileAnswersRepository Answers { get; } = new();
        public StubRegistrationService Registration { get; }
        public StubPreferencesService PreferencesService { get; }
        public CountedPhotos Photos { get; } = new();
        public ListedConsents Consents { get; } = new();
        public LatestLiveness Liveness { get; } = new();
        public FakeVoiceAnswerRepository Voice { get; } = new();
        public FakeMemberSettingsRepository Settings { get; } = new();
        public RegistrationDraftService Service { get; }

        public Harness()
        {
            Registration = new StubRegistrationService(Profiles);
            PreferencesService = new StubPreferencesService(Preferences);
            Service = new RegistrationDraftService(
                Users, Profiles, Preferences, Answers, Drafts, Registration, PreferencesService,
                Photos, Consents, Liveness, Voice, Settings, Options.Create(new PhotoOptions()), Options.Create(new AdmissionOptions()),
                TestMapper.Instance, DiscardLogger<RegistrationDraftService>.Instance);
        }

        public Guid AddMember(bool phoneConfirmed = true, bool emailConfirmed = true)
        {
            var id = Guid.NewGuid();
            Users.Add(new UserRecord(
                id, "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999), phoneConfirmed,
                $"{id:N}@example.test", emailConfirmed, "Member", true, false, false, false, ["Member"]));
            return id;
        }
    }

    // ---- accumulating, page by page -------------------------------------------------------

    [Fact]
    public async Task FirstPage_IsStoredWithoutCreatingAProfile()
    {
        var h = new Harness();
        var id = h.AddMember();

        var progress = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Name: "Ada Lovelace"), default);

        Assert.Equal("Ada Lovelace", progress.Answers.Name);
        Assert.Null(progress.Profile);
        Assert.Null(await h.Profiles.FindByUserIdAsync(id, default));
        Assert.Equal(RegistrationProgress.Self, progress.NextStep);
    }

    /// <summary>
    /// The reason the draft exists: a required answer can be stored on its own, which a NOT NULL
    /// profile column could never accept.
    /// </summary>
    [Fact]
    public async Task RequiredAnswersArriveOnePageAtATime()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Name: "Ada"), default);
        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Gender: Gender.Female), default);
        var afterBirth = await h.Service.PatchAsync(
            id, new UpdateRegistrationRequest(DateOfBirth: AdultDob, Hometown: "Pune"), default);

        Assert.Null(afterBirth.Profile);
        Assert.Equal(RegistrationProgress.Life, afterBirth.NextStep);
        Assert.Equal("Ada", afterBirth.Answers.Name);
        Assert.Equal("Female", afterBirth.Answers.Gender);
        Assert.Equal(AdultDob, afterBirth.Answers.DateOfBirth);
    }

    [Fact]
    public async Task LaterPages_DoNotOverwriteEarlierAnswers()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Name: "Ada", Nickname: "Adz"), default);
        var after = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(HeightCm: 168), default);

        Assert.Equal("Ada", after.Answers.Name);
        Assert.Equal("Adz", after.Answers.Nickname);
        Assert.Equal(168, after.Answers.HeightCm);
    }

    // ---- promotion ------------------------------------------------------------------------

    [Fact]
    public async Task TheAnswerThatCompletesTheSet_CreatesTheProfile()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(
                Name: "Ada", Gender: Gender.Female, DateOfBirth: AdultDob, Hometown: "Pune"),
            default);

        var promoted = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(City: "Bangalore"), default);

        Assert.NotNull(promoted.Profile);
        Assert.Equal("Ada", promoted.Profile!.Name);
        Assert.Equal("Bangalore", promoted.Profile.City);
        // The personal details are done, so the flow moves on to the preference pages.
        Assert.Equal(RegistrationProgress.Looking, promoted.NextStep);
        Assert.NotNull(await h.Profiles.FindByUserIdAsync(id, default));
    }

    /// <summary>The draft is dropped on promotion so no field ever has two homes.</summary>
    [Fact]
    public async Task Promotion_DiscardsTheDraft()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, CompletePage(), default);

        Assert.Null(await h.Drafts.FindByUserIdAsync(id, default));
    }

    [Fact]
    public async Task Promotion_CarriesTheOptionalAnswersThrough()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Nickname: "Adz", HeightCm: 168, Work: "Compilers"), default);
        var promoted = await h.Service.PatchAsync(id, CompletePage(), default);

        Assert.Equal("Adz", promoted.Profile!.Nickname);
        Assert.Equal(168, promoted.Profile.HeightCm);
        Assert.Equal("Compilers", promoted.Profile.Work);
    }

    // ---- after promotion ------------------------------------------------------------------

    [Fact]
    public async Task OnceAProfileExists_TheSameCallEditsIt()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePage(), default);

        var edited = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Work: "Writes compilers"), default);

        Assert.Equal("Writes compilers", edited.Profile!.Work);
        Assert.Null(await h.Drafts.FindByUserIdAsync(id, default));
    }

    /// <summary>
    /// A page carrying one field must not blank the rest of the profile — the merge happens against
    /// the stored row, so what the request omits survives.
    /// </summary>
    [Fact]
    public async Task EditingOneField_LeavesTheOthersIntact()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePage() with { Nickname = "Adz", HeightCm = 168 }, default);

        var edited = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(HeightCm: 170), default);

        Assert.Equal("Ada", edited.Profile!.Name);
        Assert.Equal("Adz", edited.Profile.Nickname);
        Assert.Equal("Pune", edited.Profile.Hometown);
        Assert.Equal(170, edited.Profile.HeightCm);
    }

    /// <summary>Null means "not sent"; an empty string is how the app turns a nickname back into an initial.</summary>
    [Fact]
    public async Task EmptyString_ClearsAnOptionalValue()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePage() with { Nickname = "Adz" }, default);

        var cleared = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Nickname: ""), default);

        Assert.Null(cleared.Profile!.Nickname);
    }

    // ---- preferences, pages 17 to 22 -------------------------------------------------------

    [Fact]
    public async Task WhoToMeet_IsStoredWithoutCreatingPreferences()
    {
        var h = new Harness();
        var id = h.AddMember();

        var progress = await h.Service.PatchAsync(
            id, new UpdateRegistrationRequest(InterestedIn: InterestedIn.Male, MinAge: 24, MaxAge: 32), default);

        Assert.Null(progress.Preferences);
        Assert.Equal("Male", progress.Answers.InterestedIn);
        Assert.Null(await h.Preferences.FindByUserIdAsync(id, default));
    }

    /// <summary>
    /// The track and outcome complete the §6 hard filters, so the preferences row is created at the
    /// intent page — independently of whether the profile exists yet.
    /// </summary>
    [Fact]
    public async Task TheIntentPage_CreatesThePreferences()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(
            id, new UpdateRegistrationRequest(InterestedIn: InterestedIn.Male, MinAge: 24, MaxAge: 32), default);

        var promoted = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Track: RelationshipTrack.Intent, Outcome: RelationshipOutcome.Prospect),
            default);

        Assert.NotNull(promoted.Preferences);
        Assert.Equal("Male", promoted.Preferences!.InterestedIn);
        Assert.Equal(24, promoted.Preferences.MinAge);
        Assert.Equal(32, promoted.Preferences.MaxAge);
        Assert.Equal("Intent", promoted.Preferences.Track);
        Assert.Equal("Prospect", promoted.Preferences.Outcome);
    }

    /// <summary>
    /// The bug this guards: promotion used to drop the whole draft row, which would have discarded
    /// preference answers that were not complete yet. Each group must leave on its own.
    /// </summary>
    [Fact]
    public async Task PromotingTheProfile_KeepsUnfinishedPreferenceAnswers()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(InterestedIn: InterestedIn.Male), default);
        var afterProfile = await h.Service.PatchAsync(id, CompletePage(), default);

        Assert.NotNull(afterProfile.Profile);
        Assert.Null(afterProfile.Preferences);

        // Still in the draft, and still reported, rather than lost with the promoted profile.
        Assert.Equal("Male", afterProfile.Answers.InterestedIn);
        Assert.Equal("Male", (await h.Drafts.FindByUserIdAsync(id, default))!.InterestedIn);
    }

    /// <summary>And the same in reverse — a promoted preferences row must not take the draft with it.</summary>
    [Fact]
    public async Task PromotingPreferences_KeepsUnfinishedProfileAnswers()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Name: "Ada"), default);
        var afterPreferences = await h.Service.PatchAsync(id, CompletePreferences(), default);

        Assert.NotNull(afterPreferences.Preferences);
        Assert.Null(afterPreferences.Profile);
        Assert.Equal("Ada", afterPreferences.Answers.Name);
    }

    [Fact]
    public async Task OncePreferencesExist_TheSameCallEditsThem()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePreferences(), default);

        var edited = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(MinAge: 27), default);

        Assert.Equal(27, edited.Preferences!.MinAge);
        Assert.Equal("Prospect", edited.Preferences.Outcome);
    }

    /// <summary>
    /// Null already means "not sent" under partial writes, so the slider's ceiling needs its own
    /// flag — otherwise an upper end could be set but never moved back to "and older".
    /// </summary>
    [Fact]
    public async Task MaxAgeIsOpen_ClearsAnUpperEndThatWasSet()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePreferences(), default);
        Assert.Equal(32, (await h.Service.GetAsync(id, default)).Preferences!.MaxAge);

        var opened = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(MaxAgeIsOpen: true), default);

        Assert.Null(opened.Preferences!.MaxAge);
    }

    /// <summary>
    /// Both promoted groups leave the draft, so nothing remains to keep — and the flow carries on
    /// to the optional questions rather than ending here.
    /// </summary>
    [Fact]
    public async Task BothGroupsComplete_LeavesNoDraft()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, CompletePage(), default);
        var done = await h.Service.PatchAsync(id, CompletePreferences(), default);

        Assert.NotNull(done.Profile);
        Assert.NotNull(done.Preferences);
        Assert.Null(await h.Drafts.FindByUserIdAsync(id, default));
        Assert.Equal(RegistrationProgress.Lifestyle, done.NextStep);
    }

    /// <summary>After the vibe pages the member is sent to their photos, never past them to consent.</summary>
    [Fact]
    public async Task EveryPageAnswered_ResumesAtTheFaceCheck()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);

        var progress = await h.Service.GetAsync(id, default);

        Assert.Equal(RegistrationProgress.Liveness, progress.NextStep);
    }

    [Fact]
    public async Task FaceCheckPassed_ResumesAtPhotos()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        h.Liveness.Set(id, LivenessOutcome.Passed);

        var progress = await h.Service.GetAsync(id, default);

        Assert.Equal(RegistrationProgress.Photos, progress.NextStep);
        Assert.Contains(RegistrationProgress.Liveness, progress.Completed);
    }

    /// <summary>Only a pass moves the member on, even with photos already in place.</summary>
    [Theory]
    [InlineData(LivenessOutcome.NotLive)]
    [InlineData(LivenessOutcome.FaceMismatch)]
    [InlineData(LivenessOutcome.Expired)]
    public async Task FaceCheckThatDidNotPass_KeepsTheMemberOnIt(LivenessOutcome outcome)
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        h.Photos.Count = new PhotoOptions().MaxCount;
        h.Liveness.Set(id, outcome);

        Assert.Equal(RegistrationProgress.Liveness, (await h.Service.GetAsync(id, default)).NextStep);
    }

    /// <summary>The intro video is optional and not a step, so photos lead straight to the prompts.</summary>
    [Fact]
    public async Task FaceCheckPassed_AndPhotosFilled_ResumesAtThePrompts()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        h.Photos.Count = new PhotoOptions().MaxCount;
        h.Liveness.Set(id, LivenessOutcome.Passed);

        Assert.Equal(RegistrationProgress.Voice, (await h.Service.GetAsync(id, default)).NextStep);
    }

    [Fact]
    public async Task PromptsAndNotificationsAnswered_ResumesAtConsent()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        h.Photos.Count = new PhotoOptions().MaxCount;
        h.Liveness.Set(id, LivenessOutcome.Passed);
        await AnswerPromptsAndNotificationsAsync(h, id);

        Assert.Equal(RegistrationProgress.Consent, (await h.Service.GetAsync(id, default)).NextStep);
    }

    [Fact]
    public async Task OneSlotShort_IsNotPhotosDone()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        h.Liveness.Set(id, LivenessOutcome.Passed);
        h.Photos.Count = new PhotoOptions().MaxCount - 1;

        Assert.Equal(RegistrationProgress.Photos, (await h.Service.GetAsync(id, default)).NextStep);
    }

    /// <summary>
    /// Consent counts only when every required document is accepted, each at its current version:
    /// the same rule eligibility applies, so resume and review cannot disagree.
    /// </summary>
    [Fact]
    public async Task ConsentStep_NeedsEveryDocument_AtItsCurrentVersion()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        h.Photos.Count = new PhotoOptions().MaxCount;
        h.Liveness.Set(id, LivenessOutcome.Passed);
        await AnswerPromptsAndNotificationsAsync(h, id);

        h.Consents.Accept(id, ConsentPolicyKind.Terms, "1.0");
        h.Consents.Accept(id, ConsentPolicyKind.Privacy, "1.0");
        Assert.Equal(RegistrationProgress.Consent, (await h.Service.GetAsync(id, default)).NextStep);

        // An older version of the missing document is not agreement to the current one.
        h.Consents.Accept(id, ConsentPolicyKind.CommunityGuidelines, "0.9");
        Assert.Equal(RegistrationProgress.Consent, (await h.Service.GetAsync(id, default)).NextStep);

        h.Consents.Accept(id, ConsentPolicyKind.CommunityGuidelines, "1.0");
        var done = await h.Service.GetAsync(id, default);
        Assert.Null(done.NextStep);
        Assert.Equal(RegistrationProgress.Ordered, done.Completed);
    }

    /// <summary>A page saved after consent must not report the consent as undone.</summary>
    [Fact]
    public async Task PageSavedAfterConsent_KeepsConsentDone()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        h.Photos.Count = new PhotoOptions().MaxCount;
        h.Liveness.Set(id, LivenessOutcome.Passed);
        await AnswerPromptsAndNotificationsAsync(h, id);
        h.Consents.Accept(id, ConsentPolicyKind.Terms, "1.0");
        h.Consents.Accept(id, ConsentPolicyKind.Privacy, "1.0");
        h.Consents.Accept(id, ConsentPolicyKind.CommunityGuidelines, "1.0");

        var edited = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Work: "Architect"), default);

        Assert.Null(edited.NextStep);
        Assert.Contains(RegistrationProgress.Consent, edited.Completed);
    }

    private static Task AnswerPromptsAndNotificationsAsync(Harness h, Guid id) =>
        h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(
                Prompts: [new MemberPromptAnswer("know", "Something real."), new MemberPromptAnswer("soft", "Also real.")],
                NotificationsOn: true),
            default);

    private static async Task<Guid> AnswerEveryPageAsync(Harness h)
    {
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePage(), default);
        await h.Service.PatchAsync(id, CompletePreferences(), default);
        await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(
                Lifestyle: new Dictionary<string, MemberAnswer> { ["drink"] = new("no") },
                Beliefs: new Dictionary<string, MemberAnswer> { ["faith"] = new("not-religious") },
                Vibe: ["reading"]),
            default);
        return id;
    }

    /// <summary>The whole question walk leaves the face check, photos, prompts, notifications and consent.</summary>
    [Fact]
    public async Task EveryPageAnswered_LeavesTheFaceCheckPhotosAndConsent()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, CompletePage(), default);
        await h.Service.PatchAsync(id, CompletePreferences(), default);
        var done = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(
                Lifestyle: new Dictionary<string, MemberAnswer> { ["drink"] = new("no") },
                Beliefs: new Dictionary<string, MemberAnswer> { ["faith"] = new("not-religious") },
                Vibe: ["reading"]),
            default);

        Assert.Equal(RegistrationProgress.Liveness, done.NextStep);
        Assert.Equal(
            RegistrationProgress.Ordered.Except(
                [
                    RegistrationProgress.Photos, RegistrationProgress.Liveness, RegistrationProgress.Voice,
                    RegistrationProgress.Notifications, RegistrationProgress.Consent,
                ]),
            done.Completed);
    }

    // ---- prompts, recordings, notifications and profile extras -----------------------------

    [Fact]
    public async Task Prompts_AreStoredInOrder_WithTrimmedText_AndBlankTextAsNone()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);

        var progress = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Prompts: [new("soft", "  Kindness.  "), new("know", "   ")]),
            default);

        Assert.Equal(
            [new PromptAnswerDto("soft", "Kindness.", false), new PromptAnswerDto("know", null, false)],
            progress.ProfileAnswers!.Prompts);
        // One typed answer is not enough; the blank one still needs typing or a recording.
        Assert.DoesNotContain(RegistrationProgress.Voice, progress.Completed);
    }

    /// <summary>A prompt answered only by recording counts, and the response says it has audio.</summary>
    [Fact]
    public async Task RecordedPrompts_CountAsAnswered()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Prompts: [new("know"), new("soft", "Typed.")]), default);
        h.Voice.Add(id, "know");

        var progress = await h.Service.GetAsync(id, default);

        Assert.Contains(RegistrationProgress.Voice, progress.Completed);
        Assert.True(progress.ProfileAnswers!.Prompts.Single(p => p.PromptId == "know").HasAudio);
        Assert.False(progress.ProfileAnswers.Prompts.Single(p => p.PromptId == "soft").HasAudio);
    }

    /// <summary>Dropping a prompt removes its recording; a prompt that stays keeps its own.</summary>
    [Fact]
    public async Task ChangingThePromptList_RemovesRecordingsForDroppedPrompts()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Prompts: [new("know"), new("soft")]), default);
        h.Voice.Add(id, "know");
        h.Voice.Add(id, "soft");

        var progress = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Prompts: [new("soft"), new("value", "New one.")]),
            default);

        Assert.Equal(["soft"], h.Voice.PromptIds(id));
        Assert.Equal(["soft", "value"], progress.ProfileAnswers!.Prompts.Select(p => p.PromptId));
        Assert.Contains(RegistrationProgress.Voice, progress.Completed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Notifications_EitherAnswer_SetsEverySwitch_AndFinishesTheStep(bool on)
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);

        var progress = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(NotificationsOn: on), default);

        Assert.Equal(on, progress.Settings!.NotifyIntroductions);
        Assert.Equal(on, progress.Settings.NotifyReplies);
        Assert.Equal(on, progress.Settings.NotifyWeekendSurprise);
        Assert.Equal(on, (await h.Settings.FindByUserIdAsync(id, default))!.NotifyIntroductions);
        Assert.Contains(RegistrationProgress.Notifications, progress.Completed);
    }

    /// <summary>The registration answer is a starting point; Settings changes each switch on its own.</summary>
    [Fact]
    public async Task Notifications_KeepOtherSettings_LikeHiddenFields()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);
        await h.Settings.UpsertAsync(
            Aynera.Domain.Settings.Records.MemberSettingsRecord.Empty(id) with
            {
                Visibility = new Dictionary<string, bool> { ["gender"] = false },
            },
            default);

        var progress = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(NotificationsOn: true), default);

        Assert.False(progress.Settings!.Visibility["gender"]);
        Assert.True(progress.Settings.NotifyIntroductions);
    }

    [Fact]
    public async Task DealbreakerAndRhythm_AreStored_AndAnEmptyDealbreakerClears()
    {
        var h = new Harness();
        var id = await AnswerEveryPageAsync(h);

        var saved = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(
                Dealbreaker: " I read the last page first. ",
                Rhythm: new Dictionary<string, string> { ["socialEnergy"] = "Small groups", ["weekends"] = "Slow" }),
            default);

        Assert.Equal("I read the last page first.", saved.ProfileAnswers!.Dealbreaker);
        Assert.Equal("Small groups", saved.ProfileAnswers.Rhythm["socialEnergy"]);
        // Everyday answers from the earlier page are untouched.
        Assert.Equal("no", saved.ProfileAnswers.Lifestyle["drink"].Option);

        var cleared = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Dealbreaker: ""), default);
        Assert.Null(cleared.ProfileAnswers!.Dealbreaker);
        Assert.Equal(2, cleared.ProfileAnswers.Rhythm.Count);
    }

    // ---- everyday, belief and vibe answers, pages 23 to 34 ---------------------------------

    /// <summary>
    /// Every question in these categories is optional, so there is nothing to wait for — the
    /// answers go straight to their own table rather than through the draft.
    /// </summary>
    [Fact]
    public async Task EverydayAnswers_AreStoredWithoutTouchingTheDraft()
    {
        var h = new Harness();
        var id = h.AddMember();

        var progress = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Lifestyle: new Dictionary<string, MemberAnswer>
            {
                ["drink"] = new("sometimes"),
            }),
            default);

        Assert.Equal("sometimes", progress.ProfileAnswers!.Lifestyle["drink"].Option);
        Assert.Null(await h.Drafts.FindByUserIdAsync(id, default));
        Assert.Contains(RegistrationProgress.Lifestyle, progress.Completed);
    }

    [Fact]
    public async Task AnswersInOneCategory_DoNotDisturbAnother()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Lifestyle: new Dictionary<string, MemberAnswer> { ["drink"] = new("no") }),
            default);

        var after = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Beliefs: new Dictionary<string, MemberAnswer> { ["faith"] = new("practising") }),
            default);

        Assert.Equal("no", after.ProfileAnswers!.Lifestyle["drink"].Option);
        Assert.Equal("practising", after.ProfileAnswers.Beliefs["faith"].Option);
    }

    /// <summary>
    /// A page sends its whole category, so a question it leaves out is unanswered — the way a member
    /// declines one. The other category is untouched.
    /// </summary>
    [Fact]
    public async Task EachCategoryIsReplaced_SoAClearedQuestionIsGone()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Lifestyle: new Dictionary<string, MemberAnswer>
            {
                ["drink"] = new("sometimes"),
                ["smoke"] = new("no"),
            }),
            default);

        await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Beliefs: new Dictionary<string, MemberAnswer> { ["faith"] = new("practising") }),
            default);

        var after = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Lifestyle: new Dictionary<string, MemberAnswer>
            {
                ["drink"] = new("sometimes"),
                ["diet"] = new("vegetarian"),
            }),
            default);

        Assert.Equal(["diet", "drink"], after.ProfileAnswers!.Lifestyle.Keys.Order());
        Assert.Equal("practising", after.ProfileAnswers.Beliefs["faith"].Option);

        var cleared = await h.Service.PatchAsync(
            id, new UpdateRegistrationRequest(Lifestyle: new Dictionary<string, MemberAnswer>()), default);
        Assert.Empty(cleared.ProfileAnswers!.Lifestyle);
        Assert.Single(cleared.ProfileAnswers.Beliefs);
    }

    [Fact]
    public async Task ReansweringAQuestion_ReplacesThatAnswerOnly()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Lifestyle: new Dictionary<string, MemberAnswer>
            {
                ["drink"] = new("yes"),
                ["smoke"] = new("no"),
            }),
            default);

        var after = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Lifestyle: new Dictionary<string, MemberAnswer>
            {
                ["drink"] = new("no"),
                ["smoke"] = new("no"),
            }),
            default);

        Assert.Equal("no", after.ProfileAnswers!.Lifestyle["drink"].Option);
        Assert.Equal("no", after.ProfileAnswers.Lifestyle["smoke"].Option);
    }

    /// <summary>Answering is not publishing — the flag rides with the answer.</summary>
    [Fact]
    public async Task AnAnswerCanBeKeptPrivate()
    {
        var h = new Harness();
        var id = h.AddMember();

        var progress = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(Beliefs: new Dictionary<string, MemberAnswer>
            {
                ["faith"] = new("still-figuring-it-out", Public: false),
            }),
            default);

        Assert.False(progress.ProfileAnswers!.Beliefs["faith"].Public);
    }

    /// <summary>
    /// Vibe replaces rather than merges: it is a set the member edits as a whole, and merging would
    /// make deselecting a chip impossible.
    /// </summary>
    [Fact]
    public async Task Vibe_ReplacesTheWholeSet()
    {
        var h = new Harness();
        var id = h.AddMember();

        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Vibe: ["reading", "cooking"]), default);
        var after = await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Vibe: ["reading"]), default);

        Assert.Equal(["reading"], after.ProfileAnswers!.Vibe);
    }

    /// <summary>
    /// The everyday question "movement" and the vibe group of the same name live in different
    /// categories, so they cannot collide — the bug that broke the catalog seeder, designed out.
    /// </summary>
    [Fact]
    public async Task TheSameKeyInTwoCategories_DoesNotCollide()
    {
        var h = new Harness();
        var id = h.AddMember();

        var progress = await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(
                Lifestyle: new Dictionary<string, MemberAnswer> { ["movement"] = new("most-days") },
                Vibe: ["movement"]),
            default);

        Assert.Equal("most-days", progress.ProfileAnswers!.Lifestyle["movement"].Option);
        Assert.Equal(["movement"], progress.ProfileAnswers.Vibe);
    }

    [Fact]
    public async Task AnswersAreReadBackOnResume()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(
            id,
            new UpdateRegistrationRequest(
                Lifestyle: new Dictionary<string, MemberAnswer> { ["drink"] = new("no") },
                Vibe: ["reading"]),
            default);

        var progress = await h.Service.GetAsync(id, default);

        Assert.Equal("no", progress.ProfileAnswers!.Lifestyle["drink"].Option);
        Assert.Equal(["reading"], progress.ProfileAnswers.Vibe);
    }

    [Fact]
    public async Task NoAnswersYet_LeavesTheCategoriesOutstanding()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePage(), default);
        await h.Service.PatchAsync(id, CompletePreferences(), default);

        var progress = await h.Service.GetAsync(id, default);

        Assert.Equal(RegistrationProgress.Lifestyle, progress.NextStep);
    }

    // ---- reading progress -----------------------------------------------------------------

    [Fact]
    public async Task UnverifiedEmail_ResumesAtTheEmailStep()
    {
        var h = new Harness();
        var id = h.AddMember(emailConfirmed: false);

        var progress = await h.Service.GetAsync(id, default);

        Assert.Equal(RegistrationProgress.Email, progress.NextStep);
    }

    [Fact]
    public async Task ProgressIsReadBackAfterAPartialWalk()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, new UpdateRegistrationRequest(Name: "Ada", Gender: Gender.Female), default);

        var progress = await h.Service.GetAsync(id, default);

        Assert.Equal("Ada", progress.Answers.Name);
        Assert.Equal(RegistrationProgress.Birth, progress.NextStep);
    }

    /// <summary>After promotion the profile is the answer sheet, so progress is reported from it.</summary>
    [Fact]
    public async Task AfterPromotion_ProgressIsReadFromTheProfile()
    {
        var h = new Harness();
        var id = h.AddMember();
        await h.Service.PatchAsync(id, CompletePage(), default);

        var progress = await h.Service.GetAsync(id, default);

        Assert.Equal(RegistrationProgress.Looking, progress.NextStep);
        Assert.NotNull(progress.Profile);
        Assert.Equal("Ada", progress.Profile!.Name);

        // The answers moved to the profile, so the draft no longer reports them.
        Assert.Null(progress.Answers.Name);
    }

    [Fact]
    public async Task UnknownAccount_Is404()
    {
        var h = new Harness();

        var error = await Assert.ThrowsAsync<AuthException>(
            () => h.Service.GetAsync(Guid.NewGuid(), default));

        Assert.Equal("user_not_found", error.ErrorCode);
    }

    private static UpdateRegistrationRequest CompletePage() =>
        new(Name: "Ada", Gender: Gender.Female, DateOfBirth: AdultDob, Hometown: "Pune", City: "Bangalore");

    private static UpdateRegistrationRequest CompletePreferences() =>
        new(
            InterestedIn: InterestedIn.Male,
            MinAge: 24,
            MaxAge: 32,
            Track: RelationshipTrack.Intent,
            Outcome: RelationshipOutcome.Prospect);
}

/// <summary>
/// Stands in for the preferences write path. Applies the same track/outcome check the real
/// validator does, so a test cannot store a pair the API would have refused.
/// </summary>
internal sealed class StubPreferencesService : IMemberPreferencesService
{
    private readonly FakeMemberPreferencesRepository _preferences;

    public StubPreferencesService(FakeMemberPreferencesRepository preferences) => _preferences = preferences;

    public List<UpdateMemberPreferencesRequest> Saved { get; } = [];

    public async Task<MemberPreferencesDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var record = await _preferences.FindByUserIdAsync(userId, cancellationToken);
        return record is null
            ? null
            : new MemberPreferencesDto(
                record.InterestedIn, record.MinAge, record.MaxAge, record.AgeIsFlexible, record.Track, record.Outcome);
    }

    public async Task<MemberPreferencesDto> SaveAsync(
        Guid userId,
        UpdateMemberPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        Saved.Add(request);

        if (request.Track != PreferenceRules.TrackFor(request.Outcome))
        {
            throw new AuthException(
                "track_mismatch",
                $"{request.Outcome} belongs to the {PreferenceRules.TrackFor(request.Outcome)} track.");
        }

        var saved = await _preferences.UpsertAsync(
            new MemberPreferencesRecord(
                userId,
                request.InterestedIn.ToString(),
                request.MinAge,
                request.MaxAge,
                request.AgeIsFlexible,
                request.Track.ToString(),
                request.Outcome.ToString()),
            cancellationToken);

        return new MemberPreferencesDto(
            saved.InterestedIn, saved.MinAge, saved.MaxAge, saved.AgeIsFlexible, saved.Track, saved.Outcome);
    }
}

internal sealed class FakeProfileAnswersRepository : IMemberProfileAnswersRepository
{
    private readonly Dictionary<Guid, MemberProfileAnswersRecord> _rows = new();

    public Task<MemberProfileAnswersRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault(userId));

    public Task<MemberProfileAnswersRecord> UpsertAsync(
        MemberProfileAnswersRecord answers,
        CancellationToken cancellationToken)
    {
        _rows[answers.UserId] = answers;
        return Task.FromResult(answers);
    }

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _rows.Remove(userId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeRegistrationDraftRepository : IMemberRegistrationDraftRepository
{
    private readonly Dictionary<Guid, RegistrationAnswers> _rows = new();

    public Task<RegistrationAnswers?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault(userId));

    public Task<RegistrationAnswers> UpsertAsync(
        Guid userId,
        RegistrationAnswers answers,
        CancellationToken cancellationToken)
    {
        _rows[userId] = answers;
        return Task.FromResult(answers);
    }

    public Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _rows.Remove(userId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for the one-shot registration path. Only <see cref="SaveProfileAsync"/> is reachable
/// from the draft service; the rest throw so an unintended dependency shows up as a failure rather
/// than as a silent default.
/// </summary>
internal sealed class StubRegistrationService : IRegistrationService
{
    private static readonly Guid BangaloreId = Guid.NewGuid();
    private readonly AdmissionProfileRepo _profiles;

    public StubRegistrationService(AdmissionProfileRepo profiles) => _profiles = profiles;

    public List<UpdateMemberProfileRequest> Saved { get; } = [];

    public async Task<AuthAccountDto> SaveProfileAsync(
        Guid userId,
        UpdateMemberProfileRequest request,
        CancellationToken cancellationToken)
    {
        Saved.Add(request);

        var saved = await _profiles.UpsertAsync(
            new MemberProfileRecord(
                userId,
                request.Name.Trim(),
                request.Gender.ToString(),
                request.DateOfBirth,
                request.City,
                BangaloreId,
                request.Hometown!.Trim(),
                request.Nickname,
                request.HeightCm,
                request.Work,
                request.Religion,
                request.GenderIsPublic),
            cancellationToken);

        return new AuthAccountDto(
            userId, null, true, null, true, "Member", true, false, false, false, ["Member"],
            new MemberProfileDto(
                saved.Name, saved.Gender, saved.DateOfBirth, saved.City, saved.CityId, saved.Hometown,
                saved.Nickname, saved.HeightCm, saved.Work, saved.Religion, saved.GenderIsPublic));
    }

    public Task<AuthAccountDto> RegisterAsync(CreateMemberRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RequestMemberOtpResponse> StartPhoneRegistrationAsync(
        StartPhoneRegistrationRequest request, string? clientIp, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<TokenResponse> VerifyPhoneRegistrationAsync(
        VerifyPhoneRegistrationRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RequestMemberOtpResponse> StartEmailVerificationAsync(
        Guid userId, StartEmailVerificationRequest request, string? clientIp, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AuthAccountDto> VerifyEmailCodeAsync(
        Guid userId, VerifyEmailCodeRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Only the count matters to registration progress; nothing else is called.</summary>
internal sealed class CountedPhotos : IMemberPhotoRepository
{
    public Task UpdateCaptionAsync(Guid userId, Guid photoId, string? caption, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public int Count { get; set; }

    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Count);

    public Task<IReadOnlyList<MemberPhotoRecord>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MemberPhotoRecord?> FindByIdAsync(Guid userId, Guid photoId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MemberPhotoRecord?> FindReferenceAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> NextSortOrderAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MemberPhotoRecord> AddAsync(MemberPhotoRecord photo, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SoftDeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task PromoteNextReferenceAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class ListedConsents : IMemberConsentRepository
{
    private readonly List<MemberConsentRecord> _rows = [];

    public void Accept(Guid userId, ConsentPolicyKind kind, string version) =>
        _rows.Add(new MemberConsentRecord(Guid.NewGuid(), userId, kind, version, DateTimeOffset.UtcNow));

    public Task<IReadOnlyList<MemberConsentRecord>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemberConsentRecord>>(_rows.Where(r => r.UserId == userId).ToList());

    public Task<MemberConsentRecord> AcceptAsync(MemberConsentRecord consent, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Registration progress only asks for the latest finished session.</summary>
internal sealed class LatestLiveness : ILivenessRepository
{
    private readonly Dictionary<Guid, LivenessSessionRecord> _latest = new();

    public void Set(Guid userId, LivenessOutcome outcome) =>
        _latest[userId] = new LivenessSessionRecord(
            "s-" + Guid.NewGuid().ToString("N"), userId, outcome.ToString(), 99m, 95m,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public Task<LivenessSessionRecord?> FindLatestCompletedAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_latest.TryGetValue(userId, out var record) ? record : null);

    public Task AddSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<LivenessSessionRecord?> FindSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task UpdateSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SaveSelfieAsync(Guid userId, byte[] jpeg, string faceMatchStatus, decimal? similarity, CancellationToken cancellationToken) => throw new NotSupportedException();
}
