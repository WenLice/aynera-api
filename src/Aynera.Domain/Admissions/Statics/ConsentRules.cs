using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Records;
using Aynera.Domain.Admissions.Validators;

namespace Aynera.Domain.Admissions.Statics;

/// <summary>
/// Which policy documents a member still has to accept. The one home of the rule, shared by the
/// eligibility evaluator and registration progress, so the two can never disagree about whether a
/// member has agreed.
/// </summary>
public static class ConsentRules
{
    /// <summary>
    /// The configured requirements, ignoring any entry whose key is not a known policy document so a
    /// configuration typo cannot silently become an unsatisfiable requirement.
    /// </summary>
    public static IEnumerable<(ConsentPolicyKind PolicyKind, string Version)> Required(
        IReadOnlyDictionary<string, string> requiredVersions)
    {
        foreach (var (key, version) in requiredVersions)
        {
            if (!string.IsNullOrWhiteSpace(version)
                && AdmissionValidation.TryParseConsentKind(key, out var kind))
            {
                yield return (kind, version.Trim());
            }
        }
    }

    /// <summary>Documents not yet accepted at their current version. Empty means the member has agreed.</summary>
    public static IReadOnlyList<ConsentPolicyKind> Missing(
        IReadOnlyDictionary<string, string> requiredVersions,
        IEnumerable<MemberConsentRecord> accepted)
    {
        var acceptedList = accepted as IReadOnlyCollection<MemberConsentRecord> ?? accepted.ToList();
        return Required(requiredVersions)
            .Where(required => !acceptedList.Any(c =>
                c.PolicyKind == required.PolicyKind
                && string.Equals(c.Version, required.Version, StringComparison.OrdinalIgnoreCase)))
            .Select(required => required.PolicyKind)
            .ToList();
    }
}
