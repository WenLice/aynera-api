using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Application.Features.Auth.Services.Implementations;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Application.Features.Photos.Repositories;
using Elaris.Application.Features.Videos.Repositories;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Auth.Statics;
using Elaris.Domain.Common;
using Elaris.Domain.Photos.Records;
using Elaris.Domain.Videos.Records;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Tests;

public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    public void NormalizeIndianMobile_AcceptsCommonFormats(string input, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.NormalizeIndianMobile(input));
    }

    [Fact]
    public void NormalizeIndianMobile_RejectsInvalid()
    {
        Assert.Throws<AuthException>(() => PhoneNormalizer.NormalizeIndianMobile("12345"));
    }
}

public class AuthServiceTests
{
    private static CreateMemberRequest RegisterRequest(
        string phone,
        string email = "member@example.com",
        string? password = null) =>
        new(
            phone,
            "Ada",
            "Lovelace",
            Gender.Female,
            new DateOnly(1990, 5, 15),
            "Mumbai",
            email,
            "Hindu",
            password);

    private static IOptions<EmailOptions> DefaultEmailOptions { get; } =
        Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" });

    private static AuthService CreateAuth(
        IOtpChallengeRepository otp,
        IUserRepository users,
        IRefreshSessionRepository sessions,
        ITokenService tokens,
        ISmsService sms,
        Microsoft.Extensions.Options.IOptions<OtpOptions> otpOptions,
        Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions,
        IEmailService? email = null) =>
        new(
            otp,
            users,
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            sessions,
            tokens,
            sms,
            email ?? new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            DefaultEmailOptions);

