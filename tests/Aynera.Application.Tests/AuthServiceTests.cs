using Aynera.Application.Common;
using Aynera.Application.Features.Users.Services.Interfaces;
using AutoMapper;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Application.Features.Users.Services.Implementations;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Implementations;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Videos.Repositories;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Photos.Records;
using Aynera.Domain.Videos.Records;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Tests;

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
    private static readonly ConditionalWeakTable<AuthService, AccountLifecycleService> Lifecycles = new();
    private static readonly ConditionalWeakTable<AuthService, RegistrationService> Registrations = new();
    private static readonly ConditionalWeakTable<AuthService, TestVerificationQueue> Queues = new();
    private static readonly ConditionalWeakTable<AuthService, FakeEarlyAccessCityRepository> Cities = new();
    private static RegistrationService RegistrationFor(AuthService auth) => Registrations.GetValue(auth, _ => throw new InvalidOperationException());
    private static FakeEarlyAccessCityRepository CitiesFor(AuthService auth) => Cities.GetValue(auth, _ => throw new InvalidOperationException());

    private static AuthService CreateAuthWithRegistration(
        IOtpChallengeRepository otp, IUserRepository users, IMemberProfileRepository profiles,
        IMemberPhotoRepository photos, IIntroductionVideoRepository videos,
        IRefreshSessionRepository sessions, ITokenService tokens, ISmsService sms, IEmailService email,
        IMapper mapper, IAuditWriter audit, ILogger<AuthService> logger,
        IOptions<OtpOptions> otpOptions, IOptions<JwtOptions> jwtOptions, IOptions<EmailOptions> emailOptions)
    {
        var auth = new AuthService(otp, users, profiles, sessions, tokens, sms, email,
            mapper, audit, logger, otpOptions, jwtOptions, new TestWorkflowTransaction());
        var queue = new TestVerificationQueue();
        Lifecycles.Add(auth, new AccountLifecycleService(users, profiles, new FakeMemberPreferencesRepository(), photos, videos, sessions, audit,
            DiscardLogger<AccountLifecycleService>.Instance, new TestWorkflowTransaction(), queue,
            otp, sms, email, otpOptions, jwtOptions));
        Queues.Add(auth, queue);
        var cities = new FakeEarlyAccessCityRepository();
        Cities.Add(auth, cities);
        Registrations.Add(auth, new RegistrationService(users, profiles, cities, new TestWorkflowTransaction(), queue,
            mapper, audit, emailOptions, DiscardLogger<RegistrationService>.Instance,
            otp, sms, email, auth, otpOptions, jwtOptions));
        return auth;
    }

    private sealed class TestVerificationQueue : IVerificationEmailQueue
    {
        public Task CancelAsync(Guid userId, CancellationToken cancellationToken)
        { Users.Remove(userId); return Task.CompletedTask; }
        public List<Guid> Users { get; } = [];
        public Task EnqueueAsync(Guid userId, CancellationToken cancellationToken)
        { Users.Add(userId); return Task.CompletedTask; }
    }

    // Unit tests use shared in-memory stores; rollback guarantees are tested with real PostgreSQL.
    private sealed class TestWorkflowTransaction : IWorkflowTransaction
    {
        public Task<T> ExecuteAsync<T>(IReadOnlyList<string> keys, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }

    private static UserManagementService CreateUserManagement(IUserRepository users) =>
        new(users, new FakePhotoReads(), new FakeVideoReads(), new FakeRefreshSessionRepository(),
            TestMapper.Instance, NoopAuditWriter.Instance, DiscardLogger<UserManagementService>.Instance,
            Options.Create(new JwtOptions()), new TestWorkflowTransaction());

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MemberMedia_RequiresAdminActorAndExistingMember(bool adminActor, bool existingMember)
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var member = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
        var actorId = member.Id;
        if (adminActor)
        {
            actorId = Guid.NewGuid();
            users.Add(new UserRecord(actorId, null, false, "admin@example.com", true,
                "Admin", true, false, false, false, ["admin"]));
        }
        var targetId = existingMember ? member.Id : Guid.NewGuid();
        var service = CreateUserManagement(users);
        var photoId = Guid.NewGuid();
        if (adminActor && existingMember)
        {
            var photo = await service.GetMemberPhotoAsync(actorId, targetId, photoId, CancellationToken.None);
            var video = await service.GetMemberVideoAsync(actorId, targetId, CancellationToken.None);
            Assert.Equal(photoId, photo.Id);
            Assert.Equal("image/jpeg", photo.ContentType);
            Assert.Equal(new byte[] { 1, 2, 3 }, photo.Data);
            Assert.Equal(targetId, video.UserId);
            Assert.Equal("video/mp4", video.ContentType);
        }
        else
        {
            var photoError = await Assert.ThrowsAsync<AuthException>(() =>
                service.GetMemberPhotoAsync(actorId, targetId, photoId, CancellationToken.None));
            var videoError = await Assert.ThrowsAsync<AuthException>(() =>
                service.GetMemberVideoAsync(actorId, targetId, CancellationToken.None));
            Assert.Equal("user_not_found", photoError.ErrorCode);
            Assert.Equal("user_not_found", videoError.ErrorCode);
        }
    }

    [Fact]
    public async Task Restriction_RevokesSessionsAndAuditsOnlyStateChanges()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var member = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
        var actorId = Guid.NewGuid();
        users.Add(new UserRecord(actorId, null, false, "admin@example.com", true,
            "Admin", true, false, true, false, ["admin"]));
        var sessions = new FakeRefreshSessionRepository();
        await sessions.AddAsync(new RefreshSessionRecord(Guid.NewGuid(), member.Id, "member", "test-token",
            Guid.NewGuid(), null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), null, null, null), CancellationToken.None);
        var audit = new CapturingAuditWriter();
        var service = new UserManagementService(users, new FakePhotoReads(), new FakeVideoReads(), sessions,
            TestMapper.Instance, audit, DiscardLogger<UserManagementService>.Instance, Options.Create(new JwtOptions()),
            new TestWorkflowTransaction());

        await service.RestrictMemberAsync(actorId, member.Id, CancellationToken.None);
        await service.RestrictMemberAsync(actorId, member.Id, CancellationToken.None);
        Assert.NotNull((await sessions.FindByTokenHashAsync("test-token", CancellationToken.None))!.RevokedAtUtc);
        var restricted = Assert.Single(audit.Events);
        Assert.Equal(Aynera.Domain.Audit.Statics.AuditActions.MemberRestricted, restricted.Action);
        Assert.Equal(actorId, restricted.UserId);
        Assert.Equal(member.Id, restricted.SubjectUserId);

        await service.UnrestrictMemberAsync(actorId, member.Id, CancellationToken.None);
        await service.UnrestrictMemberAsync(actorId, member.Id, CancellationToken.None);
        Assert.Equal(2, audit.Events.Count);
        Assert.Equal(Aynera.Domain.Audit.Statics.AuditActions.MemberUnrestricted, audit.Events[1].Action);
    }

    private static CreateMemberRequest RegisterRequest(
        string phone,
        string email = "member@example.com",
        string? password = null) =>
        new(
            phone,
            "Ada Lovelace",
            Gender.Female,
            new DateOnly(1990, 5, 15),
            "Mumbai",
            email,
            Hometown: "Pune",
            Religion: "Hindu",
            Password: password);

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
        CreateAuthWithRegistration(
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
            Issuer = "aynera-api",
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
            Issuer = "aynera-api",
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
        await RegistrationFor(auth).RegisterAsync(RegisterRequest(phone), CancellationToken.None);
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
        await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210", address), CancellationToken.None);
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
        await RegistrationFor(auth).RegisterAsync(
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
        await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);

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
        var account = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
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
        await RegistrationFor(auth).RegisterAsync(
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
        await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);

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
        await RegistrationFor(auth).RegisterAsync(
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

        var created = await CreateUserManagement(users).CreateAdminAsync(
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
            CreateUserManagement(users).CreateAdminAsync(
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

        var page = await CreateUserManagement(users).ListAdminsAsync(superId, new PagedQuery(), CancellationToken.None);
        Assert.Equal(2, page.TotalCount);

        var deactivated = await CreateUserManagement(users).DeactivateAdminAsync(superId, targetId, CancellationToken.None);
        Assert.False(deactivated.IsActive);

        var activated = await CreateUserManagement(users).ActivateAdminAsync(superId, targetId, CancellationToken.None);
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
            CreateUserManagement(users).DeactivateAdminAsync(superId, superId, CancellationToken.None));

        Assert.Equal("cannot_deactivate_self", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task InactiveSuperAdmin_CannotDeactivateAnAdmin()
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
            CreateUserManagement(users).DeactivateAdminAsync(superId, onlyActive, CancellationToken.None));

        Assert.Equal("super_admin_required", ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
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

        var me = await CreateUserManagement(users).GetAdminMeAsync(adminId, CancellationToken.None);

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
        var (auth, _, _, users) = CreateReadyAuth();
        var member = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateUserManagement(users).GetAdminMeAsync(member.Id, CancellationToken.None));

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
                "Older Member",
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
                "Newer Member",
                "Male",
                new DateOnly(1992, 6, 15),
                "Bangalore",
                "Hindu"));

        var page = await CreateUserManagement(users).ListMembersAsync(adminId, new MemberAdminListQuery(), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("newer@example.com", page.Items[0].Email);
        Assert.Equal("Older Member", page.Items[1].Name);
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
                "Active Person",
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
                "Restricted Person",
                "Male",
                new DateOnly(1992, 6, 15),
                "Mumbai",
                null));

        var byEmail = await CreateUserManagement(users).ListMembersAsync(
            adminId,
            new MemberAdminListQuery { Search = "restricted@" },
            CancellationToken.None);
        Assert.Single(byEmail.Items);
        Assert.Equal(restrictedId, byEmail.Items[0].Id);

        var onlyRestricted = await CreateUserManagement(users).ListMembersAsync(
            adminId,
            new MemberAdminListQuery { IsRestricted = true },
            CancellationToken.None);
        Assert.Single(onlyRestricted.Items);
        Assert.True(onlyRestricted.Items[0].IsRestricted);

        var onlyActive = await CreateUserManagement(users).ListMembersAsync(
            adminId,
            new MemberAdminListQuery { IsActive = true },
            CancellationToken.None);
        Assert.Equal(2, onlyActive.TotalCount);
    }

    [Fact]
    public async Task ListMembers_RejectsMemberActor()
    {
        var (auth, _, _, users) = CreateReadyAuth();
        var member = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
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
                "Listed Member",
                "Female",
                new DateOnly(1995, 1, 1),
                "Delhi",
                null));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateUserManagement(users).ListMembersAsync(member.Id, new MemberAdminListQuery(), CancellationToken.None));

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
                "Meera Shah",
                "Female",
                new DateOnly(1996, 4, 12),
                "Delhi",
                "Hindu"));

        var detail = await CreateUserManagement(users).GetMemberAsync(adminId, memberId, CancellationToken.None);

        Assert.Equal(memberId, detail.Id);
        Assert.Equal("Meera Shah", detail.Name);
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
            CreateUserManagement(users).GetMemberAsync(adminId, otherAdminId, CancellationToken.None));

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
                "Riya Mehta",
                "Female",
                new DateOnly(1995, 1, 1),
                "Mumbai",
                "Hindu"));

        var restricted = await CreateUserManagement(users).RestrictMemberAsync(superId, memberId, CancellationToken.None);
        Assert.True(restricted.IsRestricted);
        Assert.True(restricted.IsActive);

        var unrestricted = await CreateUserManagement(users).UnrestrictMemberAsync(superId, memberId, CancellationToken.None);
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
                "Riya Mehta",
                "Female",
                new DateOnly(1995, 1, 1),
                "Mumbai",
                "Hindu"));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            CreateUserManagement(users).RestrictMemberAsync(adminId, memberId, CancellationToken.None));

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
            CreateUserManagement(users).RestrictMemberAsync(superId, otherAdminId, CancellationToken.None));

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

        await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
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
        var auth = CreateAuthWithRegistration(
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
        var auth = CreateAuthWithRegistration(
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

        await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
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
        var auth = CreateAuthWithRegistration(
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

        await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
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
        var auth = CreateAuthWithRegistration(
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

        var account = await RegistrationFor(auth).RegisterAsync(
            RegisterRequest("9876543210", "member@example.com"),
            CancellationToken.None);

        Assert.Equal("+919876543210", account.Phone);
        Assert.Equal("member@example.com", account.Email);
        Assert.False(account.PhoneConfirmed);
        Assert.False(account.EmailConfirmed);
        Assert.Contains("member", account.Roles);
        Assert.NotNull(account.Profile);
        Assert.Equal("Ada Lovelace", account.Profile!.Name);
        Assert.Equal("Mumbai", account.Profile.City);
        Assert.NotNull(await users.FindByPhoneAsync("+919876543210", CancellationToken.None));
    }

    private static (AuthService Auth, FakeUserRepository Users, FakeSmsService Sms, FakeEmailService Email) StepwiseHarness()
    {
        var otpOptions = Options.Create(new OtpOptions { MaxRequestsPerPhonePerHour = 10, MaxRequestsPerIpPerHour = 20 });
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var users = new FakeUserRepository();
        var sms = new FakeSmsService();
        var email = new FakeEmailService();
        var auth = CreateAuthWithRegistration(
            new FakeOtpRepository(otpOptions.Value), users, new FakeMemberProfileRepository(),
            new FakeMemberPhotoRepository(), new FakeIntroductionVideoRepository(), new FakeRefreshSessionRepository(),
            new FakeTokenService(), sms, email, TestMapper.Instance, NoopAuditWriter.Instance,
            DiscardLogger<AuthService>.Instance, otpOptions, jwtOptions,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = "http://localhost:5173/verify-email" }));
        return (auth, users, sms, email);
    }

    [Fact]
    public async Task StepwiseRegistration_PhoneCodeCreatesDraftAccount_AndEmailCodeConfirmsEmail()
    {
        var (auth, users, sms, email) = StepwiseHarness();
        var registration = RegistrationFor(auth);

        var sent = await registration.StartPhoneRegistrationAsync(
            new StartPhoneRegistrationRequest("9876543210"), "1.2.3.4", CancellationToken.None);
        Assert.Equal("+919876543210", sms.LastPhone);
        Assert.NotNull(sms.LastCode);
        Assert.True(sent.ExpiresInSeconds > 0);
        Assert.Null(await users.FindByPhoneAsync("+919876543210", CancellationToken.None));

        var tokens = await registration.VerifyPhoneRegistrationAsync(
            new VerifyPhoneRegistrationRequest("9876543210", sms.LastCode!), CancellationToken.None);
        Assert.StartsWith("access-", tokens.AccessToken);
        Assert.Contains(":member:otp", tokens.AccessToken);
        Assert.True(tokens.Account.PhoneConfirmed);
        Assert.Null(tokens.Account.Email);
        Assert.Null(tokens.Account.Profile);
        Assert.Equal("Member", tokens.Account.AccountKind);

        var again = await Assert.ThrowsAsync<AuthException>(() => registration.VerifyPhoneRegistrationAsync(
            new VerifyPhoneRegistrationRequest("9876543210", sms.LastCode!), CancellationToken.None));
        Assert.Equal("otp_expired", again.ErrorCode);

        await registration.StartEmailVerificationAsync(
            tokens.Account.Id, new StartEmailVerificationRequest("Ada@Example.com"), "1.2.3.4", CancellationToken.None);
        Assert.NotNull(email.LastOtpCode);

        var wrong = await Assert.ThrowsAsync<AuthException>(() => registration.VerifyEmailCodeAsync(
            tokens.Account.Id, new VerifyEmailCodeRequest("Ada@Example.com", "000000"), CancellationToken.None));
        Assert.Equal("otp_invalid", wrong.ErrorCode);

        var account = await registration.VerifyEmailCodeAsync(
            tokens.Account.Id, new VerifyEmailCodeRequest("Ada@Example.com", email.LastOtpCode!), CancellationToken.None);
        Assert.Equal("Ada@Example.com", account.Email);
        Assert.True(account.EmailConfirmed);
        Assert.True(account.PhoneConfirmed);
    }

    /// <summary>
    /// Registration spans several pages, so a member can leave part-way and come back to the phone
    /// step with an account already created. Refusing a known number there would dead-end exactly
    /// the people the resume flow exists for, so the step issues a sign-in code instead.
    /// </summary>
    [Fact]
    public async Task StepwiseRegistration_KnownPhone_SignsInInsteadOfRefusing()
    {
        var (auth, _, sms, _) = StepwiseHarness();
        var registration = RegistrationFor(auth);
        var existing = await registration.RegisterAsync(
            RegisterRequest("9876543210", "taken@example.com"), CancellationToken.None);

        await registration.StartPhoneRegistrationAsync(
            new StartPhoneRegistrationRequest("9876543210"), null, CancellationToken.None);
        Assert.NotNull(sms.LastCode);

        var tokens = await registration.VerifyPhoneRegistrationAsync(
            new VerifyPhoneRegistrationRequest("9876543210", sms.LastCode!), CancellationToken.None);

        // The existing account, not a second one created alongside it.
        Assert.Equal(existing.Id, tokens.Account.Id);
    }

    [Fact]
    public async Task StepwiseRegistration_RejectsTakenEmail()
    {
        var (auth, _, sms, _) = StepwiseHarness();
        var registration = RegistrationFor(auth);
        await registration.RegisterAsync(RegisterRequest("9876543210", "taken@example.com"), CancellationToken.None);

        await registration.StartPhoneRegistrationAsync(
            new StartPhoneRegistrationRequest("9123456789"), null, CancellationToken.None);
        var tokens = await registration.VerifyPhoneRegistrationAsync(
            new VerifyPhoneRegistrationRequest("9123456789", sms.LastCode!), CancellationToken.None);

        var emailTaken = await Assert.ThrowsAsync<AuthException>(() => registration.StartEmailVerificationAsync(
            tokens.Account.Id, new StartEmailVerificationRequest("taken@example.com"), null, CancellationToken.None));
        Assert.Equal("email_already_exists", emailTaken.ErrorCode);
        Assert.Equal(409, emailTaken.StatusCode);
    }

    [Fact]
    public async Task CreateMember_QueuesVerification_AndAuthConfirmsEmail()
    {
        var otpOptions = Options.Create(new OtpOptions());
        var jwtOptions = Options.Create(new JwtOptions { AudienceMember = "member" });
        var email = new FakeEmailService();
        var users = new FakeUserRepository();
        var auth = CreateAuthWithRegistration(
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

        var account = await RegistrationFor(auth).RegisterAsync(
            RegisterRequest("9876543210", "verify.me@example.com"),
            CancellationToken.None);

        Assert.False(account.EmailConfirmed);
        Assert.Null(email.LastVerifyUrl);
        Assert.Equal(account.Id, Assert.Single(Queues.GetValue(auth, _ => throw new InvalidOperationException()).Users));
        var userId = account.Id;
        var token = await users.GenerateEmailConfirmationTokenAsync(userId, CancellationToken.None);

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
        var auth = CreateAuthWithRegistration(
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
            "Kid User",
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)),
            "Mumbai",
            "kid@example.com",
            Hometown: "Pune");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            RegistrationFor(auth).RegisterAsync(underage, CancellationToken.None));

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
        var auth = CreateAuthWithRegistration(
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

        await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None));

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
        var auth = CreateAuthWithRegistration(
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

        var first = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await Lifecycles.GetValue(auth, _ => throw new InvalidOperationException()).DeleteMemberAsync(first.Id, CancellationToken.None);

        var second = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);

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
        var auth = CreateAuthWithRegistration(
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

        var account = await RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None);
        await Lifecycles.GetValue(auth, _ => throw new InvalidOperationException()).DeactivateMemberAsync(account.Id, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            RegistrationFor(auth).RegisterAsync(RegisterRequest("9876543210"), CancellationToken.None));

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

    public Task SendVenueHeadsUpAsync(
        string phoneE164,
        Aynera.Application.Features.Venues.Models.VenueHeadsUpNotice notice,
        CancellationToken cancellationToken) => Task.CompletedTask;
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

    public Task SendVenueHeadsUpAsync(
        string email,
        Aynera.Application.Features.Venues.Models.VenueHeadsUpNotice notice,
        CancellationToken cancellationToken) => Task.CompletedTask;
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

    public Task<OtpConsumeOutcome> TryConsumeAsync(
        string channel,
        string destination,
        string codeHash,
        string expectedPurpose,
        string expectedAudience,
        CancellationToken cancellationToken)
    {
        var key = Key(channel, destination);
        if (!_challenges.TryGetValue(key, out var challenge)
            || challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || !string.Equals(challenge.Purpose, expectedPurpose, StringComparison.Ordinal)
            || !string.Equals(challenge.Audience, expectedAudience, StringComparison.Ordinal))
        {
            return Task.FromResult(OtpConsumeOutcome.NotFound);
        }

        if (challenge.Attempts >= _options.MaxAttempts)
        {
            _challenges.Remove(key);
            return Task.FromResult(OtpConsumeOutcome.Locked);
        }

        if (!SecureEquals.Hex(codeHash, challenge.CodeHash))
        {
            var attempts = challenge.Attempts + 1;
            if (attempts >= _options.MaxAttempts)
            {
                _challenges.Remove(key);
                return Task.FromResult(OtpConsumeOutcome.Locked);
            }

            _challenges[key] = challenge with { Attempts = attempts };
            return Task.FromResult(OtpConsumeOutcome.Invalid);
        }

        _challenges.Remove(key);
        return Task.FromResult(OtpConsumeOutcome.Consumed);
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

    public Task SetConfirmedEmailAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        var taken = _byId.Values.Any(u =>
            u.Id != userId && !u.IsDeleted
            && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            throw new AuthException("email_already_exists", "A member account with this email already exists.", statusCode: 409);
        }

        var user = _byId[userId];
        _byId[userId] = user with { Email = email, EmailConfirmed = true };
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
                || (member.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (member.Nickname?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
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

    public Task<MemberProfileRecord> UpsertAsync(MemberProfileRecord profile, CancellationToken cancellationToken)
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

