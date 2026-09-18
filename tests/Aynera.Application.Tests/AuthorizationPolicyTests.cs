using System.Security.Claims;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Statics;
using Aynera.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Application.Tests;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData(AuthPolicies.Member, "Member", true, false, true)]
    [InlineData(AuthPolicies.Member, "Member", false, false, false)]
    [InlineData(AuthPolicies.Member, "Member", true, true, false)]
    [InlineData(AuthPolicies.Admin, "Admin", true, false, true)]
    [InlineData(AuthPolicies.Admin, "Admin", false, false, false)]
    [InlineData(AuthPolicies.Admin, "Admin", true, true, false)]
    [InlineData(AuthPolicies.MemberReactivation, "Member", false, false, true)]
    [InlineData(AuthPolicies.MemberReactivation, "Member", false, true, true)]
    [InlineData(AuthPolicies.MemberAccountDeletion, "Member", false, true, true)]
    public async Task Policies_ApplyOperationSpecificStateRules(string policy, string kind, bool active, bool restricted, bool allowed)
    {
        var id = Guid.NewGuid();
        var users = new FakeUserRepository();
        var account = Admin(id) with { AccountKind = kind, IsActive = active, IsRestricted = restricted };
        users.Add(account);
        using var provider = CreateProvider(users);
        using var scope = provider.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var role = kind == "Member" ? AuthRoles.Member : AuthRoles.Admin;
        var principal = Principal(id, role, role);
        Assert.Equal(allowed, (await auth.AuthorizeAsync(principal, null, policy)).Succeeded);
        users.Add(account with { IsDeleted = true });
        Assert.False((await auth.AuthorizeAsync(principal, null, policy)).Succeeded);
        users.Add(account with { AccountKind = kind == "Member" ? "Admin" : "Member" });
        Assert.False((await auth.AuthorizeAsync(principal, null, policy)).Succeeded);
    }

    [Theory]
    [InlineData(AuthPolicies.Member)]
    [InlineData(AuthPolicies.Admin)]
    [InlineData(AuthPolicies.MemberReactivation)]
    [InlineData(AuthPolicies.MemberAccountDeletion)]
    public async Task AccountPolicies_RejectMissingAccount(string policy)
    {
        using var provider = CreateProvider(new FakeUserRepository());
        using var scope = provider.CreateScope();
        var role = policy == AuthPolicies.Admin ? AuthRoles.Admin : AuthRoles.Member;
        Assert.False((await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(Principal(Guid.NewGuid(), role, role), null, policy)).Succeeded);
    }

    private static ServiceProvider CreateProvider(FakeUserRepository users,
        string memberAudience = AuthAudiences.Member, string adminAudience = AuthAudiences.Admin)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserRepository>(users);
        services.AddAyneraAuthorization(memberAudience, adminAudience);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ClaimsPrincipal Principal(Guid id, string role = AuthRoles.Admin,
        string audience = AuthAudiences.Admin, bool authenticated = true, string subjectType = ClaimTypes.NameIdentifier) =>
        new(new ClaimsIdentity([
            new Claim(subjectType, id.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("aud", audience),
            new Claim("isSuperAdmin", "true")
        ], authenticated ? "Test" : null));

    private static UserRecord Admin(Guid id) =>
        new(id, null, false, "admin@example.com", true, "Admin", true, false, true, false, [AuthRoles.Admin]);

    [Theory]
    [InlineData("active", true)]
    [InlineData("regular", false)]
    [InlineData("inactive", false)]
    [InlineData("restricted", false)]
    [InlineData("deleted", false)]
    [InlineData("member", false)]
    [InlineData("missing", false)]
    public async Task SuperAdmin_UsesLiveAccountState(string state, bool expected)
    {
        var id = Guid.NewGuid();
        var users = new FakeUserRepository();
        var account = state switch
        {
            "regular" => Admin(id) with { IsSuperAdmin = false },
            "inactive" => Admin(id) with { IsActive = false },
            "restricted" => Admin(id) with { IsRestricted = true },
            "deleted" => Admin(id) with { IsDeleted = true },
            "member" => Admin(id) with { AccountKind = "Member" },
            _ => Admin(id)
        };
        if (state != "missing") users.Add(account);
        using var provider = CreateProvider(users);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var result = await service.AuthorizeAsync(Principal(id), null, AuthPolicies.SuperAdmin);
        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData(AuthRoles.Admin, AuthAudiences.Admin, true, true)]
    [InlineData(AuthRoles.Member, AuthAudiences.Admin, true, false)]
    [InlineData(AuthRoles.Admin, AuthAudiences.Member, true, false)]
    [InlineData(AuthRoles.Admin, AuthAudiences.Admin, false, false)]
    public async Task SuperAdmin_AlsoRequiresAuthenticatedAdminRoleAndAudience(
        string role, string audience, bool authenticated, bool expected)
    {
        var id = Guid.NewGuid();
        var users = new FakeUserRepository();
        users.Add(Admin(id));
        using var provider = CreateProvider(users);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(Principal(id, role, audience, authenticated), null, AuthPolicies.SuperAdmin);
        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public async Task SuperAdmin_RevocationTakesEffectWithTheSameToken(string subjectType)
    {
        var id = Guid.NewGuid();
        var users = new FakeUserRepository();
        users.Add(Admin(id));
        using var provider = CreateProvider(users);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = Principal(id, subjectType: subjectType);
        Assert.True((await service.AuthorizeAsync(principal, null, AuthPolicies.SuperAdmin)).Succeeded);
        users.Add(Admin(id) with { IsSuperAdmin = false });
        Assert.False((await service.AuthorizeAsync(principal, null, AuthPolicies.SuperAdmin)).Succeeded);
        Assert.True((await service.AuthorizeAsync(principal, null, AuthPolicies.Admin)).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-subject")]
    public async Task SuperAdmin_RejectsMissingOrInvalidSubject(string? subject)
    {
        using var provider = CreateProvider(new FakeUserRepository());
        using var scope = provider.CreateScope();
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.Role, AuthRoles.Admin), new Claim("aud", AuthAudiences.Admin)
        ], "Test");
        if (subject is not null) identity.AddClaim(new Claim("sub", subject));
        Assert.False((await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(identity), null, AuthPolicies.SuperAdmin)).Succeeded);
    }

    [Theory]
    [InlineData(AuthPolicies.Member, AuthRoles.Member, "custom-member")]
    [InlineData(AuthPolicies.MemberReactivation, AuthRoles.Member, "custom-member")]
    [InlineData(AuthPolicies.MemberAccountDeletion, AuthRoles.Member, "custom-member")]
    [InlineData(AuthPolicies.Admin, AuthRoles.Admin, "custom-admin")]
    [InlineData(AuthPolicies.SuperAdmin, AuthRoles.Admin, "custom-admin")]
    public async Task Policies_RespectConfiguredAudiences(string policy, string role, string audience)
    {
        var id = Guid.NewGuid();
        var users = new FakeUserRepository();
        users.Add(Admin(id) with { AccountKind = role == AuthRoles.Member ? "Member" : "Admin" });
        using var provider = CreateProvider(users, "custom-member", "custom-admin");
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.True((await service.AuthorizeAsync(Principal(id, role, audience), null, policy)).Succeeded);
        Assert.False((await service.AuthorizeAsync(Principal(id, role, "wrong-audience"), null, policy)).Succeeded);
    }
}