    private static (
        AuthService Auth,
        FakeSmsService Sms,
        FakeEmailService Email,
        FakeUserRepository Users) CreateReadyAuth()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 5,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "elaris-api",
            AccessTokenLifetimeMinutes = 60,
            AudienceMember = "member"
        });
        var otp = new FakeOtpRepository(otpOptions.Value);
        var sms = new FakeSmsService();
        var email = new FakeEmailService();
        var users = new FakeUserRepository();
        var auth = CreateAuth(
            otp,
            users,
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            sms,
            otpOptions,
            jwtOptions,
            email);
        return (auth, sms, email, users);
    }

    [Fact]
    public async Task RequestThenVerify_IssuesTokens()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 5,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "elaris-api",
            AccessTokenLifetimeMinutes = 15,
            AudienceMember = "member"
        });

        var otp = new FakeOtpRepository(otpOptions.Value);
        var sms = new FakeSmsService();
        var users = new FakeUserRepository();
        var sessions = new FakeRefreshSessionRepository();
        var tokens = new FakeTokenService();
        var auth = CreateAuth(otp, users, sessions, tokens, sms, otpOptions, jwtOptions);

        var phone = "9876543210";
        await auth.CreateMemberAsync(RegisterRequest(phone), CancellationToken.None);
        await auth.RequestMemberOtpAsync(new RequestMemberOtpRequest(phone), "127.0.0.1", CancellationToken.None);

        var code = sms.LastCode;
        Assert.False(string.IsNullOrWhiteSpace(code));

        var tokensResponse = await auth.VerifyMemberOtpAsync(
            new VerifyMemberOtpRequest(phone, code!),
            CancellationToken.None);

        Assert.Equal("Bearer", tokensResponse.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(tokensResponse.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokensResponse.RefreshToken));
        Assert.Equal("+919876543210", tokensResponse.Account.Phone);
        Assert.Contains("member", tokensResponse.Account.Roles);
        Assert.True(tokensResponse.Account.PhoneConfirmed);
        Assert.NotNull(tokensResponse.Account.Profile);
    }

    [Fact]
    public async Task RequestThenVerify_EmailOtp_ConfirmsEmail()
    {
        var (auth, sms, email, _) = CreateReadyAuth();
        const string address = "member@example.com";
        await auth.CreateMemberAsync(RegisterRequest("9876543210", address), CancellationToken.None);
        await auth.RequestMemberOtpAsync(new RequestMemberOtpRequest(address), "127.0.0.1", CancellationToken.None);

        Assert.Null(sms.LastCode);
        Assert.False(string.IsNullOrWhiteSpace(email.LastOtpCode));

        var tokens = await auth.VerifyMemberOtpAsync(
            new VerifyMemberOtpRequest(address, email.LastOtpCode!),
            CancellationToken.None);

        Assert.True(tokens.Account.EmailConfirmed);
        Assert.False(tokens.Account.PhoneConfirmed);
    }

    [Fact]
    public async Task PasswordLogin_AfterRegisterWithPassword_IssuesTokens()
    {
        var (auth, _, _, _) = CreateReadyAuth();
        await auth.CreateMemberAsync(
            RegisterRequest("9876543210", password: "secret12"),
            CancellationToken.None);

        var tokens = await auth.LoginWithPasswordAsync(
            new MemberPasswordLoginRequest("9876543210", "secret12"),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.Equal("+919876543210", tokens.Account.Phone);
    }

    [Fact]
    public async Task PasswordLogin_WithoutPassword_ReturnsPasswordNotSet()
    {
        var (auth, _, _, _) = CreateReadyAuth();
        await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.LoginWithPasswordAsync(
                new MemberPasswordLoginRequest("9876543210", "secret12"),
                CancellationToken.None));

        Assert.Equal("password_not_set", ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SetPassword_ThenPasswordLogin_Succeeds()
    {
        var (auth, sms, _, _) = CreateReadyAuth();
        var account = await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), null, CancellationToken.None);
        await auth.VerifyMemberOtpAsync(
            new VerifyMemberOtpRequest("9876543210", sms.LastCode!),
            CancellationToken.None);

        await auth.SetPasswordAsync(
            account.Id,
            new SetMemberPasswordRequest("secret12"),
            CancellationToken.None);

        var tokens = await auth.LoginWithPasswordAsync(
            new MemberPasswordLoginRequest("member@example.com", "secret12"),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
    }

    [Fact]
    public async Task ForgotAndResetPassword_IssuesTokensAndAllowsPasswordLogin()
    {
        var (auth, sms, _, _) = CreateReadyAuth();
        await auth.CreateMemberAsync(
            RegisterRequest("9876543210", password: "oldpass12"),
            CancellationToken.None);

        await auth.RequestPasswordResetAsync(
            new ForgotMemberPasswordRequest("9876543210"),
            "127.0.0.1",
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(sms.LastCode));

        var resetTokens = await auth.ResetPasswordAsync(
            new ResetMemberPasswordRequest("9876543210", sms.LastCode!, "newpass12"),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(resetTokens.AccessToken));
        Assert.True(resetTokens.Account.PhoneConfirmed);

        var loginTokens = await auth.LoginWithPasswordAsync(
            new MemberPasswordLoginRequest("9876543210", "newpass12"),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(loginTokens.AccessToken));
    }

    [Fact]
    public async Task ForgotPassword_UnknownIdentifier_DoesNotSendOtp()
    {
        var (auth, sms, email, _) = CreateReadyAuth();
        var response = await auth.RequestPasswordResetAsync(
            new ForgotMemberPasswordRequest("9111222333"),
            "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(300, response.ExpiresInSeconds);
        Assert.Null(sms.LastCode);
        Assert.Null(email.LastOtpCode);
    }

    [Fact]
    public async Task PasswordLogin_RejectsAdminAccount()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        users.Add(new UserRecord(
            Guid.NewGuid(),
            "+919876543210",
            true,
            "ops@example.com",
            true,
            "Admin",
            true,
            false,
            false, false,
            ["admin"]));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.LoginWithPasswordAsync(
                new MemberPasswordLoginRequest("9876543210", "secret12"),
                CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task AdminOtp_IssuesAdminAudienceTokens()
    {
        var (auth, sms, _, users) = CreateReadyAuth();
        users.Add(new UserRecord(
            Guid.NewGuid(),
            "+919876543210",
            true,
            "ops@example.com",
            true,
            "Admin",
            true,
            false,
            false, false,
            ["admin"]));

        await auth.RequestOtpAsync(new RequestAdminOtpRequest("9876543210"), "127.0.0.1", CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(sms.LastCode));

        var tokens = await auth.VerifyOtpAsync(
            new VerifyAdminOtpRequest("9876543210", sms.LastCode!),
            CancellationToken.None);

        Assert.Contains(":admin:otp", tokens.AccessToken);
        Assert.Equal("Admin", tokens.Account.AccountKind);
        Assert.Contains("admin", tokens.Account.Roles);
        Assert.True(tokens.Account.PhoneConfirmed);
    }

    [Fact]
    public async Task AdminOtp_RejectsMemberAccount()
    {
        var (auth, sms, _, _) = CreateReadyAuth();
        await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.RequestOtpAsync(new RequestAdminOtpRequest("9876543210"), "127.0.0.1", CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
        Assert.Null(sms.LastCode);
    }

    [Fact]
    public async Task AdminPasswordLogin_IssuesAdminAudienceTokens()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        users.Add(
            new UserRecord(
                Guid.NewGuid(),
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]),
            "secret12");

        var tokens = await auth.LoginWithPasswordAsync(
            new AdminPasswordLoginRequest("ops@example.com", "secret12"),
            CancellationToken.None);

        Assert.Contains(":admin:pwd", tokens.AccessToken);
        Assert.Equal("Admin", tokens.Account.AccountKind);
    }

    [Fact]
    public async Task AdminPasswordLogin_RejectsMemberAccount()
    {
        var (auth, _, _, _) = CreateReadyAuth();
        await auth.CreateMemberAsync(
            RegisterRequest("9876543210", password: "secret12"),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.LoginWithPasswordAsync(
                new AdminPasswordLoginRequest("9876543210", "secret12"),
                CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task CreateAdmin_AllowsMultipleAdmins()
    {
        var users = new FakeUserRepository();
        var first = await users.CreateAdminAsync(
            "ops@example.com",
            "+919876543210",
            "secret12",
            CancellationToken.None);
        var second = await users.CreateAdminAsync(
            "other@example.com",
            "+919811122233",
            "secret12",
            CancellationToken.None);

        Assert.False(first.IsSuperAdmin);
        Assert.False(second.IsSuperAdmin);
        Assert.True(await users.AnyAdminExistsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SuperAdmin_CanCreateAnotherAdmin()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var superId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                superId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                true, false,
                ["admin"]),
            "secret12");

        var created = await auth.CreateAdminAsync(
            superId,
            new CreateAdminRequest("other@example.com", "secret12", "9811122233"),
            CancellationToken.None);

        Assert.Equal("Admin", created.AccountKind);
        Assert.False(created.IsSuperAdmin);
        Assert.Equal("other@example.com", created.Email);
        Assert.Contains("admin", created.Roles);
    }

    [Fact]
    public async Task RegularAdmin_CannotCreateAdmin()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var actorId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                actorId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]),
            "secret12");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.CreateAdminAsync(
                actorId,
                new CreateAdminRequest("other@example.com", "secret12"),
                CancellationToken.None));

        Assert.Equal("super_admin_required", ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanListAndDeactivateAnotherAdmin()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var superId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                superId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                true, false,
                ["admin"]));
        users.Add(
            new UserRecord(
                targetId,
                "+919811122233",
                true,
                "other@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));

        var page = await auth.ListAdminsAsync(superId, new PagedQuery(), CancellationToken.None);
        Assert.Equal(2, page.TotalCount);

        var deactivated = await auth.DeactivateAdminAsync(superId, targetId, CancellationToken.None);
        Assert.False(deactivated.IsActive);

        var activated = await auth.ActivateAdminAsync(superId, targetId, CancellationToken.None);
        Assert.True(activated.IsActive);
    }

    [Fact]
    public async Task SuperAdmin_CannotDeactivateSelf()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var superId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                superId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                true, false,
                ["admin"]));
        users.Add(
            new UserRecord(
                Guid.NewGuid(),
                "+919811122233",
                true,
                "other@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.DeactivateAdminAsync(superId, superId, CancellationToken.None));

        Assert.Equal("cannot_deactivate_self", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CannotDeactivateLastActiveAdmin()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var superId = Guid.NewGuid();
        var onlyActive = Guid.NewGuid();
        users.Add(
            new UserRecord(
                superId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                false,
                false,
                true, false,
                ["admin"]));
        users.Add(
            new UserRecord(
                onlyActive,
                "+919811122233",
                true,
                "other@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.DeactivateAdminAsync(superId, onlyActive, CancellationToken.None));

        Assert.Equal("last_admin", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task AdminGetMe_ReturnsLiveSuperAdminFlag()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var adminId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                adminId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                true, false,
                ["admin"]));

        var me = await ((IAdminAuthService)auth).GetMeAsync(adminId, CancellationToken.None);

        Assert.Equal(adminId, me.Id);
        Assert.Equal("Admin", me.AccountKind);
        Assert.True(me.IsSuperAdmin);
        Assert.Equal("ops@example.com", me.Email);
        Assert.Contains("admin", me.Roles);
        Assert.Null(me.Profile);
    }

    [Fact]
    public async Task AdminGetMe_RejectsMemberAccount()
    {
        var (auth, _, _, _) = CreateReadyAuth();
        var member = await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            ((IAdminAuthService)auth).GetMeAsync(member.Id, CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Admin_CanListMembers_NewestFirst_ExcludesAdmins()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var adminId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                adminId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        users.AddMember(
            new MemberAdminRecord(
                Guid.NewGuid(),
                "+919811100001",
                true,
                "older@example.com",
                true,
                true,
                false,
                older,
                "Older",
                "Member",
                "Female",
                new DateOnly(1994, 2, 3),
                "Delhi",
                null));
        users.AddMember(
            new MemberAdminRecord(
                Guid.NewGuid(),
                "+919811100002",
                false,
                "newer@example.com",
                false,
                true,
                false,
                newer,
                "Newer",
                "Member",
                "Male",
                new DateOnly(1992, 6, 15),
                "Bangalore",
                "Hindu"));

        var page = await auth.ListMembersAsync(adminId, new MemberAdminListQuery(), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("newer@example.com", page.Items[0].Email);
        Assert.Equal("Older", page.Items[1].FirstName);
        Assert.Equal("Bangalore", page.Items[0].City);
        Assert.DoesNotContain(page.Items, item => item.Email == "ops@example.com");
    }

    [Fact]
    public async Task Admin_CanFilterAndSearchMembers()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var adminId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                adminId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                false,
                false,
                ["admin"]));
        users.AddMember(
            new MemberAdminRecord(
                Guid.NewGuid(),
                "+919811100011",
                true,
                "active@example.com",
                true,
                true,
                false,
                DateTimeOffset.UtcNow,
                "Active",
                "Person",
                "Female",
                new DateOnly(1994, 2, 3),
                "Delhi",
                null));
        var restrictedId = Guid.NewGuid();
        users.AddMember(
            new MemberAdminRecord(
                restrictedId,
                "+919811100012",
                true,
                "restricted@example.com",
                true,
                true,
                true,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                "Restricted",
                "Person",
                "Male",
                new DateOnly(1992, 6, 15),
                "Mumbai",
                null));

        var byEmail = await auth.ListMembersAsync(
            adminId,
            new MemberAdminListQuery { Search = "restricted@" },
            CancellationToken.None);
        Assert.Single(byEmail.Items);
        Assert.Equal(restrictedId, byEmail.Items[0].Id);

        var onlyRestricted = await auth.ListMembersAsync(
            adminId,
            new MemberAdminListQuery { IsRestricted = true },
            CancellationToken.None);
        Assert.Single(onlyRestricted.Items);
        Assert.True(onlyRestricted.Items[0].IsRestricted);

        var onlyActive = await auth.ListMembersAsync(
            adminId,
            new MemberAdminListQuery { IsActive = true },
            CancellationToken.None);
        Assert.Equal(2, onlyActive.TotalCount);
    }

    [Fact]
    public async Task ListMembers_RejectsMemberActor()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var member = await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);
        users.AddMember(
            new MemberAdminRecord(
                Guid.NewGuid(),
                "+919811100003",
                true,
                "listed@example.com",
                true,
                true,
                false,
                DateTimeOffset.UtcNow,
                "Listed",
                "Member",
                "Female",
                new DateOnly(1995, 1, 1),
                "Delhi",
                null));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.ListMembersAsync(member.Id, new MemberAdminListQuery(), CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Admin_CanGetMember_IncludesProfileAndEmptyMedia()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                adminId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));
        users.AddMember(
            new MemberAdminRecord(
                memberId,
                "+919811100004",
                true,
                "meera@example.com",
                true,
                true, false,
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                "Meera",
                "Shah",
                "Female",
                new DateOnly(1996, 4, 12),
                "Delhi",
                "Hindu"));

        var detail = await auth.GetMemberAsync(adminId, memberId, CancellationToken.None);

        Assert.Equal(memberId, detail.Id);
        Assert.Equal("Meera", detail.FirstName);
        Assert.Equal("Delhi", detail.City);
        Assert.Empty(detail.Photos);
        Assert.Null(detail.IntroductionVideo);
    }

    [Fact]
    public async Task GetMember_RejectsAdminTarget()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var adminId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                adminId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));
        users.Add(
            new UserRecord(
                otherAdminId,
                "+919811122233",
                true,
                "other@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.GetMemberAsync(adminId, otherAdminId, CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanRestrictAndUnrestrictMember()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var superId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                superId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                true, false,
                ["admin"]));
        users.AddMember(
            new MemberAdminRecord(
                memberId,
                "+919811100005",
                true,
                "riya@example.com",
                true,
                true, false,
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                "Riya",
                "Mehta",
                "Female",
                new DateOnly(1995, 1, 1),
                "Mumbai",
                "Hindu"));

        var restricted = await auth.RestrictMemberAsync(superId, memberId, CancellationToken.None);
        Assert.True(restricted.IsRestricted);
        Assert.True(restricted.IsActive);

        var unrestricted = await auth.UnrestrictMemberAsync(superId, memberId, CancellationToken.None);
        Assert.False(unrestricted.IsRestricted);
    }

    [Fact]
    public async Task RestrictMember_RequiresSuperAdmin()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                adminId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));
        users.AddMember(
            new MemberAdminRecord(
                memberId,
                "+919811100006",
                true,
                "riya2@example.com",
                true,
                true, false,
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                "Riya",
                "Mehta",
                "Female",
                new DateOnly(1995, 1, 1),
                "Mumbai",
                "Hindu"));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.RestrictMemberAsync(adminId, memberId, CancellationToken.None));

        Assert.Equal("super_admin_required", ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task RestrictMember_RejectsAdminTarget()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var superId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        users.Add(
            new UserRecord(
                superId,
                "+919876543210",
                true,
                "ops@example.com",
                true,
                "Admin",
                true,
                false,
                true, false,
                ["admin"]));
        users.Add(
            new UserRecord(
                otherAdminId,
                "+919811122233",
                true,
                "other@example.com",
                true,
                "Admin",
                true,
                false,
                false, false,
                ["admin"]));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.RestrictMemberAsync(superId, otherAdminId, CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Verify_RejectsNonMemberAudience()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 5,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions());
        var otp = new FakeOtpRepository(otpOptions.Value);
        var sms = new FakeSmsService();
        var users = new FakeUserRepository();
        var auth = CreateAuth(otp, users, new FakeRefreshSessionRepository(), new FakeTokenService(), sms, otpOptions, jwtOptions);

        await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), "127.0.0.1", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.VerifyMemberOtpAsync(
                new VerifyMemberOtpRequest("9876543210", sms.LastCode!, "admin"),
                CancellationToken.None));

        Assert.Equal("invalid_audience", ex.ErrorCode);
    }

    [Fact]
    public async Task Request_RejectsAdminAccountOnMemberDoor()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 5,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions());
        var otp = new FakeOtpRepository(otpOptions.Value);
        var sms = new FakeSmsService();
        var users = new FakeUserRepository();
        var id = Guid.NewGuid();
        users.Add(new UserRecord(id, "+919876543210", true, "ops@example.com", true, "Admin", true, false, false, false, ["admin"]));
        var auth = CreateAuth(otp, users, new FakeRefreshSessionRepository(), new FakeTokenService(), sms, otpOptions, jwtOptions);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), "127.0.0.1", CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
        Assert.Null(sms.LastCode);
    }

    [Fact]
    public async Task Request_RejectsUnregisteredPhone()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            MaxRequestsPerPhonePerHour = 5,
            MaxRequestsPerIpPerHour = 20,
            TtlSeconds = 300,
            CodeLength = 6,
            MaxAttempts = 5
        });
        var jwtOptions = Options.Create(new JwtOptions());
        var auth = new AuthService(
            new FakeOtpRepository(otpOptions.Value),
            new FakeUserRepository(),
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            new FakeSmsService(),
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), null, CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Request_RateLimitsPerPhone()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            MaxRequestsPerPhonePerHour = 2,
            MaxRequestsPerIpPerHour = 100,
            TtlSeconds = 300,
            CodeLength = 6,
            MaxAttempts = 5
        });
        var jwtOptions = Options.Create(new JwtOptions());
        var otp = new FakeOtpRepository(otpOptions.Value);
        var sms = new FakeSmsService();
        var users = new FakeUserRepository();
        var auth = new AuthService(
            otp,
            users,
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            sms,
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), null, CancellationToken.None);
        await auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), null, CancellationToken.None));

        Assert.Equal("otp_rate_limited", ex.ErrorCode);
        Assert.Equal(429, ex.StatusCode);
    }

    [Fact]
    public async Task Verify_RejectsInvalidCode_ThenLocks()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            MaxAttempts = 2,
            TtlSeconds = 300,
            CodeLength = 6,
            MaxRequestsPerPhonePerHour = 10,
            MaxRequestsPerIpPerHour = 10
        });
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var otp = new FakeOtpRepository(otpOptions.Value);
        var sms = new FakeSmsService();
        var auth = new AuthService(
            otp,
            new FakeUserRepository(),
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            sms,
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await auth.RequestMemberOtpAsync(new RequestMemberOtpRequest("9876543210"), null, CancellationToken.None);

        var first = await Assert.ThrowsAsync<AuthException>(() =>
            auth.VerifyMemberOtpAsync(new VerifyMemberOtpRequest("9876543210", "000000"), CancellationToken.None));
        Assert.Equal("otp_invalid", first.ErrorCode);

        var second = await Assert.ThrowsAsync<AuthException>(() =>
            auth.VerifyMemberOtpAsync(new VerifyMemberOtpRequest("9876543210", "000000"), CancellationToken.None));
        Assert.Equal("otp_locked", second.ErrorCode);
    }

    [Fact]
    public async Task CreateMember_CreatesAccount()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 10,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var users = new FakeUserRepository();
        var auth = new AuthService(
            new FakeOtpRepository(otpOptions.Value),
            users,
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            new FakeSmsService(),
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        var account = await auth.CreateMemberAsync(
            RegisterRequest("9876543210", "member@example.com"),
            CancellationToken.None);

        Assert.Equal("+919876543210", account.Phone);
        Assert.Equal("member@example.com", account.Email);
        Assert.False(account.PhoneConfirmed);
        Assert.False(account.EmailConfirmed);
        Assert.Contains("member", account.Roles);
        Assert.NotNull(account.Profile);
        Assert.Equal("Ada", account.Profile!.FirstName);
        Assert.Equal("Mumbai", account.Profile.City);
        Assert.NotNull(await users.FindByPhoneAsync("+919876543210", CancellationToken.None));
    }

    [Fact]
    public async Task CreateMember_SendsVerificationEmail_AndConfirmEmail()
    {
        var otpOptions = Options.Create(new OtpOptions());
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var email = new FakeEmailService();
        var users = new FakeUserRepository();
        var auth = new AuthService(
            new FakeOtpRepository(otpOptions.Value),
            users,
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            new FakeSmsService(),
            email,
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        var account = await auth.CreateMemberAsync(
            RegisterRequest("9876543210", "verify.me@example.com"),
            CancellationToken.None);

        Assert.False(account.EmailConfirmed);
        Assert.Equal("verify.me@example.com", email.LastEmail);
        Assert.False(string.IsNullOrWhiteSpace(email.LastVerifyUrl));
        Assert.Contains("userId=", email.LastVerifyUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token=", email.LastVerifyUrl, StringComparison.OrdinalIgnoreCase);

        var uri = new Uri(email.LastVerifyUrl!);
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]), StringComparer.OrdinalIgnoreCase);

        var userId = Guid.Parse(query["userId"]);
        var token = query["token"];

        var confirmed = await auth.ConfirmEmailAsync(
            new ConfirmEmailRequest(userId, token),
            CancellationToken.None);

        Assert.True(confirmed.EmailConfirmed);
        Assert.Equal(account.Id, confirmed.Id);
    }

    [Fact]
    public async Task CreateMember_RejectsUnderage()
    {
        var otpOptions = Options.Create(new OtpOptions());
        var jwtOptions = Options.Create(new JwtOptions());
        var auth = new AuthService(
            new FakeOtpRepository(otpOptions.Value),
            new FakeUserRepository(),
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            new FakeSmsService(),
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        var underage = new CreateMemberRequest(
            "9876543210",
            "Kid",
            "User",
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)),
            "Mumbai",
            "kid@example.com");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.CreateMemberAsync(underage, CancellationToken.None));

        Assert.Equal("underage", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateMember_RejectsDuplicatePhone()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 10,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var auth = new AuthService(
            new FakeOtpRepository(otpOptions.Value),
            new FakeUserRepository(),
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            new FakeSmsService(),
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None));

        Assert.Equal("user_already_exists", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteMember_AllowsReregisterAsNewAccount()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 10,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var users = new FakeUserRepository();
        var auth = new AuthService(
            new FakeOtpRepository(otpOptions.Value),
            users,
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            new FakeSmsService(),
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        var first = await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await auth.DeleteMemberAsync(first.Id, CancellationToken.None);

        var second = await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.True(second.IsActive);
        Assert.False(second.IsDeleted);
    }

    [Fact]
    public async Task CreateMember_WhenDeactivated_IsBlocked()
    {
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            TtlSeconds = 300,
            MaxAttempts = 5,
            MaxRequestsPerPhonePerHour = 10,
            MaxRequestsPerIpPerHour = 20
        });
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var users = new FakeUserRepository();
        var auth = new AuthService(
            new FakeOtpRepository(otpOptions.Value),
            users,
            new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(),
            new FakeIntroductionVideoRepository(),
            new FakeRefreshSessionRepository(),
            new FakeTokenService(),
            new FakeSmsService(),
            new FakeEmailService(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance,
            otpOptions,
            jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));

        var account = await auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await auth.DeactivateMemberAsync(account.Id, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            auth.CreateMemberAsync(RegisterRequest("9876543210"), CancellationToken.None));

        Assert.Equal("account_deactivated", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }
}

