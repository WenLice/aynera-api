using AutoMapper;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Application.Features.Photos.Repositories;
using Elaris.Application.Features.Videos.Repositories;
using Elaris.Domain.Audit.Records;
using Elaris.Domain.Audit.Statics;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Responses;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Auth.Statics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Features.Auth.Services.Implementations;

public sealed class AuthService : IAuthService
{
    private readonly IOtpChallengeRepository _otpChallenges;
    private readonly IUserRepository _users;
    private readonly IMemberProfileRepository _profiles;
    private readonly IMemberPhotoRepository _photos;
    private readonly IIntroductionVideoRepository _introductionVideos;
    private readonly IRefreshSessionRepository _refreshSessions;
    private readonly ITokenService _tokens;
    private readonly ISmsService _sms;
    private readonly IEmailService _email;
    private readonly IMapper _mapper;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AuthService> _logger;
    private readonly OtpOptions _otpOptions;
    private readonly JwtOptions _jwtOptions;
    private readonly EmailOptions _emailOptions;

    public AuthService(
        IOtpChallengeRepository otpChallenges,
        IUserRepository users,
        IMemberProfileRepository profiles,
        IMemberPhotoRepository photos,
        IIntroductionVideoRepository introductionVideos,
        IRefreshSessionRepository refreshSessions,
        ITokenService tokens,
        ISmsService sms,
        IEmailService email,
        IMapper mapper,
        IAuditWriter audit,
        ILogger<AuthService> logger,
        IOptions<OtpOptions> otpOptions,
        IOptions<JwtOptions> jwtOptions,
        IOptions<EmailOptions> emailOptions)
    {
        _otpChallenges = otpChallenges;
        _users = users;
        _profiles = profiles;
        _photos = photos;
        _introductionVideos = introductionVideos;
        _refreshSessions = refreshSessions;
        _tokens = tokens;
        _sms = sms;
        _email = email;
        _mapper = mapper;
        _audit = audit;
        _logger = logger;
        _otpOptions = otpOptions.Value;
        _jwtOptions = jwtOptions.Value;
        _emailOptions = emailOptions.Value;
    }

