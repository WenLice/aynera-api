using System.Reflection;
using Aynera.Api.Controllers;
using Aynera.Api.OpenApi;
using Aynera.Domain.Auth.Statics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

public sealed class ControllerRoutingTests
{
    private static readonly string[] SuperAdminActions =
        ["CreateAdmin", "ListAdmins", "DeactivateAdmin", "ActivateAdmin", "RestrictMember", "UnrestrictMember"];

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore().AddApiExplorer().AddApplicationPart(typeof(AuthController).Assembly);
        return services.BuildServiceProvider();
    }

    private static ControllerActionDescriptor[] Actions(IServiceProvider provider) =>
        provider.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToArray();

    private static object[] Metadata(ControllerActionDescriptor action) =>
        action.ControllerTypeInfo.GetCustomAttributes(true)
            .Concat(action.MethodInfo.GetCustomAttributes(true)).ToArray();

    [Fact]
    public void EveryRoute_StartsWithItsControllerPrefix_AndHasNoLegacyAlias()
    {
        using var provider = BuildProvider();
        var actions = Actions(provider);

        foreach (var action in actions)
        {
            var template = action.AttributeRouteInfo!.Template!;
            var prefix = action.ControllerTypeInfo.GetCustomAttribute<RouteAttribute>()!.Template!;

            Assert.False(template.StartsWith("admin/") || template.StartsWith("public/"),
                $"{action.DisplayName}: legacy alias '{template}' must not exist.");
            Assert.True(template == prefix || template.StartsWith(prefix + "/"),
                $"{action.DisplayName}: route '{template}' must start with controller prefix '{prefix}'.");
        }

        var routes = actions.SelectMany(a => a.ActionConstraints!.OfType<HttpMethodActionConstraint>()
            .SelectMany(c => c.HttpMethods.Select(m => $"{m} {a.AttributeRouteInfo!.Template}"))).ToArray();
        Assert.Equal(routes.Length, routes.Distinct().Count());
        Assert.Contains("GET members/GetAll", routes);
        Assert.Contains("GET members/me", routes);
        Assert.Contains("DELETE members/me", routes);
        Assert.Contains("POST members/me/deactivate", routes);
        Assert.Contains("POST members/reactivate/recover", routes);
        Assert.Contains("GET admins/GetAll", routes);
        Assert.Contains("POST admins/Create", routes);
        Assert.Contains("GET admins/me", routes);
        Assert.Contains("POST auth/admin/password", routes);
        Assert.Contains("POST photos/Upload", routes);
        Assert.Contains("POST introduction-video/Upload", routes);
        Assert.Contains("GET introduction-video/me", routes);
        Assert.Contains("GET introduction-video/{id:guid}/content", routes);
        Assert.Contains("GET photos/GetAll", routes);
        Assert.Contains("GET photos/{id:guid}/{photoId:guid}", routes);
        Assert.Contains("GET suggestions/GetAll", routes);
        Assert.Contains("POST suggestions/Create", routes);
        Assert.Contains("GET feedback/GetAll", routes);
        Assert.Contains("POST feedback/Create", routes);
        Assert.Contains("GET venues/GetAll", routes);
        Assert.Contains("POST venues/Create", routes);
        Assert.Contains("GET early-access/cities/GetAll", routes);
        Assert.Contains("GET admissions/GetAll", routes);
        Assert.Contains("GET audit/events/GetAll", routes);
        Assert.DoesNotContain(routes, r => r.Contains(" admin/") || r.Contains(" public/") || r.Contains(" users"));
        Assert.DoesNotContain(routes, r => r.Contains(" members/me/photos") || r.Contains(" members/me/introduction-video"));
    }

    [Fact]
    public void CollectionRoutes_UseActionSegments()
    {
        // Convention: list actions end in "GetAll" and collection POSTs carry an action segment
        // ("Create", "Upload", "register", …). A bare controller prefix is never a route (health
        // excepted), and no template is served by both GET and POST — the verb is in the path.
        using var provider = BuildProvider();
        var actions = Actions(provider);

        foreach (var action in actions)
        {
            var template = action.AttributeRouteInfo!.Template!;
            var prefix = action.ControllerTypeInfo.GetCustomAttribute<RouteAttribute>()!.Template!;
            Assert.True(template != prefix || prefix == "health",
                $"{action.DisplayName}: bare collection route '{template}' — use '{prefix}/GetAll' or '{prefix}/Create'.");
        }

        var byTemplate = actions
            .SelectMany(a => a.ActionConstraints!.OfType<HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods.Select(m => (Method: m, Template: a.AttributeRouteInfo!.Template!))))
            .GroupBy(x => x.Template, x => x.Method);
        foreach (var group in byTemplate)
        {
            Assert.False(group.Contains("GET") && group.Contains("POST"),
                $"'{group.Key}' is served by both GET and POST — split into .../GetAll and .../Create.");
        }

        var lists = actions.Where(a => a.ActionName.StartsWith("List", StringComparison.Ordinal));
        Assert.All(lists, a => Assert.EndsWith("/GetAll", a.AttributeRouteInfo!.Template));
    }

    [Fact]
    public void EveryAction_DeclaresAuthorizationExplicitly()
    {
        // The fallback policy denies anything undeclared, so every real endpoint must opt in one way
        // or the other rather than rely on the default.
        using var provider = BuildProvider();

        foreach (var action in Actions(provider))
        {
            var metadata = Metadata(action);
            var anonymous = metadata.Any(m => m is IAllowAnonymous);
            var policy = metadata.OfType<IAuthorizeData>().Select(a => a.Policy).FirstOrDefault(p => !string.IsNullOrEmpty(p));

            Assert.True(anonymous || policy is not null,
                $"{action.DisplayName}: must declare [AllowAnonymous] or [Authorize(Policy = ...)].");

            if (action.ControllerName is "Members" or "Admins" && SuperAdminActions.Contains(action.ActionName))
            {
                Assert.Equal(AuthPolicies.SuperAdmin, policy);
            }
        }
    }

    [Fact]
    public void SwaggerAudience_FollowsAuthorizationPolicy()
    {
        using var provider = BuildProvider();
        var descriptions = provider.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items.SelectMany(g => g.Items).ToArray();
        Assert.NotEmpty(descriptions);

        foreach (var api in descriptions)
        {
            var action = (ControllerActionDescriptor)api.ActionDescriptor;
            var policy = Metadata(action).OfType<IAuthorizeData>().Select(a => a.Policy).FirstOrDefault();
            var expected = policy is AuthPolicies.Admin or AuthPolicies.SuperAdmin
                || (action.ControllerName == "Auth" && api.RelativePath!.StartsWith("auth/admin/"))
                ? ApiAudience.Admin
                : ApiAudience.Member;

            Assert.True(expected == ApiAudience.Of(api),
                $"{api.HttpMethod} {api.RelativePath}: expected '{expected}' doc, got '{ApiAudience.Of(api)}'.");
        }

        string Audience(string method, string path) =>
            ApiAudience.Of(Assert.Single(descriptions, d => d.HttpMethod == method && d.RelativePath == path));

        Assert.Equal(ApiAudience.Member, Audience("GET", "health"));
        Assert.Equal(ApiAudience.Member, Audience("POST", "auth/login"));
        Assert.Equal(ApiAudience.Member, Audience("GET", "members/me"));
        Assert.Equal(ApiAudience.Member, Audience("POST", "feedback/Create"));
        Assert.Equal(ApiAudience.Admin, Audience("GET", "feedback/GetAll"));
        Assert.Equal(ApiAudience.Admin, Audience("POST", "auth/admin/password"));
        Assert.Equal(ApiAudience.Admin, Audience("GET", "members/GetAll"));
        Assert.Equal(ApiAudience.Admin, Audience("GET", "admins/me"));
        Assert.Equal(ApiAudience.Member, Audience("GET", "photos/GetAll"));
        Assert.Equal(ApiAudience.Admin, Audience("GET", "photos/{id}/{photoId}"));
        Assert.Equal(ApiAudience.Member, Audience("GET", "introduction-video/me"));
        Assert.Equal(ApiAudience.Admin, Audience("GET", "introduction-video/{id}/content"));
        Assert.Equal(ApiAudience.Admin, Audience("GET", "audit/events/GetAll"));
    }
}
