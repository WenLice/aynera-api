using Aynera.Application.Common;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Application.Features.Admissions.Services.Interfaces;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Exceptions;
using Aynera.Domain.Admissions.Records;
using Aynera.Domain.Admissions.Requests;
using Aynera.Domain.Admissions.Responses;
using Aynera.Domain.Admissions.Validators;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Aynera.Application.Features.Admissions.Services.Implementations;

public sealed class MemberAdmissionService : IMemberAdmissionService
{
    private const int MaxPageSize = 100;

    private readonly IMemberAdmissionRepository _admissions;
    private readonly IMemberConsentRepository _consents;
    private readonly IMemberProfileRepository _profiles;
    private readonly IUserRepository _users;
    private readonly IMemberEligibilityEvaluator _eligibility;
    private readonly IWorkflowTransaction _transaction;
    private readonly IAuditWriter _audit;
    private readonly ILogger<MemberAdmissionService> _logger;

    public MemberAdmissionService(
        IMemberAdmissionRepository admissions,
        IMemberConsentRepository consents,
        IMemberProfileRepository profiles,
        IUserRepository users,
        IMemberEligibilityEvaluator eligibility,
        IWorkflowTransaction transaction,
        IAuditWriter audit,
        ILogger<MemberAdmissionService> logger)
    {
        _admissions = admissions;
        _consents = consents;
        _profiles = profiles;
        _users = users;
        _eligibility = eligibility;
        _transaction = transaction;
        _audit = audit;
        _logger = logger;
    }

    public async Task<MemberAdmissionDto> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(userId, cancellationToken);
        var admission = await _admissions.FindByUserIdAsync(userId, cancellationToken) ?? Draft(userId);

