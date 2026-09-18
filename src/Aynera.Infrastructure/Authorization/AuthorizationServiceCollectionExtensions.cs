using System.Security.Claims;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Statics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Infrastructure.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddAyneraAuthorization(
        this IServiceCollection services,
        string memberAudience = AuthAudiences.Member,
        string adminAudience = AuthAudiences.Admin)
    {
        services.AddScoped<IAuthorizationHandler, SuperAdminAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, AccountStateAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            var memberIdentity = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(AuthRoles.Member)
                .RequireAssertion(context => HasAudience(context.User, memberAudience))
                .Build();
            options.AddPolicy(AuthPolicies.Member, policy => policy.Combine(memberIdentity)
                .AddRequirements(new AccountStateRequirement(AccountKind.Member)));
            options.AddPolicy(AuthPolicies.MemberReactivation, policy => policy.Combine(memberIdentity)
                // Lifecycle service rejects restriction under the account lock and preserves its error response.
                .AddRequirements(new AccountStateRequirement(AccountKind.Member, AllowInactive: true, AllowRestricted: true)));
            options.AddPolicy(AuthPolicies.MemberAccountDeletion, policy => policy.Combine(memberIdentity)
                .AddRequirements(new AccountStateRequirement(AccountKind.Member, AllowInactive: true, AllowRestricted: true)));

            var adminPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(AuthRoles.Admin)
                .RequireAssertion(context => HasAudience(context.User, adminAudience))
                .AddRequirements(new AccountStateRequirement(AccountKind.Admin))
                .Build();
            options.AddPolicy(AuthPolicies.Admin, adminPolicy);
            options.AddPolicy(AuthPolicies.SuperAdmin, policy => policy
                .Combine(adminPolicy)
                .AddRequirements(new SuperAdminRequirement()));

            // Deny by default: an action that forgets its [Authorize(Policy = ...)] or [AllowAnonymous]
            // is refused rather than exposed. Every real endpoint declares one of the two explicitly.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        return services;
    }

    private static bool HasAudience(ClaimsPrincipal user, string audience) =>
        user.Claims.Any(claim =>
            claim.Type is "aud" or "http://schemas.microsoft.com/identity/claims/audience"
            && string.Equals(claim.Value, audience, StringComparison.Ordinal));
}
