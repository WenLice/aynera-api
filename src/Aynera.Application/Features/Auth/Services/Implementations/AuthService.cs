using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Application.Common;
using AutoMapper;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Auth.Services.Implementations;

public sealed class AuthService : IAuthService, IAdminAuthService
{
    private readonly IOtpChallengeRepository _otpChallenges;
    private readonly IUserRepository _users;
    private readonly IMemberProfileRepository _profiles;
    private readonly IRefreshSessionRepository _refreshSessions;
    private readonly ITokenService _tokens;
    private readonly ISmsService _sms;
    private readonly IEmailService _email;
    private readonly IMapper _mapper;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AuthService> _logger;
    private readonly OtpOptions _otpOptions;
    private readonly JwtOptions _jwtOptions;
    private readonly IWorkflowTransaction _transaction;

    public AuthService(
        IOtpChallengeRepository otpChallenges,
        IUserRepository users,
        IMemberProfileRepository profiles,
        IRefreshSessionRepository refreshSessions,
        ITokenService tokens,
        ISmsService sms,
        IEmailService email,
        IMapper mapper,
        IAuditWriter audit,
        ILogger<AuthService> logger,
        IOptions<OtpOptions> otpOptions,
        IOptions<JwtOptions> jwtOptions,
        IWorkflowTransaction transaction)
    {
        _otpChallenges = otpChallenges;
        _users = users;
        _profiles = profiles;
        _refreshSessions = refreshSessions;
        _tokens = tokens;
        _sms = sms;
        _email = email;
        _mapper = mapper;
        _audit = audit;
        _logger = logger;
        _otpOptions = otpOptions.Value;
        _jwtOptions = jwtOptions.Value;
        _transaction = transaction;
    }