    public async Task<AuthAccountDto> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken)
    {
        AgeRules.EnsureAdult(request.DateOfBirth);

        var phone = PhoneNormalizer.NormalizeIndianMobile(request.Phone);
        var email = request.Email.Trim();
        _logger.LogInformation("CreateMember starting for masked phone {Phone}", AuditRedaction.MaskPhone(phone));

        var user = await _users.CreateMemberAsync(phone, email, cancellationToken);
        var profile = await _profiles.CreateAsync(
            new MemberProfileRecord(
                user.Id,
                request.FirstName.Trim(),
                request.LastName.Trim(),
                request.Gender.ToString(),
                request.DateOfBirth,
                request.City.Trim(),
                string.IsNullOrWhiteSpace(request.Religion) ? null : request.Religion.Trim()),
            cancellationToken);

        await SendEmailVerificationAsync(user.Id, email, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberRegistered,
                Outcome: AuditOutcomes.Success,
                Message: "Member registered.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("firstName", null, profile.FirstName),
                    ("lastName", null, profile.LastName),
                    ("gender", null, profile.Gender),
                    ("city", null, profile.City),
                    ("phone", null, AuditRedaction.MaskPhone(phone)),
                    ("email", null, AuditRedaction.MaskEmail(email))
                ])),
            cancellationToken);

        _logger.LogInformation("CreateMember succeeded for user {UserId}", user.Id);
        return MapAccount(user, profile);
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
        var phone = PhoneNormalizer.NormalizeIndianMobile(request.Phone);
        _logger.LogInformation("RequestMemberOtp starting for {Phone}", AuditRedaction.MaskPhone(phone));
        try
        {
            await RequireRegisteredActiveMemberAsync(phone, cancellationToken);

            var (allowed, retryAfter) = await _otpChallenges.TryAcquireRequestSlotAsync(phone, clientIp, cancellationToken);
            if (!allowed)
            {
                await _audit.WriteAsync(
                    new AuditEventWriteModel(
                        Action: AuditActions.OtpRequestFailed,
                        Outcome: AuditOutcomes.Denied,
                        Message: "OTP request rate limited.",
                        ClientIp: clientIp,
                        Metadata: new { phone = AuditRedaction.MaskPhone(phone), errorCode = "otp_rate_limited" }),
                    cancellationToken);
                throw new AuthException(
                    "otp_rate_limited",
                    "Too many OTP requests. Try again later.",
                    statusCode: 429);
            }

            var code = OtpCodeGenerator.Generate(_otpOptions.CodeLength);
            var ttl = TimeSpan.FromSeconds(_otpOptions.TtlSeconds);
            var challenge = new OtpChallenge(
                PhoneE164: phone,
                CodeHash: TokenHasher.Hash(code),
                Attempts: 0,
                ExpiresAtUtc: DateTimeOffset.UtcNow.Add(ttl),
                Purpose: "login",
                Audience: _jwtOptions.AudienceMemberWeb);

            await _otpChallenges.StoreAsync(challenge, ttl, cancellationToken);
            await _sms.SendOtpAsync(phone, code, cancellationToken);

            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.OtpRequested,
                    Outcome: AuditOutcomes.Success,
                    Message: "OTP requested.",
                    ClientIp: clientIp,
                    Audience: _jwtOptions.AudienceMemberWeb,
                    Metadata: new { phone = AuditRedaction.MaskPhone(phone) }),
                cancellationToken);

            _logger.LogInformation("RequestMemberOtp succeeded for {Phone}", AuditRedaction.MaskPhone(phone));
            return new RequestMemberOtpResponse(_otpOptions.TtlSeconds, retryAfter);
        }
        catch (AuthException ex) when (ex.ErrorCode is not "otp_rate_limited")
        {
            _logger.LogWarning(ex, "RequestMemberOtp failed {ErrorCode} for {Phone}", ex.ErrorCode, AuditRedaction.MaskPhone(phone));
            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.OtpRequestFailed,
                    Outcome: AuditOutcomes.Failure,
                    Message: "OTP request failed.",
                    ClientIp: clientIp,
                    Metadata: new { phone = AuditRedaction.MaskPhone(phone), errorCode = ex.ErrorCode }),
                cancellationToken);
            throw;
        }
    }

    public async Task<TokenResponse> VerifyMemberOtpAsync(
        VerifyMemberOtpRequest request,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNormalizer.NormalizeIndianMobile(request.Phone);
        var audience = string.IsNullOrWhiteSpace(request.Audience)
            ? _jwtOptions.AudienceMemberWeb
            : request.Audience.Trim();
        _logger.LogInformation("VerifyMemberOtp starting for {Phone} audience {Audience}", AuditRedaction.MaskPhone(phone), audience);

        if (!string.Equals(audience, _jwtOptions.AudienceMemberWeb, StringComparison.Ordinal)
            && !string.Equals(audience, _jwtOptions.AudienceMemberMobile, StringComparison.Ordinal))
        {
            throw new AuthException("invalid_audience", "Audience must be member-web or member-mobile.");
        }

        var challenge = await _otpChallenges.GetAsync(phone, cancellationToken);
        if (challenge is null || challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await WriteOtpFailedAsync(phone, audience, "otp_expired", cancellationToken);
            throw new AuthException("otp_expired", "OTP expired or not found. Request a new code.", statusCode: 401);
        }

        if (challenge.Attempts >= _otpOptions.MaxAttempts)
        {
            await _otpChallenges.RemoveAsync(phone, cancellationToken);
            await WriteOtpFailedAsync(phone, audience, "otp_locked", cancellationToken);
            throw new AuthException("otp_locked", "Too many invalid attempts. Request a new code.", statusCode: 401);
        }

        var codeHash = TokenHasher.Hash(request.Code?.Trim() ?? string.Empty);
        if (!SecureEquals.Hex(codeHash, challenge.CodeHash))
        {
            var stillValid = await _otpChallenges.IncrementAttemptsAsync(phone, cancellationToken);
            if (!stillValid)
            {
                await WriteOtpFailedAsync(phone, audience, "otp_locked", cancellationToken);
                throw new AuthException("otp_locked", "Too many invalid attempts. Request a new code.", statusCode: 401);
            }

            await WriteOtpFailedAsync(phone, audience, "otp_invalid", cancellationToken);
            throw new AuthException("otp_invalid", "Invalid OTP code.", statusCode: 401);
        }

        await _otpChallenges.RemoveAsync(phone, cancellationToken);

        var user = await RequireRegisteredActiveMemberAsync(phone, cancellationToken);
        await _users.MarkPhoneConfirmedAsync(user.Id, cancellationToken);
        await _users.TouchLastLoginAsync(user.Id, cancellationToken);

        user = await _users.FindByIdAsync(user.Id, cancellationToken)
            ?? throw new AuthException("user_missing", "Account could not be loaded.", statusCode: 500);

        var refresh = _tokens.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        var refreshDays = _jwtOptions.RefreshDaysForAudience(audience);

        await _refreshSessions.AddAsync(
            new RefreshSessionRecord(
                Id: refresh.SessionId,
                UserId: user.Id,
                Audience: audience,
                TokenHash: refresh.TokenHash,
                FamilyId: refresh.FamilyId,
                DeviceLabel: null,
                CreatedAtUtc: now,
                ExpiresAtUtc: now.AddDays(refreshDays),
                RevokedAtUtc: null,
                ReplacedAtUtc: null,
                ReplacedBySessionId: null),
            cancellationToken);

        var access = _tokens.CreateAccessToken(user, audience, refresh.SessionId, amr: "otp", authTimeUtc: now);

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
                Metadata: new { phone = AuditRedaction.MaskPhone(phone) }),
            cancellationToken);

        _logger.LogInformation("VerifyMemberOtp succeeded for user {UserId}", user.Id);
        return new TokenResponse(
            AccessToken: access.AccessToken,
            RefreshToken: refresh.RefreshToken,
            TokenType: "Bearer",
            ExpiresInSeconds: (int)(access.ExpiresAtUtc - now).TotalSeconds,
            Account: await MapAccountAsync(user, cancellationToken));
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

        if (session.ReplacedAtUtc is not null || session.RevokedAtUtc is not null)
        {
            await _refreshSessions.RevokeFamilyAsync(session.FamilyId, cancellationToken);
            throw new AuthException("refresh_reuse", "Refresh token reuse detected. Sign in again.", statusCode: 401);
        }

        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await _refreshSessions.RevokeAsync(session.Id, cancellationToken);
            throw new AuthException("refresh_expired", "Refresh token expired. Sign in again.", statusCode: 401);
        }

        var user = await _users.FindByIdAsync(session.UserId, cancellationToken);
        if (user is null)
        {
            await _refreshSessions.RevokeFamilyAsync(session.FamilyId, cancellationToken);
            throw new AuthException("invalid_refresh", "Account no longer exists.", statusCode: 401);
        }

        var issued = _tokens.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        var refreshDays = _jwtOptions.RefreshDaysForAudience(session.Audience);
        var newSession = new RefreshSessionRecord(
            Id: issued.SessionId,
            UserId: user.Id,
            Audience: session.Audience,
            TokenHash: issued.TokenHash,
            FamilyId: session.FamilyId,
            DeviceLabel: session.DeviceLabel,
            CreatedAtUtc: now,
            ExpiresAtUtc: now.AddDays(refreshDays),
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

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.TokenRefreshed,
                Outcome: AuditOutcomes.Success,
                Message: "Refresh token rotated.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Audience: session.Audience),
            cancellationToken);

        _logger.LogInformation("RefreshToken succeeded for user {UserId}", user.Id);
        return new TokenResponse(
            AccessToken: access.AccessToken,
            RefreshToken: issued.RefreshToken,
            TokenType: "Bearer",
            ExpiresInSeconds: (int)(access.ExpiresAtUtc - now).TotalSeconds,
            Account: await MapAccountAsync(user, cancellationToken));
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

    public async Task DeleteMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteMember {UserId}", userId);
        // Soft-delete related rows first, then the user (frees phone for a brand-new account).
        await _refreshSessions.SoftDeleteAllForUserAsync(userId, cancellationToken);
        await _profiles.SoftDeleteByUserIdAsync(userId, cancellationToken);
        await _photos.SoftDeleteAllForUserAsync(userId, cancellationToken);
        await _introductionVideos.SoftDeleteByUserIdAsync(userId, cancellationToken);
        await _users.SoftDeleteMemberAsync(userId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberDeleted,
                Outcome: AuditOutcomes.Success,
                Message: "Member soft-deleted.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: userId.ToString("D"),
                Changes: AuditChanges.Create([("isDeleted", false, true)])),
            cancellationToken);
    }

    public async Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeactivateMember {UserId}", userId);
        await _users.DeactivateMemberAsync(userId, cancellationToken);
        await _refreshSessions.RevokeAllForUserAsync(userId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberDeactivated,
                Outcome: AuditOutcomes.Success,
                Message: "Member deactivated.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: userId.ToString("D"),
                Changes: AuditChanges.Create([("isActive", true, false)])),
            cancellationToken);
    }

    public async Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivateMember {UserId}", userId);
        await _users.ActivateMemberAsync(userId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberActivated,
                Outcome: AuditOutcomes.Success,
                Message: "Member activated.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: userId.ToString("D"),
                Changes: AuditChanges.Create([("isActive", false, true)])),
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

    private Task WriteOtpFailedAsync(
        string phone,
        string audience,
        string errorCode,
        CancellationToken cancellationToken) =>
        _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.OtpFailed,
                Outcome: AuditOutcomes.Failure,
                Message: "OTP verification failed.",
                Audience: audience,
                Metadata: new { phone = AuditRedaction.MaskPhone(phone), errorCode }),
            cancellationToken);

    private async Task SendEmailVerificationAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        var token = await _users.GenerateEmailConfirmationTokenAsync(userId, cancellationToken);
        var baseUrl = (_emailOptions.VerifyLinkBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new AuthException(
                "email_config_invalid",
                "Email verification link base URL is not configured.",
                statusCode: 500);
        }

        var verifyUrl =
            $"{baseUrl}?userId={userId:D}&token={Uri.EscapeDataString(token)}";
        await _email.SendVerificationLinkAsync(email, verifyUrl, cancellationToken);
    }

    private async Task<UserRecord> RequireRegisteredActiveMemberAsync(
        string phoneE164,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByPhoneAsync(phoneE164, cancellationToken);
        if (user is null || user.IsDeleted)
        {
            throw new AuthException(
                "user_not_found",
                "No account found for this phone. Register first.",
                statusCode: 404);
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
