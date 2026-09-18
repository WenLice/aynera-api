using Aynera.Application.Common;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Preferences.Repositories;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Application.Features.Videos.Repositories;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Auth.Statics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Users.Services.Implementations;

/// <summary>Coordinates member account lifecycle and its dependent data.</summary>
public sealed class AccountLifecycleService : IAccountLifecycleService
{
    private readonly IWorkflowTransaction _transaction;
    private readonly IVerificationEmailQueue _emails;
    private readonly IUserRepository _users;
    private readonly IMemberProfileRepository _profiles;
    private readonly IMemberPreferencesRepository _preferences;
    private readonly IMemberPhotoRepository _photos;
    private readonly IIntroductionVideoRepository _introductionVideos;
    private readonly IRefreshSessionRepository _refreshSessions;
    private readonly IOtpChallengeRepository _otpChallenges;
    private readonly ISmsService _sms;
    private readonly IEmailService _email;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AccountLifecycleService> _logger;
    private readonly OtpOptions _otpOptions;
    private readonly JwtOptions _jwtOptions;

    public AccountLifecycleService(
        IUserRepository users,
        IMemberProfileRepository profiles,
        IMemberPreferencesRepository preferences,
        IMemberPhotoRepository photos,
        IIntroductionVideoRepository introductionVideos,
        IRefreshSessionRepository refreshSessions,
        IAuditWriter audit,
        ILogger<AccountLifecycleService> logger,
        IWorkflowTransaction transaction,
        IVerificationEmailQueue emails,
        IOtpChallengeRepository otpChallenges,
        ISmsService sms,
        IEmailService email,
        IOptions<OtpOptions> otpOptions,
        IOptions<JwtOptions> jwtOptions)
    {
        _transaction = transaction;
        _emails = emails;
        _users = users;
        _profiles = profiles;
        _preferences = preferences;
        _photos = photos;
        _introductionVideos = introductionVideos;
        _refreshSessions = refreshSessions;
        _otpChallenges = otpChallenges;
        _sms = sms;
        _email = email;
        _audit = audit;
        _logger = logger;
        _otpOptions = otpOptions.Value;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task DeleteMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteMember {UserId}", userId);
        await _transaction.ExecuteAsync(new[] { $"account:{userId:D}" }, async ct =>
        {
            // All stores use the transaction's scoped DbContext, including Identity writes.
            await _refreshSessions.SoftDeleteAllForUserAsync(userId, ct);
            await _profiles.SoftDeleteByUserIdAsync(userId, ct);
            await _preferences.SoftDeleteByUserIdAsync(userId, ct);
            await _photos.SoftDeleteAllForUserAsync(userId, ct);
            await _introductionVideos.SoftDeleteByUserIdAsync(userId, ct);
            await _emails.CancelAsync(userId, ct);
            await _users.SoftDeleteMemberAsync(userId, ct);
            return true;
        }, cancellationToken);

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
        await _transaction.ExecuteAsync(new[] { $"account:{userId:D}" }, async ct =>
        {
            await _users.DeactivateMemberAsync(userId, ct);
            await _refreshSessions.RevokeAllForUserAsync(userId, ct);
            return true;
        }, cancellationToken);

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
        await ActivateEligibleMemberAsync(userId, cancellationToken);
    }