    public async Task<AuthAccountDto> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ConfirmEmail starting");

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new AuthException("email_token_invalid", "Email verification token is required.", statusCode: 400);
        }

        await _users.ConfirmEmailAsync(request.UserId, request.Token.Trim(), cancellationToken);

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.EmailConfirmed,
                Outcome: AuditOutcomes.Success,
                Message: "Email confirmed.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Changes: AuditChanges.Create([("emailConfirmed", false, true)])),
            cancellationToken);

        return await MapAccountAsync(user, cancellationToken);
    }

    public async Task<RequestMemberOtpResponse> RequestMemberOtpAsync(
        RequestMemberOtpRequest request,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        return await RequestOtpAsync(
            identifier,
            OtpPurposes.Login,
            clientIp,
            AccountKind.Member,
            _jwtOptions.AudienceMember,
            cancellationToken);
    }

    public async Task<TokenResponse> VerifyMemberOtpAsync(
        VerifyMemberOtpRequest request,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        var audience = string.IsNullOrWhiteSpace(request.Audience)
            ? _jwtOptions.AudienceMember
            : request.Audience.Trim();
        _logger.LogInformation(
            "VerifyMemberOtp starting for {Identifier} audience {Audience}",
            LoginIdentifiers.Mask(identifier),
            audience);

        if (!string.Equals(audience, _jwtOptions.AudienceMember, StringComparison.Ordinal))
        {
            throw new AuthException("invalid_audience", "Audience must be member.");
        }

        await ConsumeOtpAsync(identifier, request.Code, OtpPurposes.Login, audience, cancellationToken);

        var user = await RequireRegisteredActiveAccountAsync(identifier, AccountKind.Member, cancellationToken);
        await ConfirmIdentifierAsync(user.Id, identifier, cancellationToken);
        await _users.TouchLastLoginAsync(user.Id, cancellationToken);

        user = await _users.FindByIdAsync(user.Id, cancellationToken)
            ?? throw new AuthException("user_missing", "Account could not be loaded.", statusCode: 500);

        var tokens = await IssueMemberTokensAsync(user, audience, amr: "otp", cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.OtpVerified,
                Outcome: AuditOutcomes.Success,
                Message: "OTP verified; tokens issued.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Audience: audience,
                Metadata: new
                {
                    identifier = LoginIdentifiers.Mask(identifier),
                    channel = identifier.ChannelKey
                }),
            cancellationToken);

        _logger.LogInformation("VerifyMemberOtp succeeded for user {UserId}", user.Id);
        return tokens;
    }

    public async Task<TokenResponse> LoginWithPasswordAsync(
        MemberPasswordLoginRequest request,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        var masked = LoginIdentifiers.Mask(identifier);
        _logger.LogInformation("Password login starting for {Identifier}", masked);
        return await LoginWithPasswordForKindAsync(
            identifier,
            request.Password,
            AccountKind.Member,
            _jwtOptions.AudienceMember,
            cancellationToken);
    }

    public async Task<RequestMemberOtpResponse> RequestOtpAsync(
        RequestAdminOtpRequest request,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        return await RequestOtpAsync(
            identifier,
            OtpPurposes.AdminLogin,
            clientIp,
            AccountKind.Admin,
            _jwtOptions.AudienceAdmin,
            cancellationToken);
    }

    public async Task<TokenResponse> VerifyOtpAsync(
        VerifyAdminOtpRequest request,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        var audience = _jwtOptions.AudienceAdmin;
        _logger.LogInformation(
            "VerifyAdminOtp starting for {Identifier} audience {Audience}",
            LoginIdentifiers.Mask(identifier),
            audience);

        await ConsumeOtpAsync(identifier, request.Code, OtpPurposes.AdminLogin, audience, cancellationToken);

        var user = await RequireRegisteredActiveAccountAsync(identifier, AccountKind.Admin, cancellationToken);
        await ConfirmIdentifierAsync(user.Id, identifier, cancellationToken);
        await _users.TouchLastLoginAsync(user.Id, cancellationToken);

        user = await _users.FindByIdAsync(user.Id, cancellationToken)
            ?? throw new AuthException("user_missing", "Account could not be loaded.", statusCode: 500);

        var tokens = await IssueMemberTokensAsync(user, audience, amr: "otp", cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.OtpVerified,
                Outcome: AuditOutcomes.Success,
                Message: "Admin OTP verified; tokens issued.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Audience: audience,
                Metadata: new
                {
                    identifier = LoginIdentifiers.Mask(identifier),
                    channel = identifier.ChannelKey
                }),
            cancellationToken);

        _logger.LogInformation("VerifyAdminOtp succeeded for user {UserId}", user.Id);
        return tokens;
    }

    public async Task<TokenResponse> LoginWithPasswordAsync(
        AdminPasswordLoginRequest request,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        var masked = LoginIdentifiers.Mask(identifier);
        _logger.LogInformation("Admin password login starting for {Identifier}", masked);
        return await LoginWithPasswordForKindAsync(
            identifier,
            request.Password,
            AccountKind.Admin,
            _jwtOptions.AudienceAdmin,
            cancellationToken);
    }

    private async Task<TokenResponse> LoginWithPasswordForKindAsync(
        LoginIdentifier identifier,
        string password,
        AccountKind expectedKind,
        string audience,
        CancellationToken cancellationToken)
    {
        var masked = LoginIdentifiers.Mask(identifier);
        var user = await RequireRegisteredActiveAccountAsync(identifier, expectedKind, cancellationToken);
        var check = await _users.CheckPasswordAsync(user.Id, password, cancellationToken);
        if (check is PasswordCheckResult.NotSet)
        {
            throw new AuthException(
                "password_not_set",
                "No password is set for this account. Sign in with OTP or set a password after OTP login.",
                statusCode: 400);
        }

        if (check is PasswordCheckResult.LockedOut)
        {
            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.PasswordLoginFailed,
                    Outcome: AuditOutcomes.Denied,
                    Message: "Password login locked out.",
                    UserId: user.Id,
                    SubjectUserId: user.Id,
                    SubjectType: AuditSubjectTypes.User,
                    SubjectId: user.Id.ToString("D"),
                    Audience: audience,
                    Metadata: new { identifier = masked, errorCode = "account_locked" }),
                cancellationToken);
            throw new AuthException(
                "account_locked",
                "This account is locked after too many failed sign-in attempts. Try again later or use OTP.",
                statusCode: 403);
        }

        if (check is not PasswordCheckResult.Success)
        {
            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.PasswordLoginFailed,
                    Outcome: AuditOutcomes.Failure,
                    Message: "Password login failed.",
                    UserId: user.Id,
                    SubjectUserId: user.Id,
                    SubjectType: AuditSubjectTypes.User,
                    SubjectId: user.Id.ToString("D"),
                    Audience: audience,
                    Metadata: new { identifier = masked, errorCode = "password_invalid" }),
                cancellationToken);
            throw new AuthException("password_invalid", "Phone/email or password is incorrect.", statusCode: 401);
        }

        await _users.TouchLastLoginAsync(user.Id, cancellationToken);
        user = await _users.FindByIdAsync(user.Id, cancellationToken)
            ?? throw new AuthException("user_missing", "Account could not be loaded.", statusCode: 500);

        var tokens = await IssueMemberTokensAsync(user, audience, amr: "pwd", cancellationToken);
        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.PasswordLoginSucceeded,
                Outcome: AuditOutcomes.Success,
                Message: expectedKind == AccountKind.Admin
                    ? "Admin password login succeeded."
                    : "Password login succeeded.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Audience: audience,
                Metadata: new { identifier = masked, channel = identifier.ChannelKey }),
            cancellationToken);

        _logger.LogInformation("Password login succeeded for user {UserId}", user.Id);
        return tokens;
    }

    public async Task SetPasswordAsync(
        Guid userId,
        SetMemberPasswordRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SetPassword starting for user {UserId}", userId);
        var hadPassword = await _users.HasPasswordAsync(userId, cancellationToken);
        await _users.SetPasswordAsync(userId, request.Password, request.CurrentPassword, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.PasswordSet,
                Outcome: AuditOutcomes.Success,
                Message: hadPassword ? "Password changed." : "Password set.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: userId.ToString("D")),
            cancellationToken);
    }

    public Task<RequestMemberOtpResponse> RequestPasswordResetAsync(
        ForgotMemberPasswordRequest request,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        return RequestOtpAsync(
            identifier,
            OtpPurposes.PasswordReset,
            clientIp,
            requiredKind: null,
            _jwtOptions.AudienceMember,
            cancellationToken);
    }

    public async Task<TokenResponse> ResetPasswordAsync(
        ResetMemberPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        var audience = _jwtOptions.AudienceMember;
        _logger.LogInformation("ResetPassword starting for {Identifier}", LoginIdentifiers.Mask(identifier));

        await ConsumeOtpAsync(
            identifier,
            request.Code,
            OtpPurposes.PasswordReset,
            audience,
            cancellationToken);

        var user = await RequireRegisteredActiveAccountAsync(identifier, AccountKind.Member, cancellationToken);
        await _users.ResetPasswordAsync(user.Id, request.NewPassword, cancellationToken);
        await ConfirmIdentifierAsync(user.Id, identifier, cancellationToken);
        await _users.TouchLastLoginAsync(user.Id, cancellationToken);

        user = await _users.FindByIdAsync(user.Id, cancellationToken)
            ?? throw new AuthException("user_missing", "Account could not be loaded.", statusCode: 500);

        var tokens = await IssueMemberTokensAsync(user, audience, amr: "otp", cancellationToken);
        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.PasswordResetCompleted,
                Outcome: AuditOutcomes.Success,
                Message: "Password reset; tokens issued.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Audience: audience,
                Metadata: new
                {
                    identifier = LoginIdentifiers.Mask(identifier),
                    channel = identifier.ChannelKey
                }),
            cancellationToken);

        _logger.LogInformation("ResetPassword succeeded for user {UserId}", user.Id);
        return tokens;
    }

    public async Task<TokenResponse> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("RefreshToken starting");
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            _logger.LogWarning("RefreshToken failed: missing token");
            throw new AuthException("invalid_refresh", "Refresh token is required.", statusCode: 401);
        }

        var hash = TokenHasher.Hash(request.RefreshToken.Trim());
        var session = await _refreshSessions.FindByTokenHashAsync(hash, cancellationToken);
        if (session is null)
        {
            throw new AuthException("invalid_refresh", "Refresh token is invalid.", statusCode: 401);
        }

        var outcome = await _transaction.ExecuteAsync(new[] { $"account:{session.UserId:D}" },
            ct => RefreshUnderLockAsync(hash, ct), cancellationToken);
        // Expected denial may revoke a family. Commit that revocation before returning the error.
        if (outcome.Error is not null) throw outcome.Error;
        var tokens = outcome.Tokens!;
        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.TokenRefreshed,
                Outcome: AuditOutcomes.Success,
                Message: "Refresh token rotated.",
                UserId: tokens.Account.Id,
                SubjectUserId: tokens.Account.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: tokens.Account.Id.ToString("D"),
                Audience: session.Audience),
            cancellationToken);
        _logger.LogInformation("RefreshToken succeeded for user {UserId}", tokens.Account.Id);
        return tokens;
    }

    private sealed record RefreshOutcome(TokenResponse? Tokens, AuthException? Error);

    private async Task<RefreshOutcome> RefreshUnderLockAsync(string hash, CancellationToken cancellationToken)
    {
        // The pre-lock lookup only selected the account lock. Always reread session state here.
        var session = await _refreshSessions.FindByTokenHashAsync(hash, cancellationToken);
        if (session is null)
            return new RefreshOutcome(null, new AuthException("invalid_refresh", "Refresh token is invalid.", statusCode: 401));
        if (session.ReplacedAtUtc is not null || session.RevokedAtUtc is not null)
        {
            await _refreshSessions.RevokeFamilyAsync(session.FamilyId, cancellationToken);
            return new RefreshOutcome(null, new AuthException("refresh_reuse", "Refresh token reuse detected. Sign in again.", statusCode: 401));
        }

        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await _refreshSessions.RevokeAsync(session.Id, cancellationToken);
            return new RefreshOutcome(null, new AuthException("refresh_expired", "Refresh token expired. Sign in again.", statusCode: 401));
        }

        var user = await _users.FindByIdAsync(session.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
        {
            await _refreshSessions.RevokeFamilyAsync(session.FamilyId, cancellationToken);
            return new RefreshOutcome(null, new AuthException("invalid_refresh", "Account no longer exists.", statusCode: 401));
        }

        if (user.IsRestricted)
        {
            await _refreshSessions.RevokeAllForUserAsync(user.Id, cancellationToken);
            return new RefreshOutcome(null, new AuthException(
                "account_restricted",
                "This account is restricted. Contact support.",
                statusCode: 403));
        }

        if (!user.IsActive)
        {
            await _refreshSessions.RevokeAllForUserAsync(user.Id, cancellationToken);
            return new RefreshOutcome(null, new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before continuing.",
                statusCode: 403));
        }

        try
        {
            EnsureCanIssueTokens(user, session.Audience);
        }
        catch (AuthException error)
        {
            await _refreshSessions.RevokeFamilyAsync(session.FamilyId, cancellationToken);
            return new RefreshOutcome(null, error);
        }

        var issued = _tokens.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        var refreshLifetime = _jwtOptions.RefreshLifetimeForAudience(session.Audience);
        var newSession = new RefreshSessionRecord(
            Id: issued.SessionId,
            UserId: user.Id,
            Audience: session.Audience,
            TokenHash: issued.TokenHash,
            FamilyId: session.FamilyId,
            DeviceLabel: session.DeviceLabel,
            CreatedAtUtc: now,
            ExpiresAtUtc: now.Add(refreshLifetime),
            RevokedAtUtc: null,
            ReplacedAtUtc: null,
            ReplacedBySessionId: null);

        await _refreshSessions.AddAsync(newSession, cancellationToken);
        await _refreshSessions.MarkReplacedAsync(session.Id, newSession.Id, cancellationToken);

        var access = _tokens.CreateAccessToken(
            user,
            session.Audience,
            newSession.Id,
            amr: "otp",
            authTimeUtc: now);

        return new RefreshOutcome(new TokenResponse(
            AccessToken: access.AccessToken,
            RefreshToken: issued.RefreshToken,
            TokenType: "Bearer",
            ExpiresInSeconds: (int)(access.ExpiresAtUtc - now).TotalSeconds,
            Account: await MapAccountAsync(user, cancellationToken)), null);
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Logout starting");
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return;
        }

        var hash = TokenHasher.Hash(request.RefreshToken.Trim());
        var session = await _refreshSessions.FindByTokenHashAsync(hash, cancellationToken);
        if (session is null)
        {
            return;
        }

        await _refreshSessions.RevokeAsync(session.Id, cancellationToken);
        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.Logout,
                Outcome: AuditOutcomes.Success,
                Message: "Logged out.",
                UserId: session.UserId,
                SubjectUserId: session.UserId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: session.UserId.ToString("D"),
                Audience: session.Audience),
            cancellationToken);
    }

    public async Task<AuthAccountDto> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetMe {UserId}", userId);
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("GetMe user not found {UserId}", userId);
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        return await MapAccountAsync(user, cancellationToken);
    }

    private async Task<RequestMemberOtpResponse> RequestOtpAsync(
        LoginIdentifier identifier,
        string purpose,
        string? clientIp,
        AccountKind? requiredKind,
        string audience,
        CancellationToken cancellationToken)
    {
        var masked = LoginIdentifiers.Mask(identifier);
        _logger.LogInformation("Request OTP {Purpose} starting for {Identifier}", purpose, masked);
        try
        {
            UserRecord? user;
            if (requiredKind is { } kind)
            {
                user = await RequireRegisteredActiveAccountAsync(identifier, kind, cancellationToken);
            }
            else
            {
                user = await FindMemberForPasswordResetAsync(identifier, cancellationToken);
            }

            var (allowed, retryAfter) = await _otpChallenges.TryAcquireRequestSlotAsync(
                identifier.ChannelKey,
                identifier.Destination,
                clientIp,
                cancellationToken);
            if (!allowed)
            {
                await _audit.WriteAsync(
                    new AuditEventWriteModel(
                        Action: AuditActions.OtpRequestFailed,
                        Outcome: AuditOutcomes.Denied,
                        Message: "OTP request rate limited.",
                        ClientIp: clientIp,
                        Metadata: new
                        {
                            identifier = masked,
                            channel = identifier.ChannelKey,
                            errorCode = "otp_rate_limited"
                        }),
                    cancellationToken);
                throw new AuthException(
                    "otp_rate_limited",
                    "Too many OTP requests. Try again later.",
                    statusCode: 429);
            }

            if (user is null)
            {
                return new RequestMemberOtpResponse(_otpOptions.TtlSeconds, retryAfter);
            }

            var code = OtpCodeGenerator.Generate(_otpOptions.CodeLength);
            var ttl = TimeSpan.FromSeconds(_otpOptions.TtlSeconds);
            var challenge = new OtpChallenge(
                Channel: identifier.ChannelKey,
                Destination: identifier.Destination,
                CodeHash: TokenHasher.Hash(code),
                Attempts: 0,
                ExpiresAtUtc: DateTimeOffset.UtcNow.Add(ttl),
                Purpose: purpose,
                Audience: audience);

            await _otpChallenges.StoreAsync(challenge, ttl, cancellationToken);
            await DispatchOtpAsync(identifier, code, cancellationToken);

            var requestedAction = string.Equals(purpose, OtpPurposes.PasswordReset, StringComparison.Ordinal)
                ? AuditActions.PasswordResetRequested
                : AuditActions.OtpRequested;
            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: requestedAction,
                    Outcome: AuditOutcomes.Success,
                    Message: string.Equals(purpose, OtpPurposes.PasswordReset, StringComparison.Ordinal)
                        ? "Password reset OTP requested."
                        : "OTP requested.",
                    ClientIp: clientIp,
                    Audience: audience,
                    Metadata: new { identifier = masked, channel = identifier.ChannelKey }),
                cancellationToken);

            _logger.LogInformation("Request OTP {Purpose} succeeded for {Identifier}", purpose, masked);
            return new RequestMemberOtpResponse(_otpOptions.TtlSeconds, retryAfter);
        }
        catch (AuthException ex) when (ex.ErrorCode is not "otp_rate_limited")
        {
            _logger.LogWarning(ex, "Request OTP failed {ErrorCode} for {Identifier}", ex.ErrorCode, masked);
            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.OtpRequestFailed,
                    Outcome: AuditOutcomes.Failure,
                    Message: "OTP request failed.",
                    ClientIp: clientIp,
                    Metadata: new
                    {
                        identifier = masked,
                        channel = identifier.ChannelKey,
                        errorCode = ex.ErrorCode
                    }),
                cancellationToken);
            throw;
        }
    }

    private async Task ConsumeOtpAsync(
        LoginIdentifier identifier,
        string? code,
        string expectedPurpose,
        string audience,
        CancellationToken cancellationToken)
    {
        var outcome = await _otpChallenges.TryConsumeAsync(
            identifier.ChannelKey,
            identifier.Destination,
            TokenHasher.Hash(code?.Trim() ?? string.Empty),
            expectedPurpose,
            audience,
            cancellationToken);
        switch (outcome)
        {
            case OtpConsumeOutcome.Consumed:
                return;
            case OtpConsumeOutcome.Invalid:
                await WriteOtpFailedAsync(identifier, audience, "otp_invalid", cancellationToken);
                throw new AuthException("otp_invalid", "Invalid OTP code.", statusCode: 401);
            case OtpConsumeOutcome.Locked:
                await WriteOtpFailedAsync(identifier, audience, "otp_locked", cancellationToken);
                throw new AuthException(
                    "otp_locked",
                    "Too many invalid attempts. Request a new code.",
                    statusCode: 401);
            default:
                await WriteOtpFailedAsync(identifier, audience, "otp_expired", cancellationToken);
                throw new AuthException("otp_expired", "OTP expired or not found. Request a new code.", statusCode: 401);
        }
    }

    private Task DispatchOtpAsync(
        LoginIdentifier identifier,
        string code,
        CancellationToken cancellationToken) =>
        identifier.Channel == LoginChannel.Phone
            ? _sms.SendOtpAsync(identifier.Destination, code, cancellationToken)
            : _email.SendOtpAsync(identifier.Destination, code, cancellationToken);

    private Task ConfirmIdentifierAsync(
        Guid userId,
        LoginIdentifier identifier,
        CancellationToken cancellationToken) =>
        identifier.Channel == LoginChannel.Phone
            ? _users.MarkPhoneConfirmedAsync(userId, cancellationToken)
            : _users.MarkEmailConfirmedAsync(userId, cancellationToken);

    public async Task<TokenResponse> IssueMemberSessionAsync(
        Guid userId,
        string amr,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken)
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        await _users.TouchLastLoginAsync(user.Id, cancellationToken);
        var tokens = await IssueMemberTokensAsync(user, _jwtOptions.AudienceMember, amr, cancellationToken);
        _logger.LogInformation("Member session issued for user {UserId} via {Amr}", user.Id, amr);
        return tokens;
    }

    private async Task<TokenResponse> IssueMemberTokensAsync(
        UserRecord user,
        string audience,
        string amr,
        CancellationToken cancellationToken)
    {
        return await _transaction.ExecuteAsync(new[] { $"account:{user.Id:D}" }, async ct =>
        {
            user = await _users.FindByIdAsync(user.Id, ct)
                ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
            EnsureCanIssueTokens(user, audience);
            var refresh = _tokens.CreateRefreshToken();
            var now = DateTimeOffset.UtcNow;
            var refreshLifetime = _jwtOptions.RefreshLifetimeForAudience(audience);

            await _refreshSessions.AddAsync(
                new RefreshSessionRecord(
                    Id: refresh.SessionId,
                    UserId: user.Id,
                    Audience: audience,
                    TokenHash: refresh.TokenHash,
                    FamilyId: refresh.FamilyId,
                    DeviceLabel: null,
                    CreatedAtUtc: now,
                    ExpiresAtUtc: now.Add(refreshLifetime),
                    RevokedAtUtc: null,
                    ReplacedAtUtc: null,
                    ReplacedBySessionId: null),
                ct);

            var access = _tokens.CreateAccessToken(user, audience, refresh.SessionId, amr, authTimeUtc: now);
            return new TokenResponse(
                AccessToken: access.AccessToken,
                RefreshToken: refresh.RefreshToken,
                TokenType: "Bearer",
                ExpiresInSeconds: (int)(access.ExpiresAtUtc - now).TotalSeconds,
                Account: await MapAccountAsync(user, ct));
        }, cancellationToken);
    }

    private void EnsureCanIssueTokens(UserRecord user, string audience)
    {
        var expectedKind = string.Equals(audience, _jwtOptions.AudienceMember, StringComparison.Ordinal)
            ? nameof(AccountKind.Member)
            : string.Equals(audience, _jwtOptions.AudienceAdmin, StringComparison.Ordinal)
                ? nameof(AccountKind.Admin) : null;
        if (user.IsDeleted || expectedKind is null || !string.Equals(user.AccountKind, expectedKind, StringComparison.Ordinal))
            throw new AuthException("invalid_account", "Account is not eligible for this session.", statusCode: 401);
        if (user.IsRestricted)
            throw new AuthException("account_restricted", "This account is restricted. Contact support.", statusCode: 403);
        if (!user.IsActive)
            throw new AuthException("account_deactivated", "This account is deactivated. Activate it before continuing.", statusCode: 403);
    }

    private Task WriteOtpFailedAsync(
        LoginIdentifier identifier,
        string audience,
        string errorCode,
        CancellationToken cancellationToken) =>
        _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.OtpFailed,
                Outcome: AuditOutcomes.Failure,
                Message: "OTP verification failed.",
                Audience: audience,
                Metadata: new
                {
                    identifier = LoginIdentifiers.Mask(identifier),
                    channel = identifier.ChannelKey,
                    errorCode
                }),
            cancellationToken);

    private async Task<UserRecord> RequireRegisteredActiveAccountAsync(
        LoginIdentifier identifier,
        AccountKind expectedKind,
        CancellationToken cancellationToken)
    {
        var user = await FindByIdentifierAsync(identifier, cancellationToken);
        var notFoundMessage = expectedKind == AccountKind.Admin
            ? (identifier.Channel == LoginChannel.Phone
                ? "No account found for this phone."
                : "No account found for this email.")
            : (identifier.Channel == LoginChannel.Phone
                ? "No account found for this phone. Register first."
                : "No account found for this email. Register first.");

        if (user is null || user.IsDeleted)
        {
            throw new AuthException("user_not_found", notFoundMessage, statusCode: 404);
        }

        if (!string.Equals(user.AccountKind, expectedKind.ToString(), StringComparison.Ordinal))
        {
            throw new AuthException("user_not_found", notFoundMessage, statusCode: 404);
        }

        if (user.IsRestricted)
        {
            throw new AuthException(
                "account_restricted",
                "This account is restricted. Contact support.",
                statusCode: 403);
        }

        if (!user.IsActive)
        {
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before continuing.",
                statusCode: 403);
        }

        return user;
    }

    private async Task<UserRecord?> FindMemberForPasswordResetAsync(
        LoginIdentifier identifier,
        CancellationToken cancellationToken)
    {
        var user = await FindByIdentifierAsync(identifier, cancellationToken);
        if (user is null
            || user.IsDeleted
            || user.IsRestricted
            || !user.IsActive
            || !string.Equals(user.AccountKind, nameof(AccountKind.Member), StringComparison.Ordinal))
        {
            return null;
        }

        return user;
    }

    private Task<UserRecord?> FindByIdentifierAsync(
        LoginIdentifier identifier,
        CancellationToken cancellationToken) =>
        identifier.Channel == LoginChannel.Phone
            ? _users.FindByPhoneAsync(identifier.Destination, cancellationToken)
            : _users.FindByEmailAsync(identifier.Destination, cancellationToken);

    private async Task<AuthAccountDto> MapAccountAsync(UserRecord user, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FindByUserIdAsync(user.Id, cancellationToken);
        return MapAccount(user, profile);
    }

    private AuthAccountDto MapAccount(UserRecord user, MemberProfileRecord? profile)
    {
        var account = _mapper.Map<AuthAccountDto>(user);
        return account with
        {
            Profile = profile is null ? null : _mapper.Map<MemberProfileDto>(profile)
        };
    }
}


