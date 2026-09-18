using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Common;
using Microsoft.AspNetCore.Hosting;
using StackExchange.Redis;

namespace Aynera.Api.IntegrationTests;

public sealed class RedisOtpAuthApiFactory : AuthApiFactory
{
    private static readonly bool DetectedRedis = RedisProbe.IsAvailable();

    public bool RedisAvailable { get; } = DetectedRedis;

    public RedisOtpAuthApiFactory() : base(useInMemoryOtpStore: !DetectedRedis)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Aynera:Redis", RedisProbe.Configuration);
        builder.UseSetting("AYNERA_REDIS", RedisProbe.Configuration);
    }
}

[Collection("Integration")]
public sealed class RedisOtpConsumptionTests(RedisOtpAuthApiFactory factory)
{
    [Fact]
    public async Task Recover_ConcurrentProofUse_AgainstRedis_SucceedsOnce()
    {
        Assert.True(factory.RedisAvailable, "Redis is required. Start redis (docker compose) or set AYNERA_REDIS.");
        using var client = factory.CreateClient();
        var phone = UniquePhone();
        using var registered = await client.PostAsJsonAsync(
            "/members/register",
            new CreateMemberRequest(
                phone,
                "Ada",
                "Lovelace",
                Gender.Female,
                new DateOnly(1990, 5, 15),
                "Mumbai",
                $"redis-otp-{Guid.NewGuid():N}@example.com",
                null,
                "secret12"));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        using var login = await client.PostAsJsonAsync("/auth/password", new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var deactivated = await client.PostAsync("/members/me/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        using var requested = await client.PostAsJsonAsync(
            "/members/reactivate/request",
            new RequestMemberReactivationRequest(phone));
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var code = factory.Sms.GetCode("+91" + phone);
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
    }

    [Fact]
    public async Task LoginOtp_ConcurrentVerify_AgainstRedis_IssuesTokensOnce()
    {
        Assert.True(factory.RedisAvailable, "Redis is required. Start redis (docker compose) or set AYNERA_REDIS.");
        using var client = factory.CreateClient();
        var phone = UniquePhone();
        using var registered = await client.PostAsJsonAsync(
            "/members/register",
            new CreateMemberRequest(
                phone,
                "Ada",
                "Lovelace",
                Gender.Female,
                new DateOnly(1990, 5, 15),
                "Mumbai",
                $"redis-login-{Guid.NewGuid():N}@example.com",
                null,
                "secret12"));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        using var requested = await client.PostAsJsonAsync("/auth/login", new RequestMemberOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var code = factory.Sms.GetCode("+91" + phone);
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var verifications = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/auth/verifysms", new VerifyMemberOtpRequest(phone, code!)),
            secondClient.PostAsJsonAsync("/auth/verifysms", new VerifyMemberOtpRequest(phone, code!)));
        using (verifications[0])
        using (verifications[1])
        {
            Assert.Equal(1, verifications.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, verifications.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        }
    }

    private static string UniquePhone() => "9" + Random.Shared.NextInt64(100_000_000, 999_999_999);
}

internal static class RedisProbe
{
    public static string Configuration =>
        Environment.GetEnvironmentVariable("AYNERA_REDIS") ?? "localhost:6379";

    public static bool IsAvailable()
    {
        try
        {
            var options = ConfigurationOptions.Parse(Configuration);
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = 1000;
            options.SyncTimeout = 1000;
            using var mux = ConnectionMultiplexer.Connect(options);
            return mux.IsConnected;
        }
        catch
        {
            return false;
        }
    }
}
