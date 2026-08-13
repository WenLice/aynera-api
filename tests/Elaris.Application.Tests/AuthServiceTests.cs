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
    private static CreateMemberRequest RegisterRequest(string phone, string email = "member@example.com") =>
        new(
            phone,
            "Ada",
            "Lovelace",
            Gender.Female,
            new DateOnly(1990, 5, 15),
            "Mumbai",
            email,
            "Hindu");

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
            AudienceMemberWeb = "member-web"
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
        var jwtOptions = Options.Create(new JwtOptions { AudienceMemberWeb = "member-web" });
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
        var jwtOptions = Options.Create(new JwtOptions { AudienceMemberWeb = "member-web" });
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
        var jwtOptions = Options.Create(new JwtOptions { AudienceMemberWeb = "member-web" });
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
        var jwtOptions = Options.Create(new JwtOptions { AudienceMemberWeb = "member-web" });
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
        var jwtOptions = Options.Create(new JwtOptions { AudienceMemberWeb = "member-web" });
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
        var jwtOptions = Options.Create(new JwtOptions { AudienceMemberWeb = "member-web" });
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

file sealed class FakeSmsService : ISmsService
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

file sealed class FakeEmailService : IEmailService
{
    public string? LastEmail { get; private set; }
    public string? LastVerifyUrl { get; private set; }

    public Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken)
    {
        LastEmail = email;
        LastVerifyUrl = verifyUrl;
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
        new($"access-{user.Id}", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(15));

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
    private readonly Dictionary<string, int> _phoneCounts = new(StringComparer.Ordinal);

    public FakeOtpRepository(OtpOptions options)
    {
        _options = options;
    }

    public Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireRequestSlotAsync(
        string phoneE164,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        _phoneCounts.TryGetValue(phoneE164, out var count);
        count++;
        _phoneCounts[phoneE164] = count;
        if (count > _options.MaxRequestsPerPhonePerHour)
        {
            return Task.FromResult<(bool, int?)>((false, 3600));
        }

        return Task.FromResult<(bool, int?)>((true, null));
    }

    public Task StoreAsync(OtpChallenge challenge, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _challenges[challenge.PhoneE164] = challenge;
        return Task.CompletedTask;
    }

    public Task<OtpChallenge?> GetAsync(string phoneE164, CancellationToken cancellationToken) =>
        Task.FromResult(_challenges.TryGetValue(phoneE164, out var c) ? c : null);

    public Task<bool> IncrementAttemptsAsync(string phoneE164, CancellationToken cancellationToken)
    {
        if (!_challenges.TryGetValue(phoneE164, out var challenge))
        {
            return Task.FromResult(false);
        }

        var updated = challenge with { Attempts = challenge.Attempts + 1 };
        if (updated.Attempts >= _options.MaxAttempts)
        {
            _challenges.Remove(phoneE164);
            return Task.FromResult(false);
        }

        _challenges[phoneE164] = updated;
        return Task.FromResult(true);
    }

    public Task RemoveAsync(string phoneE164, CancellationToken cancellationToken)
    {
        _challenges.Remove(phoneE164);
        return Task.CompletedTask;
    }
}

sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, UserRecord> _byId = new();
    private readonly Dictionary<string, Guid> _byPhone = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _emailTokens = new();

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

    public Task<UserRecord> CreateMemberAsync(string phoneE164, string? email, CancellationToken cancellationToken)
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
        var user = new UserRecord(id, phoneE164, false, email, false, "Member", true, false, ["member"]);
        _byId[id] = user;
        _byPhone[phoneE164] = id;
        return Task.FromResult(user);
    }

    public Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _byId[userId];
        _byId[userId] = user with { PhoneConfirmed = true };
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

