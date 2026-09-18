using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Aynera.Api.Controllers;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Records;
using Aynera.Infrastructure.Authorization;
using Aynera.Domain.Auth.Statics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace Aynera.Api.IntegrationTests;

// Exercises real controller authorization metadata and middleware without starting database-backed services.
// Authentication is supplied by a test scheme; JWT validation remains covered by the database suite.
public sealed class AuthorizationHttpTests
{
    private static readonly Guid ActorId = Guid.Parse("8b4a12de-c67a-477f-a578-f6453eb575ad");

    private static IHost CreateServer(UserLookup repository) => new HostBuilder()
        .ConfigureWebHost(web => web.UseTestServer()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IUserRepository>((IUserRepository)repository);
            services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
            services.AddAyneraAuthorization();
            services.AddControllers().AddApplicationPart(typeof(MembersController).Assembly);
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapGet("/policy-probe", () => "allowed").RequireAuthorization(AuthPolicies.SuperAdmin);
            });
        })).Start();

    private static UserLookup Repository()
    {
        var repository = (UserLookup)DispatchProxy.Create<IUserRepository, UserLookup>();
        repository.Account = new UserRecord(ActorId, null, false, "admin@example.com", true,
            "Admin", true, false, true, false, [AuthRoles.Admin]);
        return repository;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("member", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.Forbidden)]
    public async Task PrivilegedControllerRoutes_RejectUnauthorizedCallers(string? role, HttpStatusCode expected)
    {
        var repository = Repository();
        repository.Account = repository.Account! with { IsSuperAdmin = false };
        using var server = CreateServer(repository);
        using var client = server.GetTestClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var id = Guid.NewGuid();
        var routes = new[]
        {
            (HttpMethod.Get, "/admins/GetAll"),
            (HttpMethod.Post, "/admins/Create"),
            (HttpMethod.Post, $"/admins/{id}/activate"),
            (HttpMethod.Post, $"/admins/{id}/deactivate"),
            (HttpMethod.Post, $"/members/{id}/restrict"),
            (HttpMethod.Post, $"/members/{id}/unrestrict")
        };
        foreach (var (method, route) in routes)
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, route));
            Assert.True(response.StatusCode == expected, $"{method} {route}: expected {expected}, got {response.StatusCode}");
        }
    }

    [Fact]
    public async Task SuperAdminMiddleware_ObservesPrivilegeRevocationAcrossRequests()
    {
        var repository = Repository();
        using var server = CreateServer(repository);
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
        using var allowed = await client.GetAsync("/policy-probe");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        repository.Account = repository.Account! with { IsSuperAdmin = false };
        using var denied = await client.GetAsync("/policy-probe");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    public class UserLookup : DispatchProxy
    {
        public UserRecord? Account { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IUserRepository.FindByIdAsync)
                ? Task.FromResult(args![0] is Guid id && id == ActorId ? Account : null)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("aud", role),
                new Claim("isSuperAdmin", "true")
            ], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
