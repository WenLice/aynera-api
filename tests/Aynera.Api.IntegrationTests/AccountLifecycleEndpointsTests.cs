using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class AccountLifecycleEndpointsTests(AuthApiFactory factory)
{
    [Fact]
    public async Task Deactivate_RevokesRefresh_AndExistingAccessTokenCanReactivate()
    {
        using var client = factory.CreateClient();
        var (phone, tokens) = await RegisterLoginAsync(client);

        using var deactivated = await client.PostAsync("/members/me/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var revoked = await client.PostAsJsonAsync("/auth/refresh", new RefreshTokenRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var blocked = await client.PostAsJsonAsync("/auth/password", new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        using var reactivated = await client.PostAsync("/members/me/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        using var newLogin = await client.PostAsJsonAsync("/auth/password", new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        var newTokens = (await newLogin.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>())!.Data!;
        Assert.Equal(tokens.Account.Id, newTokens.Account.Id);
        Assert.True(newTokens.Account.IsActive);
        Assert.NotEqual(tokens.RefreshToken, newTokens.RefreshToken);
    }

    [Fact]
    public async Task Recover_WithReactivationOtp_ReactivatesWithoutRestoringRefresh()
    {
        using var client = factory.CreateClient();
        var (phone, tokens) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);

        client.DefaultRequestHeaders.Authorization = null;
        using var requested = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(phone));
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        var code = factory.Sms.GetCode(ToE164(phone));
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var stillBlocked = await client.PostAsJsonAsync(
            "/auth/password",
            new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.Forbidden, stillBlocked.StatusCode);

        using var recovered = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(phone, code!));
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);

        using var replay = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(phone, code!));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using var restoredRefresh = await client.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, restoredRefresh.StatusCode);

        using var newLogin = await client.PostAsJsonAsync(
            "/auth/password",
            new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        var newTokens = (await newLogin.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>())!.Data!;
        Assert.True(newTokens.Account.IsActive);
        Assert.NotEqual(tokens.RefreshToken, newTokens.RefreshToken);
    }

    [Fact]
    public async Task Recover_WithEmailOtp_Reactivates()
    {
        using var client = factory.CreateClient();
        var email = $"recover-{Guid.NewGuid():N}@example.com";
        await RegisterLoginAsync(client, email);
        await DeactivateAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        using var requested = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(email));
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var code = factory.Email.GetOtpCode(email);
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var recovered = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(email, code!));
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task Recover_RejectsInvalidExpiredWrongPurposeAndIdentifierOnly()
    {
        using var client = factory.CreateClient();
        var (phone, _) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        using var identifierOnly = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(phone, ""));
        Assert.Equal(HttpStatusCode.BadRequest, identifierOnly.StatusCode);

        using var missing = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(phone, "123456"));
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var requested = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(phone));
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var code = factory.Sms.GetCode(ToE164(phone));
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var invalid = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(phone, "000000"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadFromJsonAsync<ApiResponse<object?>>();
        Assert.Equal("otp_invalid", invalidBody!.ErrorCode);

        using var loginWithRecoveryCode = await client.PostAsJsonAsync(
            "/auth/otp/verify",
            new VerifyMemberOtpRequest(phone, code!));
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithRecoveryCode.StatusCode);

        using var expireScope = factory.Services.CreateScope();
        var otp = expireScope.ServiceProvider.GetRequiredService<IOtpChallengeRepository>();
        var destination = ToE164(phone);
        await otp.StoreAsync(
            new OtpChallenge(
                "phone",
                destination,
                TokenHasher.Hash(code!),
                0,
                DateTimeOffset.UtcNow.AddSeconds(-1),
                OtpPurposes.Reactivation,
                "member"),
            TimeSpan.Zero,
            CancellationToken.None);

        using var expired = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(phone, code!));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        var expiredBody = await expired.Content.ReadFromJsonAsync<ApiResponse<object?>>();
        Assert.Equal("otp_expired", expiredBody!.ErrorCode);

        using var stillBlocked = await client.PostAsJsonAsync(
            "/auth/password",
            new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.Forbidden, stillBlocked.StatusCode);
    }

    [Fact]
    public async Task RequestReactivation_DoesNotRevealUnknownDeletedRestrictedOrWrongKind()
    {
        using var client = factory.CreateClient();
        var unknownPhone = UniquePhone();
        using var unknown = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(unknownPhone));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Null(factory.Sms.GetCode(ToE164(unknownPhone)));

        var (activePhone, _) = await RegisterLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = null;
        using var active = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(activePhone));
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.Null(factory.Sms.GetCode(ToE164(activePhone)));

        var (deactivatedPhone, _) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var member = await users.FindByPhoneAsync(ToE164(deactivatedPhone), CancellationToken.None);
            await users.RestrictMemberAsync(member!.Id, CancellationToken.None);
        }

        client.DefaultRequestHeaders.Authorization = null;
        using var restricted = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(deactivatedPhone));
        Assert.Equal(HttpStatusCode.OK, restricted.StatusCode);
        Assert.Null(factory.Sms.GetCode(ToE164(deactivatedPhone)));

        var (deletedPhone, _) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);
        using var deletedAccount = await client.DeleteAsync("/members/me");
        Assert.Equal(HttpStatusCode.OK, deletedAccount.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var deleted = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(deletedPhone));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Null(factory.Sms.GetCode(ToE164(deletedPhone)));

        var adminPhone = UniquePhone();
        var adminEmail = $"admin-recover-{Guid.NewGuid():N}@example.com";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserRepository>()
                .CreateAdminAsync(adminEmail, ToE164(adminPhone), "adminpass12", CancellationToken.None);
        }

        using var admin = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(adminPhone));
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Null(factory.Sms.GetCode(ToE164(adminPhone)));
    }

    [Fact]
    public async Task Recover_RejectsRestrictedDeletedAndWrongKindAfterProof()
    {
        using var client = factory.CreateClient();
        var (restrictedPhone, _) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);
        client.DefaultRequestHeaders.Authorization = null;
        using var restrictedRequest = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(restrictedPhone));
        Assert.Equal(HttpStatusCode.OK, restrictedRequest.StatusCode);
        var restrictedCode = factory.Sms.GetCode(ToE164(restrictedPhone));
        Assert.False(string.IsNullOrWhiteSpace(restrictedCode));
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var member = await users.FindByPhoneAsync(ToE164(restrictedPhone), CancellationToken.None);
            await users.RestrictMemberAsync(member!.Id, CancellationToken.None);
        }

        using var restrictedRecover = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(restrictedPhone, restrictedCode!));
        Assert.Equal(HttpStatusCode.Forbidden, restrictedRecover.StatusCode);
        var restrictedBody = await restrictedRecover.Content.ReadFromJsonAsync<ApiResponse<object?>>();
        Assert.Equal("account_restricted", restrictedBody!.ErrorCode);

        var (deletedPhone, _) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);
        client.DefaultRequestHeaders.Authorization = null;
        using var deletedRequest = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(deletedPhone));
        Assert.Equal(HttpStatusCode.OK, deletedRequest.StatusCode);
        var deletedCode = factory.Sms.GetCode(ToE164(deletedPhone));
        Assert.False(string.IsNullOrWhiteSpace(deletedCode));
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var member = await users.FindByPhoneAsync(ToE164(deletedPhone), CancellationToken.None);
            await users.SoftDeleteMemberAsync(member!.Id, CancellationToken.None);
        }

        using var deletedRecover = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(deletedPhone, deletedCode!));
        Assert.Equal(HttpStatusCode.NotFound, deletedRecover.StatusCode);

        var adminPhone = UniquePhone();
        using var adminRecover = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(adminPhone, "123456"));
        Assert.Equal(HttpStatusCode.Unauthorized, adminRecover.StatusCode);
    }

    [Fact]
    public async Task Recover_ConcurrentProofUse_LeavesAccountActiveOnce()
    {
        using var client = factory.CreateClient();
        var (phone, _) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        using var requested = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(phone));
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var code = factory.Sms.GetCode(ToE164(phone));
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var recoveries = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/members/reactivate/recover", new RecoverMemberRequest(phone, code!)),
            secondClient.PostAsJsonAsync("/members/reactivate/recover", new RecoverMemberRequest(phone, code!)));
        using (recoveries[0])
        using (recoveries[1])
        {
            Assert.Equal(1, recoveries.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, recoveries.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        }

        using var login = await client.PostAsJsonAsync(
            "/auth/password",
            new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>())!.Data!;
        Assert.True(tokens.Account.IsActive);

        using var replay = await client.PostAsJsonAsync(
            "/members/reactivate/recover",
            new RecoverMemberRequest(phone, code!));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedReactivate_RejectsRestrictedMember()
    {
        using var client = factory.CreateClient();
        var (phone, _) = await RegisterLoginAsync(client);
        await DeactivateAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var member = await users.FindByPhoneAsync(ToE164(phone), CancellationToken.None);
            await users.RestrictMemberAsync(member!.Id, CancellationToken.None);
        }

        using var reactivated = await client.PostAsync("/members/me/reactivate", null);
        Assert.Equal(HttpStatusCode.Forbidden, reactivated.StatusCode);
        var body = await reactivated.Content.ReadFromJsonAsync<ApiResponse<object?>>();
        Assert.Equal("account_restricted", body!.ErrorCode);
    }

    private async Task<(string Phone, TokenResponse Tokens)> RegisterLoginAsync(
        HttpClient client,
        string? email = null)
    {
        var phone = UniquePhone();
        var request = new CreateMemberRequest(
            phone,
            "Ada Lovelace",
            Gender.Female,
            new DateOnly(1990, 5, 15),
            "Bangalore",
            email ?? $"lifecycle-{Guid.NewGuid():N}@example.com",
            Password: "secret12");
        using var registered = await client.PostAsJsonAsync("/members/register", request);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        using var login = await client.PostAsJsonAsync("/auth/password", new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (phone, tokens);
    }

    private static async Task DeactivateAsync(HttpClient client)
    {
        using var deactivated = await client.PostAsync("/members/me/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
    }

    private static string UniquePhone() => "9" + Random.Shared.NextInt64(100_000_000, 999_999_999);

    private static string ToE164(string phone) => "+91" + phone;
}
