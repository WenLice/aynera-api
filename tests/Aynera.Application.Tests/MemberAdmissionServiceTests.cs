using Aynera.Application.Common;
using Aynera.Application.Features.Admissions.Models;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Application.Features.Admissions.Services.Implementations;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Exceptions;
using Aynera.Domain.Admissions.Records;
using Aynera.Domain.Admissions.Requests;
using Aynera.Domain.Admissions.Validators;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Records;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Tests;

public class MemberAdmissionServiceTests
{
    private static readonly DateOnly AdultDob = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25);

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public AdmissionProfileRepo Profiles { get; } = new();
        public AdmissionRepo Admissions { get; } = new();
        public ConsentRepo Consents { get; } = new();
        public IdentityEvidenceRepo Identity { get; } = new();
        public CapturingAuditWriter Audit { get; } = new();
        public AdmissionOptions Options { get; } = new();
        public static readonly Guid DelhiId = Guid.NewGuid();
        public MemberEligibilityEvaluator Evaluator { get; }
        public MemberAdmissionService Service { get; }

        public Harness()
        {
            Evaluator = new MemberEligibilityEvaluator(
                Users, Profiles, Admissions, Consents, Identity, Microsoft.Extensions.Options.Options.Create(Options));
            Service = new MemberAdmissionService(
                Admissions, Consents, Profiles, Users, Evaluator, new PassThroughTransaction(),
                Audit, DiscardLogger<MemberAdmissionService>.Instance);
        }

        public Guid AddMember(
            bool active = true, bool restricted = false, bool deleted = false,
            bool phoneConfirmed = true, bool emailConfirmed = true, bool withProfile = true, DateOnly? dob = null)
        {
            var id = Guid.NewGuid();
            Users.Add(new UserRecord(id, "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999), phoneConfirmed,
                $"{id:N}@example.test", emailConfirmed, "Member", active, deleted, false, restricted, ["Member"]));
            if (withProfile)
            {
                Profiles.Set(new MemberProfileRecord(id, "Asha", "Rao", "Female", dob ?? AdultDob, "Delhi", null, DelhiId));
            }

            return id;
        }

        public Guid AddAdmin()
        {
            var id = Guid.NewGuid();
            Users.Add(new UserRecord(id, "+919000000001", true, null, false, "Admin", true, false, false, false, ["Admin"]));
            return id;
        }

        public async Task AcceptAllConsentsAsync(Guid userId)
        {
            foreach (var kind in Enum.GetValues<ConsentPolicyKind>())
            {
                await Service.AcceptConsentAsync(userId, new AcceptConsentRequest(kind.ToString(), "1.0"), CancellationToken.None);
            }
        }

        public Task Decide(Guid userId, string decision, string? reason = null) =>
            Service.DecideAsync(userId, new AdmissionDecisionRequest(decision, reason, null), AddAdmin(), CancellationToken.None);
    }

    // ---------- Submit ----------

    [Fact]
    public async Task Get_MemberWithNoRow_ReadsAsDraftAndNotEligible()
    {
        var h = new Harness();
        var member = h.AddMember();

        var dto = await h.Service.GetAsync(member, CancellationToken.None);

        Assert.Equal("Draft", dto.State);
        Assert.False(dto.Eligibility.IsEligible);
        Assert.Contains(EligibilityReasons.AdmissionNotApproved, dto.Eligibility.UnmetRequirements);
        Assert.Null(await h.Admissions.FindByUserIdAsync(member, CancellationToken.None)); // reading does not create a row
    }

    [Fact]
    public async Task Submit_FromDraft_MovesToSubmittedAndAudits()
    {
        var h = new Harness();
        var member = h.AddMember();

        var dto = await h.Service.SubmitAsync(member, CancellationToken.None);

        Assert.Equal("Submitted", dto.State);
        Assert.NotNull(dto.SubmittedAtUtc);
        var audit = Assert.Single(h.Audit.Events, e => e.Action == AuditActions.AdmissionSubmitted);
        Assert.Equal(member, audit.UserId);
        Assert.Equal(AuditSubjectTypes.MemberAdmission, audit.SubjectType);
    }

    [Theory]
    [InlineData("Submitted", "admission_already_submitted")]
    [InlineData("InReview", "admission_already_submitted")]
    [InlineData("Approved", "admission_already_approved")]
    public async Task Submit_WhenNotDraftOrRejected_Throws409(string state, string expectedCode)
    {
        var h = new Harness();
        var member = h.AddMember();
        await h.Service.SubmitAsync(member, CancellationToken.None);
        if (state == "InReview") await h.Decide(member, "StartReview");
        if (state == "Approved") await h.Decide(member, "Approve");

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Service.SubmitAsync(member, CancellationToken.None));

        Assert.Equal(expectedCode, ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_AfterRejection_ResubmitsAndClearsDecisionButKeepsReasonInAudit()
    {
        var h = new Harness();
        var member = h.AddMember();
        await h.Service.SubmitAsync(member, CancellationToken.None);
        await h.Decide(member, "Reject", "Photos do not match the intro video.");
        h.Audit.Events.Clear();

        var dto = await h.Service.SubmitAsync(member, CancellationToken.None);

        Assert.Equal("Submitted", dto.State);
        Assert.Null(dto.DecidedAtUtc);
        Assert.Null(dto.DecidedByUserId);
        Assert.Null(dto.DecisionReason);

        var audit = Assert.Single(h.Audit.Events, e => e.Action == AuditActions.AdmissionSubmitted);
        Assert.Contains("resubmitted", audit.Message, StringComparison.OrdinalIgnoreCase);
        var reasonChange = audit.Changes!["reason"];
        Assert.Equal("Photos do not match the intro video.", reasonChange.Old?.ToString());
        Assert.Null(reasonChange.New);
    }

    [Fact]
    public async Task Submit_WithoutProfile_Throws()
    {
        var h = new Harness();
        var member = h.AddMember(withProfile: false);

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Service.SubmitAsync(member, CancellationToken.None));

        Assert.Equal("admission_profile_required", ex.ErrorCode);
    }

    [Fact]
    public async Task Submit_Underage_Throws()
    {
        var h = new Harness();
        var member = h.AddMember(dob: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18).AddDays(1));

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Service.SubmitAsync(member, CancellationToken.None));

        Assert.Equal("admission_underage", ex.ErrorCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Submit_InactiveOrRestrictedAccount_Throws(bool active, bool restricted)
    {
        var h = new Harness();
        var member = h.AddMember(active: active, restricted: restricted);

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Service.SubmitAsync(member, CancellationToken.None));

        Assert.Equal("admission_account_unavailable", ex.ErrorCode);
    }

    [Fact]
    public async Task Submit_DeletedAccount_ReadsAsNotFound()
    {
        // Soft-deleted users are filtered out of every lookup, so they surface as 404 rather than a state error.
        var h = new Harness();
        var member = h.AddMember(deleted: true);

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Service.SubmitAsync(member, CancellationToken.None));

        Assert.Equal("admission_member_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Submit_AdminAccount_Is404()
    {
        var h = new Harness();
        var admin = h.AddAdmin();

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Service.SubmitAsync(admin, CancellationToken.None));

        Assert.Equal("admission_member_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    // ---------- Decide ----------

    [Fact]
    public async Task Decide_FullReviewPath_StampsDecisionOnlyOnTerminalStates()
    {
        var h = new Harness();
        var member = h.AddMember();
        var admin = h.AddAdmin();
        await h.Service.SubmitAsync(member, CancellationToken.None);

        var inReview = await h.Service.DecideAsync(member, new AdmissionDecisionRequest("StartReview", null, "Claimed"), admin, CancellationToken.None);
        Assert.Equal("InReview", inReview.State);
        Assert.Null(inReview.DecidedAtUtc);
        Assert.Equal("Claimed", inReview.ReviewNote);

        var approved = await h.Service.DecideAsync(member, new AdmissionDecisionRequest("Approve", null, null), admin, CancellationToken.None);
        Assert.Equal("Approved", approved.State);
        Assert.NotNull(approved.DecidedAtUtc);
        Assert.Equal(admin, approved.DecidedByUserId);
        Assert.Equal("Claimed", approved.ReviewNote); // preserved when not supplied

        var reopened = await h.Service.DecideAsync(member, new AdmissionDecisionRequest("Reopen", null, null), admin, CancellationToken.None);
        Assert.Equal("InReview", reopened.State);
        Assert.Null(reopened.DecidedAtUtc); // no stale approval timestamp
        Assert.Null(reopened.DecidedByUserId);

        Assert.Equal(
            [AuditActions.AdmissionSubmitted, AuditActions.AdmissionReviewStarted, AuditActions.AdmissionApproved, AuditActions.AdmissionReopened],
            h.Audit.Events.Select(e => e.Action).ToArray());
    }

    [Fact]
    public async Task Decide_RejectWithoutReason_Throws()
    {
        var h = new Harness();
        var member = h.AddMember();
        await h.Service.SubmitAsync(member, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Decide(member, "Reject", "  "));

        Assert.Equal("validation_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task Decide_RejectRecordsReasonAndAuditsIt()
    {
        var h = new Harness();
        var member = h.AddMember();
        await h.Service.SubmitAsync(member, CancellationToken.None);

        await h.Decide(member, "Reject", "  Incomplete profile.  ");

        var dto = await h.Service.GetAsync(member, CancellationToken.None);
        Assert.Equal("Rejected", dto.State);
        Assert.Equal("Incomplete profile.", dto.DecisionReason);
        var audit = Assert.Single(h.Audit.Events, e => e.Action == AuditActions.AdmissionRejected);
        Assert.Equal("Incomplete profile.", audit.Changes!["reason"].New?.ToString());
    }

    [Fact]
    public async Task Decide_InvalidTransition_Is409AndWritesNoAudit()
    {
        var h = new Harness();
        var member = h.AddMember(); // Draft: no staff edge is valid

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Decide(member, "Approve"));

        Assert.Equal("admission_transition_invalid", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
        Assert.Empty(h.Audit.Events);
        Assert.Null(await h.Admissions.FindByUserIdAsync(member, CancellationToken.None));
    }

    [Fact]
    public async Task Decide_UnknownDecision_Throws()
    {
        var h = new Harness();
        var member = h.AddMember();

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Decide(member, "Ban"));

        Assert.Equal("validation_failed", ex.ErrorCode);
    }

    // ---------- Consents ----------

    [Fact]
    public async Task AcceptConsent_IsIdempotentPerVersion_AndUnknownKindFails()
    {
        var h = new Harness();
        var member = h.AddMember();

        await h.Service.AcceptConsentAsync(member, new AcceptConsentRequest("terms", "1.0"), CancellationToken.None);
        var dto = await h.Service.AcceptConsentAsync(member, new AcceptConsentRequest("Terms", "1.0"), CancellationToken.None);

        Assert.Single(dto.Consents);
        Assert.Equal("Terms", dto.Consents[0].PolicyKind);

        var ex = await Assert.ThrowsAsync<AdmissionException>(() =>
            h.Service.AcceptConsentAsync(member, new AcceptConsentRequest("MatchmakingDataUse", "1.0"), CancellationToken.None));
        Assert.Equal("validation_failed", ex.ErrorCode);
    }

    // ---------- Eligibility ----------

    [Fact]
    public async Task Eligibility_ApprovedWithAllEvidence_IsEligible()
    {
        var h = new Harness();
        var member = h.AddMember();
        await h.AcceptAllConsentsAsync(member);
        await h.Service.SubmitAsync(member, CancellationToken.None);
        await h.Decide(member, "Approve");

        var result = await h.Evaluator.EvaluateAsync(member, CancellationToken.None);

        Assert.True(result.IsEligible);
        Assert.Empty(result.UnmetRequirements);
        Assert.Equal("Approved", result.AdmissionState);
    }

    [Fact]
    public async Task Eligibility_ReportsEveryUnmetRequirement_NotJustTheFirst()
    {
        var h = new Harness();
        var member = h.AddMember(active: false, restricted: true, phoneConfirmed: false, emailConfirmed: false, withProfile: false);
        h.Identity.Rejected.Add(member);

        var result = await h.Evaluator.EvaluateAsync(member, CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal(
            new HashSet<string>
            {
                EligibilityReasons.AdmissionNotApproved,
                EligibilityReasons.AccountInactive,
                EligibilityReasons.AccountRestricted,
                EligibilityReasons.PhoneUnverified,
                EligibilityReasons.EmailUnverified,
                EligibilityReasons.ProfileMissing,
                EligibilityReasons.IdentityRejected,
                EligibilityReasons.ConsentMissing("Terms"),
                EligibilityReasons.ConsentMissing("Privacy"),
                EligibilityReasons.ConsentMissing("CommunityGuidelines")
            },
            result.UnmetRequirements.ToHashSet());
    }

    [Fact]
    public async Task Eligibility_ApprovalIsNotCached_DeactivationRevokesImmediately()
    {
        var h = new Harness();
        var member = h.AddMember();
        await h.AcceptAllConsentsAsync(member);
        await h.Service.SubmitAsync(member, CancellationToken.None);
        await h.Decide(member, "Approve");
        Assert.True((await h.Evaluator.EvaluateAsync(member, CancellationToken.None)).IsEligible);

        var user = (await h.Users.FindByIdAsync(member, CancellationToken.None))!;
        h.Users.Add(user with { IsActive = false });

        var result = await h.Evaluator.EvaluateAsync(member, CancellationToken.None);
        Assert.False(result.IsEligible);
        Assert.Equal([EligibilityReasons.AccountInactive], result.UnmetRequirements);
    }

    [Fact]
    public async Task Eligibility_PolicyVersionBump_RegatesUntilReaccepted()
    {
        var h = new Harness();
        var member = h.AddMember();
        await h.AcceptAllConsentsAsync(member);
        await h.Service.SubmitAsync(member, CancellationToken.None);
        await h.Decide(member, "Approve");

        h.Options.RequiredConsentVersions["Privacy"] = "2.0";

        var regated = await h.Evaluator.EvaluateAsync(member, CancellationToken.None);
        Assert.False(regated.IsEligible);
        Assert.Equal([EligibilityReasons.ConsentMissing("Privacy")], regated.UnmetRequirements);

        await h.Service.AcceptConsentAsync(member, new AcceptConsentRequest("Privacy", "2.0"), CancellationToken.None);

        var restored = await h.Evaluator.EvaluateAsync(member, CancellationToken.None);
        Assert.True(restored.IsEligible);
        Assert.Equal(4, (await h.Consents.ListByUserIdAsync(member, CancellationToken.None)).Count); // old row retained
    }

    [Fact]
    public async Task Eligibility_UnderageMember_IsBlockedEvenIfApproved()
    {
        var h = new Harness();
        var member = h.AddMember(dob: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-17));
        await h.AcceptAllConsentsAsync(member);
        // Force an approved row past the submit gate to prove the evaluator checks age independently.
        await h.Admissions.UpsertAsync(new MemberAdmissionRecord(member, AdmissionState.Approved, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, Guid.NewGuid(), null, null, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var result = await h.Evaluator.EvaluateAsync(member, CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal([EligibilityReasons.Underage], result.UnmetRequirements);
    }

    [Fact]
    public async Task Eligibility_UnknownUser_StopsAtAccountNotFound()
    {
        var h = new Harness();

        var result = await h.Evaluator.EvaluateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal([EligibilityReasons.AdmissionNotApproved, EligibilityReasons.AccountNotFound], result.UnmetRequirements);
    }

    [Fact]
    public async Task Eligibility_IgnoresUnknownConfiguredPolicyKeys()
    {
        var h = new Harness();
        h.Options.RequiredConsentVersions["Newsletterr"] = "1.0"; // typo must not become an unsatisfiable gate
        var member = h.AddMember();
        await h.AcceptAllConsentsAsync(member);
        await h.Service.SubmitAsync(member, CancellationToken.None);
        await h.Decide(member, "Approve");

        Assert.True((await h.Evaluator.EvaluateAsync(member, CancellationToken.None)).IsEligible);
    }

    // ---------- List ----------

    [Fact]
    public async Task List_FiltersByStateAndOrdersOldestSubmissionFirst()
    {
        var h = new Harness();
        var first = h.AddMember();
        var second = h.AddMember();
        var approved = h.AddMember();
        await h.Service.SubmitAsync(first, CancellationToken.None);
        await Task.Delay(5);
        await h.Service.SubmitAsync(second, CancellationToken.None);
        await h.Service.SubmitAsync(approved, CancellationToken.None);
        await h.Decide(approved, "Approve");

        var submitted = await h.Service.ListAsync("submitted", 1, 10, CancellationToken.None);
        Assert.Equal(2, submitted.TotalCount);
        Assert.Equal([first, second], submitted.Items.Select(i => i.UserId).ToArray());

        var all = await h.Service.ListAsync(null, 1, 10, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);

        var ex = await Assert.ThrowsAsync<AdmissionException>(() => h.Service.ListAsync("Pending", 1, 10, CancellationToken.None));
        Assert.Equal("validation_failed", ex.ErrorCode);
    }
}

// ---------- fakes ----------

internal sealed class PassThroughTransaction : IWorkflowTransaction
{
    public Task<T> ExecuteAsync<T>(IReadOnlyList<string> lockKeys, Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) => operation(cancellationToken);
}

internal sealed class AdmissionProfileRepo : IMemberProfileRepository
{
    private readonly Dictionary<Guid, MemberProfileRecord> _profiles = new();

    public void Set(MemberProfileRecord profile) => _profiles[profile.UserId] = profile;

    public Task<MemberProfileRecord> CreateAsync(MemberProfileRecord profile, CancellationToken cancellationToken)
    {
        Set(profile);
        return Task.FromResult(profile);
    }

    public Task<MemberProfileRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_profiles.GetValueOrDefault(userId));

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _profiles.Remove(userId);
        return Task.CompletedTask;
    }
}

internal sealed class AdmissionRepo : IMemberAdmissionRepository
{
    private readonly Dictionary<Guid, MemberAdmissionRecord> _rows = new();

    public Task<MemberAdmissionRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault(userId));

    public Task<MemberAdmissionRecord> UpsertAsync(MemberAdmissionRecord admission, CancellationToken cancellationToken)
    {
        var created = _rows.TryGetValue(admission.UserId, out var existing) ? existing.CreatedAtUtc : admission.CreatedAtUtc;
        var saved = admission with { CreatedAtUtc = created };
        _rows[admission.UserId] = saved;
        return Task.FromResult(saved);
    }

    public Task<(IReadOnlyList<MemberAdmissionRecord> Items, int TotalCount)> ListPageAsync(
        AdmissionState? state, int skip, int take, CancellationToken cancellationToken)
    {
        var query = _rows.Values.Where(r => state is null || r.State == state).ToList();
        var page = query
            .OrderBy(r => r.SubmittedAtUtc ?? r.CreatedAtUtc)
            .ThenBy(r => r.UserId)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult(((IReadOnlyList<MemberAdmissionRecord>)page, query.Count));
    }
}

internal sealed class ConsentRepo : IMemberConsentRepository
{
    private readonly List<MemberConsentRecord> _rows = [];

    public Task<IReadOnlyList<MemberConsentRecord>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult((IReadOnlyList<MemberConsentRecord>)_rows.Where(r => r.UserId == userId).ToList());

    public Task<MemberConsentRecord> AcceptAsync(MemberConsentRecord consent, CancellationToken cancellationToken)
    {
        var existing = _rows.FirstOrDefault(r =>
            r.UserId == consent.UserId && r.PolicyKind == consent.PolicyKind && r.Version == consent.Version);
        if (existing is not null)
        {
            return Task.FromResult(existing);
        }

        _rows.Add(consent);
        return Task.FromResult(consent);
    }
}

internal sealed class IdentityEvidenceRepo : IMemberIdentityEvidenceRepository
{
    public HashSet<Guid> Rejected { get; } = [];

    public Task<bool> HasRejectedFaceMatchAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rejected.Contains(userId));
}
