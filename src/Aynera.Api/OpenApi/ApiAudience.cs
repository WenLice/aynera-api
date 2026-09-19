using Aynera.Domain.Auth.Statics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace Aynera.Api.OpenApi;

/// <summary>
/// Splits the OpenAPI output into an admin document and a member document. The audience is derived
/// from each action's authorization policy, so the docs cannot drift from what the backend enforces.
/// An explicit <c>[ApiExplorerSettings(GroupName = ...)]</c> overrides the derivation; it is only
/// needed for anonymous endpoints that belong to the admin site (admin login).
/// </summary>
public static class ApiAudience
{
    public const string Admin = "admin";
    public const string Member = "member";

    /// <summary>
    /// A combined document holding every endpoint regardless of audience. It is a browsing and manual-testing
    /// aid only: <see cref="Of"/> still classifies each action by policy, and the member/admin documents are
    /// still the ones that describe what each front end may call.
    /// </summary>
    public const string All = "all";

    public static string Of(ApiDescription api)
    {
        if (!string.IsNullOrEmpty(api.GroupName))
        {
            return api.GroupName;
        }

        var requiresAdmin = api.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any(data => data.Policy is AuthPolicies.Admin or AuthPolicies.SuperAdmin);

        return requiresAdmin ? Admin : Member;
    }

    /// <summary>
    /// Whether an action belongs in the named OpenAPI document. <see cref="All"/> takes everything;
    /// the audience documents take only what <see cref="Of"/> assigns them, so together they remain a
    /// partition and no endpoint can go missing from both.
    /// </summary>
    public static bool IncludedIn(string docName, ApiDescription api) =>
        docName == All || Of(api) == docName;
}