sealed class FakeSmsService : ISmsService
{
    public string? LastCode { get; private set; }
    public string? LastPhone { get; private set; }

    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        LastPhone = phoneE164;
        LastCode = code;
        return Task.CompletedTask;
    }
}

sealed class FakeEmailService : IEmailService
{
    public string? LastEmail { get; private set; }
    public string? LastVerifyUrl { get; private set; }
    public string? LastOtpCode { get; private set; }

    public Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken)
    {
        LastEmail = email;
        LastVerifyUrl = verifyUrl;
        return Task.CompletedTask;
    }

    public Task SendOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        LastEmail = email;
        LastOtpCode = code;
        return Task.CompletedTask;
    }
}

file sealed class FakeTokenService : ITokenService
{
    public AccessTokenResult CreateAccessToken(
        UserRecord user,
        string audience,
        Guid sessionId,
        string amr,
        DateTimeOffset authTimeUtc) =>
        new($"access-{user.Id}:{audience}:{amr}", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(15));

    public IssuedRefreshToken CreateRefreshToken()
    {
        var raw = $"refresh-{Guid.NewGuid():N}";
        return new IssuedRefreshToken(
            Guid.NewGuid(),
            Guid.NewGuid(),
            raw,
            TokenHasher.Hash(raw),
            DateTimeOffset.UtcNow);
    }
}

