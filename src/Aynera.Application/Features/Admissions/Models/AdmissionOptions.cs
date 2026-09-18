namespace Aynera.Application.Features.Admissions.Models;

public sealed class AdmissionOptions
{
    public const string SectionName = "Aynera:Admission";

    /// <summary>
    /// Policy document -> version a member must currently have accepted. Raising a version here
    /// immediately makes every member who accepted only the older version ineligible until they
    /// re-accept; it does not delete their earlier acceptance.
    /// </summary>
    public Dictionary<string, string> RequiredConsentVersions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Terms"] = "1.0",
            ["Privacy"] = "1.0",
            ["CommunityGuidelines"] = "1.0"
        };
}