        return await ToDtoAsync(admission, cancellationToken);
    }

    public async Task<MemberAdmissionDto> SubmitAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SubmitAdmission {UserId}", userId);

        // State is re-read under the account lock so a concurrent staff decision and a member
        // submit cannot both act on the same stale row.
        var (saved, previous) = await _transaction.ExecuteAsync(AccountLock(userId), async ct =>
        {
            var user = await RequireMemberAsync(userId, ct);

            if (user.IsDeleted || !user.IsActive || user.IsRestricted)
            {
                throw new AdmissionException(
                    "admission_account_unavailable",
                    "This account cannot submit an admission in its current state.");
            }

            // A review with nothing to review wastes staff time, so the profile gate is enforced here
            // rather than left to the eligibility verdict.
            var profile = await _profiles.FindByUserIdAsync(userId, ct)
                ?? throw new AdmissionException(
                    "admission_profile_required",
                    "Complete your profile before submitting for review.");

            if (!AdmissionValidation.IsAtLeastMinimumAge(profile.DateOfBirth, DateOnly.FromDateTime(DateTime.UtcNow)))
            {
                throw new AdmissionException(
                    "admission_underage",
                    $"Members must be at least {AdmissionValidation.MinimumAgeYears} years old.");
            }

            var existing = await _admissions.FindByUserIdAsync(userId, ct) ?? Draft(userId);

            if (!AdmissionValidation.CanSubmit(existing.State))
            {
                throw new AdmissionException(
                    existing.State == AdmissionState.Approved
                        ? "admission_already_approved"
                        : "admission_already_submitted",
                    $"Admission is already {existing.State}.",
                    409);
            }

            var now = DateTimeOffset.UtcNow;
            var updated = await _admissions.UpsertAsync(
                existing with
                {
                    State = AdmissionState.Submitted,
                    SubmittedAtUtc = now,
                    // A resubmission starts a fresh decision; the prior rejection stays in the audit trail.
                    DecidedAtUtc = null,
                    DecidedByUserId = null,
                    DecisionReason = null,
                    UpdatedAtUtc = now
                },
                ct);

            return (updated, existing);
        }, cancellationToken);

        await WriteAuditAsync(
            AuditActions.AdmissionSubmitted,
            userId,
            actorUserId: userId,
            previous.State == AdmissionState.Rejected
                ? "Admission resubmitted for review after rejection."
                : "Admission submitted for review.",
            previous.State,
            saved.State,
            cancellationToken,
            previousReason: previous.DecisionReason);

        return await ToDtoAsync(saved, cancellationToken);
    }

    public async Task<MemberAdmissionDto> DecideAsync(
        Guid userId,
        AdmissionDecisionRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("DecideAdmission {UserId} {Decision}", userId, request.Decision);

        if (!AdmissionValidation.TryParseDecision(request.Decision, out var decision))
        {
            throw new AdmissionException(
                "validation_failed",
                "Decision must be one of: StartReview, Approve, Reject, Reopen.");
        }

        var reason = Normalize(request.Reason);
        if (decision == AdmissionDecision.Reject && reason is null)
        {
            throw new AdmissionException(
                "validation_failed",
                "A reason is required when rejecting an admission.");
        }

        // Two staff deciding at once must produce exactly one transition: the second re-reads
        // the already-decided row under the lock and is refused by the transition table.
        var (saved, previous) = await _transaction.ExecuteAsync(AccountLock(userId), async ct =>
        {
            await RequireMemberAsync(userId, ct);

            var existing = await _admissions.FindByUserIdAsync(userId, ct) ?? Draft(userId);

            if (!AdmissionValidation.TryTransition(existing.State, decision, out var next))
            {
                throw new AdmissionException(
                    "admission_transition_invalid",
                    $"Cannot {decision} an admission that is {existing.State}.",
                    409);
            }

            var now = DateTimeOffset.UtcNow;
            var isFinal = next is AdmissionState.Approved or AdmissionState.Rejected;

            var updated = await _admissions.UpsertAsync(
                existing with
                {
                    State = next,
                    // Only a terminal decision stamps the decision fields; claiming or reopening a
                    // review must not leave a stale approval timestamp behind.
                    DecidedAtUtc = isFinal ? now : null,
                    DecidedByUserId = isFinal ? actorUserId : null,
                    DecisionReason = isFinal ? reason : null,
                    ReviewNote = Normalize(request.ReviewNote) ?? existing.ReviewNote,
                    UpdatedAtUtc = now
                },
                ct);

            return (updated, existing);
        }, cancellationToken);

        await WriteAuditAsync(
            decision switch
            {
                AdmissionDecision.StartReview => AuditActions.AdmissionReviewStarted,
                AdmissionDecision.Approve => AuditActions.AdmissionApproved,
                AdmissionDecision.Reject => AuditActions.AdmissionRejected,
                _ => AuditActions.AdmissionReopened
            },
            userId,
            actorUserId,
            $"Admission {saved.State} by staff.",
            previous.State,
            saved.State,
            cancellationToken,
            reason);

        return await ToDtoAsync(saved, cancellationToken);
    }

    public async Task<MemberAdmissionDto> AcceptConsentAsync(
        Guid userId,
        AcceptConsentRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("AcceptConsent {UserId} {PolicyKind}", userId, request.PolicyKind);

        if (!AdmissionValidation.TryParseConsentKind(request.PolicyKind, out var kind))
        {
            throw new AdmissionException(
                "validation_failed",
                "Policy kind must be one of: Terms, Privacy, CommunityGuidelines.");
        }

        var version = Normalize(request.Version)
            ?? throw new AdmissionException("validation_failed", "Policy version is required.");

        // Under the account lock a double-submit of the same version resolves to the idempotent
        // path instead of racing the unique index.
        await _transaction.ExecuteAsync(AccountLock(userId), async ct =>
        {
            await RequireMemberAsync(userId, ct);

            return await _consents.AcceptAsync(
                new MemberConsentRecord(
                    Id: Guid.NewGuid(),
                    UserId: userId,
                    PolicyKind: kind,
                    Version: version,
                    AcceptedAtUtc: DateTimeOffset.UtcNow),
                ct);
        }, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberConsentAccepted,
                UserId: userId,
                Outcome: AuditOutcomes.Success,
                Message: $"Consent '{kind}' accepted at version {version}.",
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.MemberAdmission,
                SubjectId: userId.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("policyKind", null, kind.ToString()),
                    ("version", null, version)
                ])),
            cancellationToken);

        var admission = await _admissions.FindByUserIdAsync(userId, cancellationToken) ?? Draft(userId);
        return await ToDtoAsync(admission, cancellationToken);
    }

    public async Task<PagedResult<MemberAdmissionSummaryDto>> ListAsync(
        string? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        AdmissionState? filter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!AdmissionValidation.TryParseState(state, out var parsed))
            {
                throw new AdmissionException(
                    "validation_failed",
                    "State must be one of: Draft, Submitted, InReview, Approved, Rejected.");
            }

            filter = parsed;
        }

        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize is < 1 or > MaxPageSize ? 25 : pageSize;

        var (items, total) = await _admissions.ListPageAsync(
            filter, (safePage - 1) * safeSize, safeSize, cancellationToken);

        return new PagedResult<MemberAdmissionSummaryDto>(
            items.Select(ToSummaryDto).ToList(),
            safePage,
            safeSize,
            total);
    }

    private static string[] AccountLock(Guid userId) => [$"account:{userId:D}"];

    /// <summary>
    /// Members that have never been submitted have no row yet; this is the value they read as,
    /// and the value a first write is based on.
    /// </summary>
    private static MemberAdmissionRecord Draft(Guid userId) =>
        new(
            UserId: userId,
            State: AdmissionState.Draft,
            SubmittedAtUtc: null,
            DecidedAtUtc: null,
            DecidedByUserId: null,
            DecisionReason: null,
            ReviewNote: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: null);

    private async Task<Domain.Auth.Records.UserRecord> RequireMemberAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken)
            ?? throw new AdmissionException("admission_member_not_found", "Member was not found.", 404);

        if (!string.Equals(user.AccountKind, nameof(AccountKind.Member), StringComparison.OrdinalIgnoreCase))
        {
            throw new AdmissionException("admission_member_not_found", "Member was not found.", 404);
        }

        return user;
    }

    private async Task<MemberAdmissionDto> ToDtoAsync(
        MemberAdmissionRecord admission,
        CancellationToken cancellationToken)
    {
        var consents = await _consents.ListByUserIdAsync(admission.UserId, cancellationToken);
        var eligibility = await _eligibility.EvaluateAsync(admission.UserId, cancellationToken);

        return new MemberAdmissionDto(
            admission.UserId,
            admission.State.ToString(),
            admission.SubmittedAtUtc,
            admission.DecidedAtUtc,
            admission.DecidedByUserId,
            admission.DecisionReason,
            admission.ReviewNote,
            admission.CreatedAtUtc,
            admission.UpdatedAtUtc,
            consents
                .OrderBy(c => c.PolicyKind)
                .ThenBy(c => c.Version)
                .Select(c => new MemberConsentDto(c.Id, c.PolicyKind.ToString(), c.Version, c.AcceptedAtUtc))
                .ToList(),
            eligibility);
    }

    private static MemberAdmissionSummaryDto ToSummaryDto(MemberAdmissionRecord a) =>
        new(a.UserId, a.State.ToString(), a.SubmittedAtUtc, a.DecidedAtUtc, a.DecidedByUserId, a.CreatedAtUtc);

    private Task WriteAuditAsync(
        string action,
        Guid subjectUserId,
        Guid actorUserId,
        string message,
        AdmissionState from,
        AdmissionState to,
        CancellationToken cancellationToken,
        string? reason = null,
        string? previousReason = null)
    {
        var changes = new List<(string, object?, object?)>
        {
            ("state", from.ToString(), to.ToString())
        };

        if (reason is not null || previousReason is not null)
        {
            changes.Add(("reason", previousReason, reason));
        }

        return _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: action,
                UserId: actorUserId,
                Outcome: AuditOutcomes.Success,
                Message: message,
                SubjectUserId: subjectUserId,
                SubjectType: AuditSubjectTypes.MemberAdmission,
                SubjectId: subjectUserId.ToString("D"),
                Changes: AuditChanges.Create(changes)),
            cancellationToken);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