file sealed class FakeOtpRepository : IOtpChallengeRepository
{
    private readonly OtpOptions _options;
    private readonly Dictionary<string, OtpChallenge> _challenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _identifierCounts = new(StringComparer.Ordinal);

    public FakeOtpRepository(OtpOptions options)
    {
        _options = options;
    }

    public Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireRequestSlotAsync(
        string channel,
        string destination,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var key = Key(channel, destination);
        _identifierCounts.TryGetValue(key, out var count);
        count++;
        _identifierCounts[key] = count;
        if (count > _options.MaxRequestsPerPhonePerHour)
        {
            return Task.FromResult<(bool, int?)>((false, 3600));
        }

        return Task.FromResult<(bool, int?)>((true, null));
    }

    public Task StoreAsync(OtpChallenge challenge, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _challenges[Key(challenge.Channel, challenge.Destination)] = challenge;
        return Task.CompletedTask;
    }

    public Task<OtpChallenge?> GetAsync(string channel, string destination, CancellationToken cancellationToken) =>
        Task.FromResult(_challenges.TryGetValue(Key(channel, destination), out var c) ? c : null);

    public Task<bool> IncrementAttemptsAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        var key = Key(channel, destination);
        if (!_challenges.TryGetValue(key, out var challenge))
        {
            return Task.FromResult(false);
        }

        var updated = challenge with { Attempts = challenge.Attempts + 1 };
        if (updated.Attempts >= _options.MaxAttempts)
        {
            _challenges.Remove(key);
            return Task.FromResult(false);
        }

        _challenges[key] = updated;
        return Task.FromResult(true);
    }

    public Task RemoveAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        _challenges.Remove(Key(channel, destination));
        return Task.CompletedTask;
    }

    private static string Key(string channel, string destination) => $"{channel}:{destination}";
}

sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, UserRecord> _byId = new();
    private readonly Dictionary<string, Guid> _byPhone = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _emailTokens = new();
    private readonly Dictionary<Guid, string> _passwords = new();
    private readonly Dictionary<Guid, int> _failedAttempts = new();
    private readonly Dictionary<Guid, MemberAdminRecord> _members = new();

    public void Add(UserRecord user, string? password = null)
    {
        _byId[user.Id] = user;
        if (!string.IsNullOrWhiteSpace(user.Phone))
        {
            _byPhone[user.Phone] = user.Id;
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            _passwords[user.Id] = password;
        }

        if (string.Equals(user.AccountKind, "Member", StringComparison.Ordinal)
            && !user.IsDeleted
            && !_members.ContainsKey(user.Id))
        {
            _members[user.Id] = new MemberAdminRecord(
                user.Id,
                user.Phone,
                user.PhoneConfirmed,
                user.Email,
                user.EmailConfirmed,
                user.IsActive,
                user.IsRestricted, DateTimeOffset.UtcNow,
                null,
                null,
                null,
                null,
                null,
                null);
        }
    }

    public void AddMember(MemberAdminRecord member)
    {
        Add(
            new UserRecord(
                member.Id,
                member.Phone,
                member.PhoneConfirmed,
                member.Email,
                member.EmailConfirmed,
                "Member",
                member.IsActive,
                false,
                false,
                member.IsRestricted,
                ["member"]));
        _members[member.Id] = member;
    }

    public Task<UserRecord?> FindByPhoneAsync(string phoneE164, CancellationToken cancellationToken)
    {
        if (_byPhone.TryGetValue(phoneE164, out var id)
            && _byId.TryGetValue(id, out var user)
            && !user.IsDeleted)
        {
            return Task.FromResult<UserRecord?>(user);
        }

        return Task.FromResult<UserRecord?>(null);
    }

    public Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim();
        var user = _byId.Values.FirstOrDefault(u =>
            !u.IsDeleted
            && !string.IsNullOrWhiteSpace(u.Email)
            && string.Equals(u.Email, normalized, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user);
    }

    public Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_byId.TryGetValue(userId, out var user) && !user.IsDeleted)
        {
            return Task.FromResult<UserRecord?>(user);
        }

        return Task.FromResult<UserRecord?>(null);
    }

    public Task<UserRecord> CreateMemberAsync(
        string phoneE164,
        string? email,
        string? password,
        CancellationToken cancellationToken)
    {
        if (_byPhone.TryGetValue(phoneE164, out var existingId)
            && _byId.TryGetValue(existingId, out var existing)
            && !existing.IsDeleted)
        {
            if (!existing.IsActive)
            {
                throw new AuthException(
                    "account_deactivated",
                    "This account is deactivated. Activate it before signing in; re-registration is not allowed.",
                    statusCode: 409);
            }

            throw new AuthException(
                "user_already_exists",
                "A member account with this phone already exists.",
                statusCode: 409);
        }

        if (!string.IsNullOrWhiteSpace(email)
            && _byId.Values.Any(u =>
                !u.IsDeleted
                && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AuthException(
                "email_already_exists",
                "A member account with this email already exists.",
                statusCode: 409);
        }

        if (_byPhone.TryGetValue(phoneE164, out var deletedId)
            && _byId.TryGetValue(deletedId, out var deleted)
            && deleted.IsDeleted)
        {
            _byPhone.Remove(phoneE164);
        }

        var id = Guid.NewGuid();
        var user = new UserRecord(id, phoneE164, false, email, false, "Member", true, false, false, false, ["member"]);
        Add(user, password);
        return Task.FromResult(user);
    }

    public Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _byId[userId];
        _byId[userId] = user with { PhoneConfirmed = true };
        return Task.CompletedTask;
    }

    public Task MarkEmailConfirmedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _byId[userId];
        _byId[userId] = user with { EmailConfirmed = true };
        return Task.CompletedTask;
    }

    public Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_byId.TryGetValue(userId, out var user) || user.IsDeleted)
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new AuthException("email_missing", "Account has no email to confirm.", statusCode: 400);
        }

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        _emailTokens[userId] = token;
        return Task.FromResult(token);
    }

    public Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        if (!_byId.TryGetValue(userId, out var user) || user.IsDeleted)
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        if (user.EmailConfirmed)
        {
            return Task.CompletedTask;
        }

        if (!_emailTokens.TryGetValue(userId, out var expected)
            || !string.Equals(expected, token, StringComparison.Ordinal))
        {
            throw new AuthException(
                "email_token_invalid",
                "Email verification link is invalid or expired.",
                statusCode: 400);
        }

        _byId[userId] = user with { EmailConfirmed = true };
        _emailTokens.Remove(userId);
        return Task.CompletedTask;
    }

    public Task TouchLastLoginAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SoftDeleteMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_byId.TryGetValue(userId, out var user))
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        if (user.Phone is not null)
        {
            _byPhone.Remove(user.Phone);
        }

        _byId[userId] = user with { IsDeleted = true, IsActive = false, PhoneConfirmed = false };
        return Task.CompletedTask;
    }

    public Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _byId[userId];
        _byId[userId] = user with { IsActive = false };
        if (_members.TryGetValue(userId, out var member))
        {
            _members[userId] = member with { IsActive = false };
        }

        return Task.CompletedTask;
    }

    public Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _byId[userId];
        if (user.IsDeleted)
        {
            throw new AuthException(
                "account_deleted",
                "This account was deleted. Register again to create a new account.",
                statusCode: 409);
        }

        _byId[userId] = user with { IsActive = true };
        if (_members.TryGetValue(userId, out var member))
        {
            _members[userId] = member with { IsActive = true };
        }

        return Task.CompletedTask;
    }

    public Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_passwords.ContainsKey(userId));

    public Task SetPasswordAsync(
        Guid userId,
        string password,
        string? currentPassword,
        CancellationToken cancellationToken)
    {
        if (_passwords.ContainsKey(userId))
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                throw new AuthException(
                    "password_current_required",
                    "Current password is required to change your password.",
                    statusCode: 400);
            }

            if (!string.Equals(_passwords[userId], currentPassword, StringComparison.Ordinal))
            {
                throw new AuthException("password_invalid", "Current password is incorrect.", statusCode: 401);
            }
        }

        _passwords[userId] = password;
        return Task.CompletedTask;
    }

    public Task<PasswordCheckResult> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken)
    {
        if (_failedAttempts.TryGetValue(userId, out var fails) && fails >= 10)
        {
            return Task.FromResult(PasswordCheckResult.LockedOut);
        }

        if (!_passwords.TryGetValue(userId, out var stored))
        {
            return Task.FromResult(PasswordCheckResult.NotSet);
        }

        if (!string.Equals(stored, password, StringComparison.Ordinal))
        {
            _failedAttempts[userId] = fails + 1;
            return Task.FromResult(
                _failedAttempts[userId] >= 10
                    ? PasswordCheckResult.LockedOut
                    : PasswordCheckResult.Invalid);
        }

        _failedAttempts.Remove(userId);
        return Task.FromResult(PasswordCheckResult.Success);
    }

    public Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        _passwords[userId] = newPassword;
        _failedAttempts.Remove(userId);
        return Task.CompletedTask;
    }

    public Task<bool> AnyAdminExistsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_byId.Values.Any(u =>
            !u.IsDeleted && string.Equals(u.AccountKind, "Admin", StringComparison.Ordinal)));

    public Task<(IReadOnlyList<UserRecord> Items, int TotalCount)> ListAdminsPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var admins = _byId.Values
            .Where(u => !u.IsDeleted && string.Equals(u.AccountKind, "Admin", StringComparison.Ordinal))
            .OrderBy(u => u.Email)
            .ThenBy(u => u.Id)
            .ToList();
        return Task.FromResult<(IReadOnlyList<UserRecord>, int)>(
            (admins.Skip(skip).Take(take).ToList(), admins.Count));
    }

    public Task<(IReadOnlyList<MemberAdminRecord> Items, int TotalCount)> ListMembersPageAsync(
        int skip,
        int take,
        string? search,
        bool? isActive,
        bool? isRestricted,
        CancellationToken cancellationToken)
    {
        IEnumerable<MemberAdminRecord> members = _members.Values
            .Where(member =>
                _byId.TryGetValue(member.Id, out var user)
                && !user.IsDeleted
                && string.Equals(user.AccountKind, "Member", StringComparison.Ordinal));

        if (isActive is not null)
        {
            members = members.Where(member => member.IsActive == isActive.Value);
        }

        if (isRestricted is not null)
        {
            members = members.Where(member => member.IsRestricted == isRestricted.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            members = members.Where(member =>
                (member.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (member.Phone?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (member.FirstName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (member.LastName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = members
            .OrderByDescending(member => member.CreatedAtUtc)
            .ThenBy(member => member.Id)
            .ToList();
        return Task.FromResult<(IReadOnlyList<MemberAdminRecord>, int)>(
            (list.Skip(skip).Take(take).ToList(), list.Count));
    }

    public Task<MemberAdminRecord?> FindMemberAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_members.TryGetValue(userId, out var member)
            && _byId.TryGetValue(userId, out var user)
            && !user.IsDeleted
            && string.Equals(user.AccountKind, "Member", StringComparison.Ordinal))
        {
            return Task.FromResult<MemberAdminRecord?>(member);
        }

        return Task.FromResult<MemberAdminRecord?>(null);
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_byId.Values.Count(u =>
            !u.IsDeleted
            && u.IsActive
            && string.Equals(u.AccountKind, "Admin", StringComparison.Ordinal)));

    public Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_byId.Values.Count(u =>
            !u.IsDeleted
            && u.IsActive
            && u.IsSuperAdmin
            && string.Equals(u.AccountKind, "Admin", StringComparison.Ordinal)));

    public Task<UserRecord> CreateAdminAsync(
        string email,
        string? phoneE164,
        string password,
        CancellationToken cancellationToken,
        bool isSuperAdmin = false)
    {
        if (!string.IsNullOrWhiteSpace(phoneE164)
            && _byPhone.TryGetValue(phoneE164, out var existingPhoneId)
            && _byId.TryGetValue(existingPhoneId, out var existingPhone)
            && !existingPhone.IsDeleted)
        {
            throw new AuthException("user_already_exists", "An account with this phone already exists.", statusCode: 409);
        }

        if (_byId.Values.Any(u =>
            !u.IsDeleted
            && !string.IsNullOrWhiteSpace(u.Email)
            && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AuthException("email_already_exists", "An account with this email already exists.", statusCode: 409);
        }

        var id = Guid.NewGuid();
        var user = new UserRecord(
            id,
            phoneE164,
            phoneE164 is not null,
            email,
            true,
            "Admin",
            true,
            false,
            isSuperAdmin,
            false,
            ["admin"]);
        Add(user, password);
        return Task.FromResult(user);
    }

    public Task RestrictMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _byId[userId];
        _byId[userId] = user with { IsRestricted = true };
        if (_members.TryGetValue(userId, out var member))
        {
            _members[userId] = member with { IsRestricted = true };
        }

        return Task.CompletedTask;
    }

    public Task UnrestrictMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _byId[userId];
        _byId[userId] = user with { IsRestricted = false };
        if (_members.TryGetValue(userId, out var member))
        {
            _members[userId] = member with { IsRestricted = false };
        }

        return Task.CompletedTask;
    }
}

file sealed class FakeMemberProfileRepository : IMemberProfileRepository
{
    private readonly Dictionary<Guid, MemberProfileRecord> _byUserId = new();

    public Task<MemberProfileRecord> CreateAsync(MemberProfileRecord profile, CancellationToken cancellationToken)
    {
        _byUserId[profile.UserId] = profile;
        return Task.FromResult(profile);
    }

    public Task<MemberProfileRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_byUserId.TryGetValue(userId, out var profile) ? profile : null);

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _byUserId.Remove(userId);
        return Task.CompletedTask;
    }
}