    public async Task<RequestMemberOtpResponse> RequestReactivationAsync(
        RequestMemberReactivationRequest request,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        var masked = LoginIdentifiers.Mask(identifier);
        _logger.LogInformation("RequestReactivation starting for {Identifier}", masked);
        try
        {
            var user = await FindMemberForReactivationAsync(identifier, cancellationToken);
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
                        Message: "Reactivation OTP request rate limited.",
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
                Purpose: OtpPurposes.Reactivation,
                Audience: _jwtOptions.AudienceMember);

            await _otpChallenges.StoreAsync(challenge, ttl, cancellationToken);
            await DispatchOtpAsync(identifier, code, cancellationToken);

            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.MemberReactivationRequested,
                    Outcome: AuditOutcomes.Success,
                    Message: "Member reactivation OTP requested.",
                    ClientIp: clientIp,
                    Audience: _jwtOptions.AudienceMember,
                    Metadata: new { identifier = masked, channel = identifier.ChannelKey }),
                cancellationToken);

            _logger.LogInformation("RequestReactivation succeeded for {Identifier}", masked);
            return new RequestMemberOtpResponse(_otpOptions.TtlSeconds, retryAfter);
        }
        catch (AuthException ex) when (ex.ErrorCode is not "otp_rate_limited")
        {
            _logger.LogWarning(ex, "RequestReactivation failed {ErrorCode} for {Identifier}", ex.ErrorCode, masked);
            await _audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.OtpRequestFailed,
                    Outcome: AuditOutcomes.Failure,
                    Message: "Reactivation OTP request failed.",
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

    public async Task RecoverMemberAsync(RecoverMemberRequest request, CancellationToken cancellationToken)
    {
        var identifier = LoginIdentifiers.Parse(request.Identifier);
        var masked = LoginIdentifiers.Mask(identifier);
        _logger.LogInformation("RecoverMember starting for {Identifier}", masked);

        await ConsumeReactivationOtpAsync(identifier, request.Code, cancellationToken);

        var user = await FindByIdentifierAsync(identifier, cancellationToken)
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        await ActivateEligibleMemberAsync(user.Id, cancellationToken);
        _logger.LogInformation("RecoverMember succeeded for user {UserId}", user.Id);
    }

    private async Task ActivateEligibleMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _transaction.ExecuteAsync(new[] { $"account:{userId:D}" }, async ct =>
        {
            var user = await _users.FindByIdAsync(userId, ct)
                ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
            EnsureMemberEligibleForActivation(user);
            await _users.ActivateMemberAsync(user.Id, ct);
            return true;
        }, cancellationToken);

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

    private static void EnsureMemberEligibleForActivation(UserRecord user)
    {
        if (user.IsDeleted)
        {
            throw new AuthException(
                "account_deleted",
                "This account was deleted. Register again to create a new account.",
                statusCode: 409);
        }

        if (!string.Equals(user.AccountKind, nameof(AccountKind.Member), StringComparison.Ordinal))
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        if (user.IsRestricted)
        {
            throw new AuthException(
                "account_restricted",
                "This account is restricted. Contact support.",
                statusCode: 403);
        }
    }

    private async Task<UserRecord?> FindMemberForReactivationAsync(
        LoginIdentifier identifier,
        CancellationToken cancellationToken)
    {
        var user = await FindByIdentifierAsync(identifier, cancellationToken);
        if (user is null
            || user.IsDeleted
            || user.IsRestricted
            || user.IsActive
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

    private async Task ConsumeReactivationOtpAsync(
        LoginIdentifier identifier,
        string? code,
        CancellationToken cancellationToken)
    {
        var audience = _jwtOptions.AudienceMember;
        var outcome = await _otpChallenges.TryConsumeAsync(
            identifier.ChannelKey,
            identifier.Destination,
            TokenHasher.Hash(code?.Trim() ?? string.Empty),
            OtpPurposes.Reactivation,
            audience,
            cancellationToken);
        switch (outcome)
        {
            case OtpConsumeOutcome.Consumed:
                return;
            case OtpConsumeOutcome.Invalid:
                await WriteReactivationProofFailedAsync(identifier, "otp_invalid", cancellationToken);
                throw new AuthException("otp_invalid", "Invalid OTP code.", statusCode: 401);
            case OtpConsumeOutcome.Locked:
                await WriteReactivationProofFailedAsync(identifier, "otp_locked", cancellationToken);
                throw new AuthException(
                    "otp_locked",
                    "Too many invalid attempts. Request a new code.",
                    statusCode: 401);
            default:
                await WriteReactivationProofFailedAsync(identifier, "otp_expired", cancellationToken);
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

    private Task WriteReactivationProofFailedAsync(
        LoginIdentifier identifier,
        string errorCode,
        CancellationToken cancellationToken) =>
        _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.OtpFailed,
                Outcome: AuditOutcomes.Failure,
                Message: "Reactivation OTP verification failed.",
                Audience: _jwtOptions.AudienceMember,
                Metadata: new
                {
                    identifier = LoginIdentifiers.Mask(identifier),
                    channel = identifier.ChannelKey,
                    errorCode
                }),
            cancellationToken);
}
