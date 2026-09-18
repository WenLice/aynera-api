using AutoMapper;
using Aynera.Application.Common;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Auth.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Users.Services.Implementations;

public sealed class RegistrationService(
    IUserRepository users, IMemberProfileRepository profiles, IEarlyAccessCityRepository cities,
    IWorkflowTransaction transaction, IVerificationEmailQueue emails, IMapper mapper, IAuditWriter audit,
    IOptions<EmailOptions> emailOptions, ILogger<RegistrationService> logger,
    IOtpChallengeRepository otpChallenges, ISmsService sms, IEmailService email, IAuthService sessions,
    IOptions<OtpOptions> otpOptions, IOptions<JwtOptions> jwtOptions) : IRegistrationService
{
    public async Task<AuthAccountDto> RegisterAsync(
        CreateMemberRequest request,
        CancellationToken cancellationToken)
    {
        AgeRules.EnsureAdult(request.DateOfBirth);

        var phone = PhoneNormalizer.NormalizeIndianMobile(request.Phone);
        var email = request.Email.Trim();
        logger.LogInformation("CreateMember starting for masked phone {Phone}", AuditRedaction.MaskPhone(phone));

        if (!Uri.TryCreate(emailOptions.Value.VerifyLinkBaseUrl, UriKind.Absolute, out var verifyUri)
            || (verifyUri.Scheme != "http" && verifyUri.Scheme != "https"))
            throw new AuthException("email_config_invalid", "Email verification link base URL is invalid.", statusCode: 500);

        // The member's city must be a catalogued, active city so that members and venues share one
        // city key. The canonical catalog name is stored, not what the member typed.
        var cityName = request.City.Trim();
        var city = await cities.FindOpenByNameAsync(cityName, cancellationToken)
            ?? throw new AuthException("city_not_supported", $"Aynera is not available in {cityName} yet.");

        var (user, profile) = await transaction.ExecuteAsync(
            new[] { "registration:phone:" + phone, "registration:email:" + email.ToUpperInvariant() },
            async ct =>
            {
                var createdUser = await users.CreateMemberAsync(
                    phone,
                    email,
                    string.IsNullOrWhiteSpace(request.Password) ? null : request.Password,
                    ct);
                var createdProfile = await profiles.CreateAsync(
                    new MemberProfileRecord(
                        createdUser.Id,
                        request.Name.Trim(),
                        request.Gender.ToString(),
                        request.DateOfBirth,
                        city.Name,
                        CityId: city.Id,
                        Nickname: request.Nickname,
                        HeightCm: request.HeightCm,
                        Hometown: request.Hometown,
                        Work: request.Work,
                        Religion: request.Religion),
                    ct);

                await emails.EnqueueAsync(createdUser.Id, ct);
                return (createdUser, createdProfile);
            }, cancellationToken);

        await audit.WriteAsync(
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
                    ("name", null, profile.Name),
                    ("gender", null, profile.Gender),
                    ("city", null, profile.City),
                    ("cityId", null, city.Id.ToString("D")),
                    ("phone", null, AuditRedaction.MaskPhone(phone)),
                    ("email", null, AuditRedaction.MaskEmail(email))
                ])),
            cancellationToken);

        logger.LogInformation("CreateMember succeeded for user {UserId}", user.Id);
        return mapper.Map<AuthAccountDto>(user) with { Profile = mapper.Map<MemberProfileDto>(profile) };
    }

    // ----- Step-wise registration (the app's phone -> code -> email -> code flow) -----

    public async Task<RequestMemberOtpResponse> StartPhoneRegistrationAsync(
        StartPhoneRegistrationRequest request,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNormalizer.NormalizeIndianMobile(request.Phone);
        var masked = AuditRedaction.MaskPhone(phone);
        logger.LogInformation("StartPhoneRegistration for {Phone}", masked);

        try
        {
            await EnsurePhoneFreeAsync(phone, cancellationToken);

            var (allowed, retryAfter) = await otpChallenges.TryAcquireRequestSlotAsync(
                PhoneChannel, phone, clientIp, cancellationToken);
            if (!allowed)
            {
                await WriteOtpRequestDeniedAsync(masked, PhoneChannel, clientIp, "otp_rate_limited", cancellationToken);
                throw new AuthException("otp_rate_limited", "Too many OTP requests. Try again later.", statusCode: 429);
            }

            var code = OtpCodeGenerator.Generate(otpOptions.Value.CodeLength);
            var ttl = TimeSpan.FromSeconds(otpOptions.Value.TtlSeconds);
            await otpChallenges.StoreAsync(
                new OtpChallenge(
                    Channel: PhoneChannel,
                    Destination: phone,
                    CodeHash: TokenHasher.Hash(code),
                    Attempts: 0,
                    ExpiresAtUtc: DateTimeOffset.UtcNow.Add(ttl),
                    Purpose: OtpPurposes.Registration,
                    Audience: jwtOptions.Value.AudienceMember),
                ttl,
                cancellationToken);
            await sms.SendOtpAsync(phone, code, cancellationToken);

            await audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.OtpRequested,
                    Outcome: AuditOutcomes.Success,
                    Message: "Registration OTP requested.",
                    ClientIp: clientIp,
                    Audience: jwtOptions.Value.AudienceMember,
                    Metadata: new { identifier = masked, channel = PhoneChannel, purpose = OtpPurposes.Registration }),
                cancellationToken);

            logger.LogInformation("StartPhoneRegistration succeeded for {Phone}", masked);
            return new RequestMemberOtpResponse(otpOptions.Value.TtlSeconds, retryAfter);
        }
        catch (AuthException ex) when (ex.ErrorCode is not "otp_rate_limited")
        {
            logger.LogWarning(ex, "StartPhoneRegistration failed {ErrorCode} for {Phone}", ex.ErrorCode, masked);
            await WriteOtpRequestDeniedAsync(masked, PhoneChannel, clientIp, ex.ErrorCode, cancellationToken);
            throw;
        }
    }

    public async Task<TokenResponse> VerifyPhoneRegistrationAsync(
        VerifyPhoneRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNormalizer.NormalizeIndianMobile(request.Phone);
        var masked = AuditRedaction.MaskPhone(phone);
        logger.LogInformation("VerifyPhoneRegistration for {Phone}", masked);

        await ConsumeCodeAsync(PhoneChannel, phone, request.Code, OtpPurposes.Registration, masked, cancellationToken);

        // The proof is consumed; now the account exists exactly once even if two verifies race.
        var user = await transaction.ExecuteAsync(new[] { "registration:phone:" + phone }, async ct =>
        {
            var created = await users.CreateMemberAsync(phone, email: null, password: null, ct);
            await users.MarkPhoneConfirmedAsync(created.Id, ct);
            return created;
        }, cancellationToken);

        await audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberRegistered,
                Outcome: AuditOutcomes.Success,
                Message: "Member account created from a verified phone (app registration).",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("phone", null, masked),
                    ("phoneConfirmed", false, true)
                ]),
                Metadata: new { flow = "phone_otp" }),
            cancellationToken);

        var tokens = await sessions.IssueMemberSessionAsync(user.Id, amr: "otp", cancellationToken);
        logger.LogInformation("VerifyPhoneRegistration created user {UserId}", user.Id);
        return tokens;
    }

    public async Task<RequestMemberOtpResponse> StartEmailVerificationAsync(
        Guid userId,
        StartEmailVerificationRequest request,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var address = request.Email.Trim();
        var destination = EmailNormalizer.Normalize(address);
        var masked = AuditRedaction.MaskEmail(address) ?? "***";
        logger.LogInformation("StartEmailVerification for user {UserId} {Email}", userId, masked);

        try
        {
            var owner = await users.FindByEmailAsync(address, cancellationToken);
            if (owner is not null && owner.Id != userId && !owner.IsDeleted)
            {
                throw new AuthException(
                    "email_already_exists",
                    "A member account with this email already exists.",
                    statusCode: 409);
            }

            var (allowed, retryAfter) = await otpChallenges.TryAcquireRequestSlotAsync(
                EmailChannel, destination, clientIp, cancellationToken);
            if (!allowed)
            {
                await WriteOtpRequestDeniedAsync(masked, EmailChannel, clientIp, "otp_rate_limited", cancellationToken);
                throw new AuthException("otp_rate_limited", "Too many OTP requests. Try again later.", statusCode: 429);
            }

            var code = OtpCodeGenerator.Generate(otpOptions.Value.CodeLength);
            var ttl = TimeSpan.FromSeconds(otpOptions.Value.TtlSeconds);
            await otpChallenges.StoreAsync(
                new OtpChallenge(
                    Channel: EmailChannel,
                    Destination: destination,
                    CodeHash: TokenHasher.Hash(code),
                    Attempts: 0,
                    ExpiresAtUtc: DateTimeOffset.UtcNow.Add(ttl),
                    Purpose: OtpPurposes.EmailVerification,
                    Audience: jwtOptions.Value.AudienceMember),
                ttl,
                cancellationToken);
            await email.SendOtpAsync(address, code, cancellationToken);

            await audit.WriteAsync(
                new AuditEventWriteModel(
                    Action: AuditActions.OtpRequested,
                    Outcome: AuditOutcomes.Success,
                    Message: "Email verification code requested.",
                    UserId: userId,
                    SubjectUserId: userId,
                    SubjectType: AuditSubjectTypes.User,
                    SubjectId: userId.ToString("D"),
                    ClientIp: clientIp,
                    Audience: jwtOptions.Value.AudienceMember,
                    Metadata: new { identifier = masked, channel = EmailChannel, purpose = OtpPurposes.EmailVerification }),
                cancellationToken);

            logger.LogInformation("StartEmailVerification succeeded for user {UserId}", userId);
            return new RequestMemberOtpResponse(otpOptions.Value.TtlSeconds, retryAfter);
        }
        catch (AuthException ex) when (ex.ErrorCode is not "otp_rate_limited")
        {
            logger.LogWarning(ex, "StartEmailVerification failed {ErrorCode} for user {UserId}", ex.ErrorCode, userId);
            await WriteOtpRequestDeniedAsync(masked, EmailChannel, clientIp, ex.ErrorCode, cancellationToken);
            throw;
        }
    }

    public async Task<AuthAccountDto> VerifyEmailCodeAsync(
        Guid userId,
        VerifyEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        var address = request.Email.Trim();
        var destination = EmailNormalizer.Normalize(address);
        var masked = AuditRedaction.MaskEmail(address) ?? "***";
        logger.LogInformation("VerifyEmailCode for user {UserId} {Email}", userId, masked);

        await ConsumeCodeAsync(EmailChannel, destination, request.Code, OtpPurposes.EmailVerification, masked, cancellationToken);

        var user = await transaction.ExecuteAsync(new[] { $"account:{userId:D}" }, async ct =>
        {
            await users.SetConfirmedEmailAsync(userId, address, ct);
            return await users.FindByIdAsync(userId, ct)
                ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }, cancellationToken);

        await audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.EmailConfirmed,
                Outcome: AuditOutcomes.Success,
                Message: "Email confirmed by code.",
                UserId: user.Id,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("email", null, masked),
                    ("emailConfirmed", false, true)
                ])),
            cancellationToken);

        var profile = await profiles.FindByUserIdAsync(user.Id, cancellationToken);
        logger.LogInformation("VerifyEmailCode succeeded for user {UserId}", user.Id);
        return mapper.Map<AuthAccountDto>(user) with
        {
            Profile = profile is null ? null : mapper.Map<MemberProfileDto>(profile)
        };
    }

    public async Task<AuthAccountDto> SaveProfileAsync(
        Guid userId,
        UpdateMemberProfileRequest request,
        CancellationToken cancellationToken)
    {
        AgeRules.EnsureAdult(request.DateOfBirth);
        logger.LogInformation("SaveProfile for user {UserId}", userId);

        // Same rule as one-shot registration: the member's city must be a catalogued, active city so
        // that members and venues share one key. The canonical catalog name is stored, not what was typed.
        var cityName = request.City.Trim();
        var city = await cities.FindOpenByNameAsync(cityName, cancellationToken)
            ?? throw new AuthException("city_not_supported", $"Aynera is not available in {cityName} yet.");

        var (user, before, after) = await transaction.ExecuteAsync(
            new[] { $"account:{userId:D}" },
            async ct =>
            {
                var account = await users.FindByIdAsync(userId, ct)
                    ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

                var previous = await profiles.FindByUserIdAsync(userId, ct);
                var saved = await profiles.UpsertAsync(
                    new MemberProfileRecord(
                        userId,
                        request.Name.Trim(),
                        request.Gender.ToString(),
                        request.DateOfBirth,
                        city.Name,
                        CityId: city.Id,
                        Nickname: request.Nickname,
                        HeightCm: request.HeightCm,
                        Hometown: request.Hometown,
                        Work: request.Work,
                        Religion: request.Religion),
                    ct);

                return (account, previous, saved);
            },
            cancellationToken);

        await audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberProfileSaved,
                Outcome: AuditOutcomes.Success,
                Message: before is null ? "Member profile created." : "Member profile updated.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: userId.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("name", before?.Name, after.Name),
                    ("nickname", before?.Nickname, after.Nickname),
                    ("gender", before?.Gender, after.Gender),
                    ("city", before?.City, after.City),
                    ("cityId", before?.CityId.ToString("D"), city.Id.ToString("D")),
                    ("heightCm", before?.HeightCm, after.HeightCm),
                    ("hometown", before?.Hometown, after.Hometown),
                    ("work", before?.Work, after.Work)
                ])),
            cancellationToken);

        logger.LogInformation("SaveProfile succeeded for user {UserId}", userId);
        return mapper.Map<AuthAccountDto>(user) with { Profile = mapper.Map<MemberProfileDto>(after) };
    }

    // ----- helpers -----

    private const string PhoneChannel = "phone";
    private const string EmailChannel = "email";

    /// <summary>Mirrors the repository's registration rule so the start step fails the same way as creation would.</summary>
    private async Task EnsurePhoneFreeAsync(string phone, CancellationToken cancellationToken)
    {
        var existing = await users.FindByPhoneAsync(phone, cancellationToken);
        if (existing is null || existing.IsDeleted)
        {
            return;
        }

        if (!existing.IsActive)
        {
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before signing in; re-registration is not allowed.",
                statusCode: 409);
        }

        if (existing.IsRestricted)
        {
            throw new AuthException(
                "account_restricted",
                "This account is restricted. Contact support; re-registration is not allowed.",
                statusCode: 409);
        }

        throw new AuthException(
            "user_already_exists",
            "A member account with this phone already exists. Sign in with the existing account.",
            statusCode: 409);
    }

    private async Task ConsumeCodeAsync(
        string channel,
        string destination,
        string? code,
        string purpose,
        string masked,
        CancellationToken cancellationToken)
    {
        var outcome = await otpChallenges.TryConsumeAsync(
            channel,
            destination,
            TokenHasher.Hash(code?.Trim() ?? string.Empty),
            purpose,
            jwtOptions.Value.AudienceMember,
            cancellationToken);

        var errorCode = outcome switch
        {
            OtpConsumeOutcome.Consumed => null,
            OtpConsumeOutcome.Invalid => "otp_invalid",
            OtpConsumeOutcome.Locked => "otp_locked",
            _ => "otp_expired"
        };
        if (errorCode is null)
        {
            return;
        }

        await audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.OtpFailed,
                Outcome: AuditOutcomes.Failure,
                Message: "OTP verification failed.",
                Audience: jwtOptions.Value.AudienceMember,
                Metadata: new { identifier = masked, channel, purpose, errorCode }),
            cancellationToken);

        throw errorCode switch
        {
            "otp_invalid" => new AuthException("otp_invalid", "Invalid OTP code.", statusCode: 401),
            "otp_locked" => new AuthException("otp_locked", "Too many invalid attempts. Request a new code.", statusCode: 401),
            _ => new AuthException("otp_expired", "OTP expired or not found. Request a new code.", statusCode: 401)
        };
    }

    private Task WriteOtpRequestDeniedAsync(
        string masked,
        string channel,
        string? clientIp,
        string errorCode,
        CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.OtpRequestFailed,
                Outcome: errorCode == "otp_rate_limited" ? AuditOutcomes.Denied : AuditOutcomes.Failure,
                Message: "OTP request failed.",
                ClientIp: clientIp,
                Metadata: new { identifier = masked, channel, errorCode }),
            cancellationToken);
}
