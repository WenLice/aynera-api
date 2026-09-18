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
}
