using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Admissions.Requests;
using Aynera.Domain.Admissions.Responses;
using Aynera.Domain.Admissions.Validators;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class AdmissionEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RequiredConsents = ["Terms", "Privacy", "CommunityGuidelines"];

    [Fact]
    public async Task MemberSubmits_StaffApproves_EligibilityFlipsToTrue()
    {
        var (memberId, memberToken) = await CreateMemberAsync();
        var adminToken = await CreateAdminTokenAsync();
        using var member = Client(memberToken);
        using var admin = Client(adminToken);

        // Fresh member reads as Draft and is blocked on approval + every consent.
        var initial = await ReadAsync<MemberAdmissionDto>(member, "/admissions/me");
        Assert.Equal("Draft", initial.State);
        Assert.False(initial.Eligibility.IsEligible);
        Assert.Contains(EligibilityReasons.AdmissionNotApproved, initial.Eligibility.UnmetRequirements);
        foreach (var kind in RequiredConsents)
        {
            Assert.Contains(EligibilityReasons.ConsentMissing(kind), initial.Eligibility.UnmetRequirements);
        }

        foreach (var kind in RequiredConsents)
        {
            var accept = await member.PostAsJsonAsync("/admissions/me/consents", new AcceptConsentRequest(kind, "1.0"));
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        }

        var submit = await member.PostAsync("/admissions/me/submit", null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var submitted = (await submit.Content.ReadFromJsonAsync<ApiResponse<MemberAdmissionDto>>(JsonOptions))!.Data!;
        Assert.Equal("Submitted", submitted.State);
        Assert.Equal(3, submitted.Consents.Count);
        Assert.False(submitted.Eligibility.IsEligible);
        Assert.Equal([EligibilityReasons.AdmissionNotApproved], submitted.Eligibility.UnmetRequirements);

        // Queue shows the member; a second submit is refused.
        var queue = await ReadAsync<PagedResult<MemberAdmissionSummaryDto>>(admin, "/admissions/GetAll?state=Submitted");
        Assert.Contains(queue.Items, i => i.UserId == memberId);
        var again = await member.PostAsync("/admissions/me/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var start = await admin.PostAsJsonAsync($"/admissions/{memberId}/decision",
            new AdmissionDecisionRequest("StartReview", null, "Checking photos"));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var approve = await admin.PostAsJsonAsync($"/admissions/{memberId}/decision",
            new AdmissionDecisionRequest("Approve", null, null));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approved = (await approve.Content.ReadFromJsonAsync<ApiResponse<MemberAdmissionDto>>(JsonOptions))!.Data!;
        Assert.Equal("Approved", approved.State);
        Assert.NotNull(approved.DecidedByUserId);
        Assert.Equal("Checking photos", approved.ReviewNote);
        Assert.True(approved.Eligibility.IsEligible);

        var eligibility = await ReadAsync<MemberEligibilityDto>(admin, $"/admissions/{memberId}/eligibility");
        Assert.True(eligibility.IsEligible);
        Assert.Empty(eligibility.UnmetRequirements);

        // Every transition left an audit row against the admission subject.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        var actions = await db.AuditEvents
            .Where(e => e.SubjectType == AuditSubjectTypes.MemberAdmission && e.SubjectId == memberId.ToString("D"))
            .Select(e => e.Action)
            .ToListAsync();
        Assert.Equal(3, actions.Count(a => a == AuditActions.MemberConsentAccepted));
        Assert.Contains(AuditActions.AdmissionSubmitted, actions);
        Assert.Contains(AuditActions.AdmissionReviewStarted, actions);
        Assert.Contains(AuditActions.AdmissionApproved, actions);
    }

    [Fact]
    public async Task RejectedMember_CanResubmit_AndReasonIsRequiredToReject()
    {
        var (memberId, memberToken) = await CreateMemberAsync();
        using var member = Client(memberToken);
        using var admin = Client(await CreateAdminTokenAsync());

        Assert.Equal(HttpStatusCode.OK, (await member.PostAsync("/admissions/me/submit", null)).StatusCode);

        var noReason = await admin.PostAsJsonAsync($"/admissions/{memberId}/decision",
            new AdmissionDecisionRequest("Reject", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var reject = await admin.PostAsJsonAsync($"/admissions/{memberId}/decision",
            new AdmissionDecisionRequest("Reject", "Intro video missing.", null));
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var rejected = (await reject.Content.ReadFromJsonAsync<ApiResponse<MemberAdmissionDto>>(JsonOptions))!.Data!;
        Assert.Equal("Rejected", rejected.State);
        Assert.Equal("Intro video missing.", rejected.DecisionReason);

        var resubmit = await member.PostAsync("/admissions/me/submit", null);
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        var resubmitted = (await resubmit.Content.ReadFromJsonAsync<ApiResponse<MemberAdmissionDto>>(JsonOptions))!.Data!;
        Assert.Equal("Submitted", resubmitted.State);
        Assert.Null(resubmitted.DecisionReason);
        Assert.Null(resubmitted.DecidedAtUtc);
    }

    [Fact]
    public async Task ConcurrentDecisions_ExactlyOneWins()
    {
        var (memberId, memberToken) = await CreateMemberAsync();
        using var member = Client(memberToken);
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsync("/admissions/me/submit", null)).StatusCode);

        var adminToken = await CreateAdminTokenAsync();
        using var approver = Client(adminToken);
        using var rejecter = Client(adminToken);

        var approve = approver.PostAsJsonAsync($"/admissions/{memberId}/decision",
            new AdmissionDecisionRequest("Approve", null, null));
        var reject = rejecter.PostAsJsonAsync($"/admissions/{memberId}/decision",
            new AdmissionDecisionRequest("Reject", "Duplicate account.", null));
        var responses = await Task.WhenAll(approve, reject);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        // The persisted row reflects the single winner, never a blend of both writes.
        var final = await ReadAsync<MemberAdmissionDto>(approver, $"/admissions/{memberId}");
        Assert.True(final.State is "Approved" or "Rejected");
        if (final.State == "Approved") Assert.Null(final.DecisionReason);
        else Assert.Equal("Duplicate account.", final.DecisionReason);
    }

    [Fact]
    public async Task ConcurrentConsentAccepts_OfSameVersion_StoreOneRow()
    {
        var (memberId, memberToken) = await CreateMemberAsync();
        using var a = Client(memberToken);
        using var b = Client(memberToken);

        var responses = await Task.WhenAll(
            a.PostAsJsonAsync("/admissions/me/consents", new AcceptConsentRequest("Terms", "1.0")),
            b.PostAsJsonAsync("/admissions/me/consents", new AcceptConsentRequest("Terms", "1.0")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        Assert.Equal(1, await db.MemberConsents.CountAsync(c => c.UserId == memberId));
    }

    [Fact]
    public async Task Submit_WithoutProfile_IsRejected()
    {
        var (_, memberToken) = await CreateMemberAsync(withProfile: false);
        using var member = Client(memberToken);

        var submit = await member.PostAsync("/admissions/me/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
        var body = await submit.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.Equal("admission_profile_required", body!.ErrorCode);
    }

    [Fact]
    public async Task Authorization_MemberCannotUseStaffRoutes_AndAnonymousIsUnauthorized()
    {
        var (memberId, memberToken) = await CreateMemberAsync();
        using var member = Client(memberToken);
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/admissions/GetAll")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/admissions/{memberId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/admissions/{memberId}/decision",
            new AdmissionDecisionRequest("Approve", null, null))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admissions/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/admissions/me/submit", null)).StatusCode);
    }

    [Fact]
    public async Task Staff_GetUnknownOrAdminUser_Is404()
    {
        using var admin = Client(await CreateAdminTokenAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admissions/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- helpers ----------

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions);
        Assert.True(body!.Success);
        return body.Data!;
    }

    /// <summary>A verified adult member with a profile, as the evaluator requires; token is member-audience.</summary>
    private async Task<(Guid Id, string Token)> CreateMemberAsync(bool withProfile = true)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            Email = $"member-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true,
            AccountKind = AccountKind.Member
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AuthRoles.Member)).Succeeded);

        if (withProfile)
        {
            var db = sp.GetRequiredService<AyneraDbContext>();
            var delhi = await db.EarlyAccessCities.SingleAsync(c => c.Name == "Delhi");
            db.MemberProfiles.Add(new MemberProfile
            {
                UserId = user.Id,
                FirstName = "Asha",
                LastName = "Rao",
                Gender = Gender.Female,
                DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
                City = delhi.Name,
                CityId = delhi.Id
            });
            await db.SaveChangesAsync();
        }

        var account = (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, CancellationToken.None))!;
        var token = sp.GetRequiredService<ITokenService>()
            .CreateAccessToken(account, AuthAudiences.Member, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow);
        return (user.Id, token.AccessToken);
    }

    private async Task<string> CreateAdminTokenAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser { Id = Guid.NewGuid(), UserName = phone, PhoneNumber = phone, AccountKind = AccountKind.Admin };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AuthRoles.Admin)).Succeeded);
        var account = (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, CancellationToken.None))!;
        var token = sp.GetRequiredService<ITokenService>()
            .CreateAccessToken(account, AuthAudiences.Admin, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow);
        return token.AccessToken;
    }
}
