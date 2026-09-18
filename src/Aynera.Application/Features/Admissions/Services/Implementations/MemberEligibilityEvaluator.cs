using Aynera.Application.Features.Admissions.Models;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Application.Features.Admissions.Services.Interfaces;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Responses;
using Aynera.Domain.Admissions.Validators;
using Aynera.Domain.Auth.Enums;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Admissions.Services.Implementations;

/// <summary>
/// Recomputes eligibility from current evidence on every call. Nothing here is cached: a member
/// who is deactivated, restricted, or caught by a policy-version bump stops being eligible on the
/// very next evaluation. Absent evidence never counts as satisfied.
/// </summary>
public sealed class MemberEligibilityEvaluator : IMemberEligibilityEvaluator
{
    private readonly IUserRepository _users;
    private readonly IMemberProfileRepository _profiles;
    private readonly IMemberAdmissionRepository _admissions;
    private readonly IMemberConsentRepository _consents;
    private readonly IMemberIdentityEvidenceRepository _identity;
    private readonly AdmissionOptions _options;

    public MemberEligibilityEvaluator(
        IUserRepository users,
        IMemberProfileRepository profiles,
        IMemberAdmissionRepository admissions,
        IMemberConsentRepository consents,
        IMemberIdentityEvidenceRepository identity,
        IOptions<AdmissionOptions> options)
    {
        _users = users;
        _profiles = profiles;
        _admissions = admissions;
        _consents = consents;
        _identity = identity;
        _options = options.Value;
    }

    public async Task<MemberEligibilityDto> EvaluateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var unmet = new List<string>();

        var admission = await _admissions.FindByUserIdAsync(userId, cancellationToken);
        var state = admission?.State ?? AdmissionState.Draft;
        if (state != AdmissionState.Approved)
        {
            unmet.Add(EligibilityReasons.AdmissionNotApproved);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            // Without an account there is nothing further to evaluate against.
            unmet.Add(EligibilityReasons.AccountNotFound);
            return new MemberEligibilityDto(userId, false, state.ToString(), unmet);
        }

        if (!string.Equals(user.AccountKind, nameof(AccountKind.Member), StringComparison.OrdinalIgnoreCase))
        {
            unmet.Add(EligibilityReasons.NotMember);
        }

        if (user.IsDeleted)
        {
            unmet.Add(EligibilityReasons.AccountDeleted);
        }

        if (!user.IsActive)
        {
            unmet.Add(EligibilityReasons.AccountInactive);
        }

        if (user.IsRestricted)
        {
            unmet.Add(EligibilityReasons.AccountRestricted);
        }

        if (!user.PhoneConfirmed)
        {
            unmet.Add(EligibilityReasons.PhoneUnverified);
        }

        if (!user.EmailConfirmed)
        {
            unmet.Add(EligibilityReasons.EmailUnverified);
        }

        var profile = await _profiles.FindByUserIdAsync(userId, cancellationToken);
        if (profile is null)
        {
            unmet.Add(EligibilityReasons.ProfileMissing);
        }
        else if (!AdmissionValidation.IsAtLeastMinimumAge(profile.DateOfBirth, Today))
        {
            unmet.Add(EligibilityReasons.Underage);
        }

        var accepted = await _consents.ListByUserIdAsync(userId, cancellationToken);
        foreach (var (policyKind, requiredVersion) in RequiredConsents())
        {
            var satisfied = accepted.Any(c =>
                c.PolicyKind == policyKind
                && string.Equals(c.Version, requiredVersion, StringComparison.OrdinalIgnoreCase));

            if (!satisfied)
            {
                unmet.Add(EligibilityReasons.ConsentMissing(policyKind.ToString()));
            }
        }

        if (await _identity.HasRejectedFaceMatchAsync(userId, cancellationToken))
        {
            unmet.Add(EligibilityReasons.IdentityRejected);
        }

        return new MemberEligibilityDto(userId, unmet.Count == 0, state.ToString(), unmet);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// Configured requirements, ignoring any entry whose key is not a known policy document so a
    /// configuration typo cannot silently become an unsatisfiable requirement.
    /// </summary>
    private IEnumerable<(ConsentPolicyKind PolicyKind, string Version)> RequiredConsents()
    {
        foreach (var (key, version) in _options.RequiredConsentVersions)
        {
            if (!string.IsNullOrWhiteSpace(version)
                && AdmissionValidation.TryParseConsentKind(key, out var kind))
            {
                yield return (kind, version.Trim());
            }
        }
    }
}