file sealed class FakeMemberPhotoRepository : IMemberPhotoRepository
{
    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<MemberPhotoRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemberPhotoRecord>>([]);

    public Task<MemberPhotoRecord?> FindByIdAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken) =>
        Task.FromResult<MemberPhotoRecord?>(null);

    public Task<MemberPhotoRecord?> FindReferenceAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<MemberPhotoRecord?>(null);

    public Task<int> NextSortOrderAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<MemberPhotoRecord> AddAsync(MemberPhotoRecord photo, CancellationToken cancellationToken) =>
        Task.FromResult(photo);

    public Task SoftDeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task PromoteNextReferenceAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

file sealed class FakeIntroductionVideoRepository : IIntroductionVideoRepository
{
    public Task<IntroductionVideoRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IntroductionVideoRecord?>(null);

    public Task<IntroductionVideoRecord> UpsertAsync(
        IntroductionVideoRecord video,
        CancellationToken cancellationToken) =>
        Task.FromResult(video);

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

file sealed class FakeRefreshSessionRepository : IRefreshSessionRepository
{
    private readonly Dictionary<Guid, RefreshSessionRecord> _sessions = new();
    private readonly HashSet<Guid> _softDeleted = new();

    public Task AddAsync(RefreshSessionRecord session, CancellationToken cancellationToken)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<RefreshSessionRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.Values.FirstOrDefault(s =>
            s.TokenHash == tokenHash && !_softDeleted.Contains(s.Id)));

    public Task MarkReplacedAsync(Guid sessionId, Guid replacedBySessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with
            {
                ReplacedAtUtc = DateTimeOffset.UtcNow,
                ReplacedBySessionId = replacedBySessionId
            };
        }

        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with { RevokedAtUtc = DateTimeOffset.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        foreach (var pair in _sessions.Where(x => x.Value.FamilyId == familyId).ToList())
        {
            _sessions[pair.Key] = pair.Value with { RevokedAtUtc = DateTimeOffset.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var pair in _sessions.Where(x => x.Value.UserId == userId).ToList())
        {
            _sessions[pair.Key] = pair.Value with { RevokedAtUtc = DateTimeOffset.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var pair in _sessions.Where(x => x.Value.UserId == userId).ToList())
        {
            _sessions[pair.Key] = pair.Value with { RevokedAtUtc = DateTimeOffset.UtcNow };
            _softDeleted.Add(pair.Key);
        }

        return Task.CompletedTask;
    }
}

