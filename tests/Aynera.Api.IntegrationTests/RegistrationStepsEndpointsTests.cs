using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Domain.Admissions.Responses;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Common;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// The app's registration order over HTTP: phone → SMS code (account + tokens) → email → emailed code.
/// </summary>
[Collection("Integration")]
public sealed class RegistrationStepsEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string NewPhone() => "98" + Random.Shared.NextInt64(10_000_000, 99_999_999);

    private static async Task<ApiResponse<T>?> Body<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json);

    [Fact]
    public async Task PhoneCode_CreatesDraftAccountWithTokens_ThenEmailCode_ConfirmsEmail()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();
        var email = $"steps-{phone}@example.com";

        // 1. Ask for the SMS code — no account exists yet.
        var start = await client.PostAsJsonAsync("/members/register/phone", new StartPhoneRegistrationRequest(phone));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var code = factory.Sms.GetCode("+91" + phone);
        Assert.False(string.IsNullOrWhiteSpace(code));

        // Wrong code is refused and consumes an attempt, not the challenge.
        var wrong = await client.PostAsJsonAsync("/members/register/phone/verify", new VerifyPhoneRegistrationRequest(phone, "000000"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("otp_invalid", (await Body<object?>(wrong))!.ErrorCode);

        // 2. Right code → Draft account + member tokens.
        var verify = await client.PostAsJsonAsync("/members/register/phone/verify", new VerifyPhoneRegistrationRequest(phone, code!));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var tokens = (await Body<TokenResponse>(verify))!.Data!;
        Assert.True(tokens.Account.PhoneConfirmed);
        Assert.Null(tokens.Account.Email);
        Assert.Null(tokens.Account.Profile);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var me = await Body<AuthAccountDto>(await client.GetAsync("/members/me"));
        Assert.Equal(tokens.Account.Id, me!.Data!.Id);
        Assert.True(me.Data.PhoneConfirmed);
        Assert.False(me.Data.EmailConfirmed);

        var admission = await Body<MemberAdmissionDto>(await client.GetAsync("/admissions/me"));
        Assert.Equal("Draft", admission!.Data!.State);

        // The same number can no longer start a registration.
        var again = await factory.CreateClient().PostAsJsonAsync("/members/register/phone", new StartPhoneRegistrationRequest(phone));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("user_already_exists", (await Body<object?>(again))!.ErrorCode);

        // 3. Ask for the email code.
        var startEmail = await client.PostAsJsonAsync("/members/me/email", new StartEmailVerificationRequest(email));
        Assert.Equal(HttpStatusCode.OK, startEmail.StatusCode);
        var emailCode = factory.Email.GetOtpCode(email);
        Assert.False(string.IsNullOrWhiteSpace(emailCode));

        // 4. Verify it → email set + confirmed on the account.
        var verifyEmail = await client.PostAsJsonAsync("/members/me/email/verify", new VerifyEmailCodeRequest(email, emailCode!));
        Assert.Equal(HttpStatusCode.OK, verifyEmail.StatusCode);
        var account = (await Body<AuthAccountDto>(verifyEmail))!.Data!;
        Assert.Equal(email, account.Email);
        Assert.True(account.EmailConfirmed);

        me = await Body<AuthAccountDto>(await client.GetAsync("/members/me"));
        Assert.True(me!.Data!.EmailConfirmed);
        Assert.Equal(email, me.Data.Email);
    }

    [Fact]
    public async Task EmailSteps_RequireASession_AndRejectAnotherAccountsEmail()
    {
        var anonymous = factory.CreateClient();
        var denied = await anonymous.PostAsJsonAsync("/members/me/email", new StartEmailVerificationRequest("x@example.com"));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        // An email already held by a live account cannot be claimed by a new one.
        var owner = NewPhone();
        var ownerEmail = $"owner-{owner}@example.com";
        var registered = await anonymous.PostAsJsonAsync("/members/register", new CreateMemberRequest(
            owner, "Owner Member", Domain.Auth.Enums.Gender.Female, new DateOnly(1993, 3, 3), "Bangalore", ownerEmail,
            Hometown: "Pune"));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        var phone = NewPhone();
        await anonymous.PostAsJsonAsync("/members/register/phone", new StartPhoneRegistrationRequest(phone));
        var verify = await anonymous.PostAsJsonAsync("/members/register/phone/verify",
            new VerifyPhoneRegistrationRequest(phone, factory.Sms.GetCode("+91" + phone)!));
        var tokens = (await Body<TokenResponse>(verify))!.Data!;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var taken = await client.PostAsJsonAsync("/members/me/email", new StartEmailVerificationRequest(ownerEmail));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("email_already_exists", (await Body<object?>(taken))!.ErrorCode);
    }
}
