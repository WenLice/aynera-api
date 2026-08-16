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
using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Auth.Statics;
using Elaris.Domain.Common;
using Elaris.Domain.Photos.Exceptions;
using Elaris.Domain.Photos.Responses;
using Elaris.Domain.Videos.Exceptions;
using Elaris.Domain.Videos.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Features.Auth.Services.Implementations;

public sealed class AuthService : IAuthService, IAdminAuthService
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

        var user = await _users.CreateMemberAsync(
            phone,
            email,
            string.IsNullOrWhiteSpace(request.Password) ? null : request.Password,
            cancellationToken);
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

    public async Task<AuthAccountDto> CreateAdminAsync(
        Guid actorId,
        CreateAdminRequest request,
        CancellationToken cancellationToken)
    {
        await RequireSuperAdminAsync(actorId, cancellationToken);

        var email = EmailNormalizer.Normalize(request.Email);
        string? phoneE164 = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            phoneE164 = PhoneNormalizer.NormalizeIndianMobile(request.Phone);
        }

        var user = await _users.CreateAdminAsync(
            email,
            phoneE164,
            request.Password,
            cancellationToken,
            isSuperAdmin: false);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.AdminCreated,
                Outcome: AuditOutcomes.Success,
                Message: "Admin account created.",
                UserId: actorId,
                SubjectUserId: user.Id,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: user.Id.ToString("D"),
                Audience: _jwtOptions.AudienceAdmin,
                Changes: AuditChanges.Create(
                [
                    ("email", null, AuditRedaction.MaskEmail(email)),
                    ("phone", null, string.IsNullOrWhiteSpace(phoneE164) ? null : AuditRedaction.MaskPhone(phoneE164))
                ])),
            cancellationToken);

        _logger.LogInformation("CreateAdmin succeeded for user {UserId}", user.Id);
        return MapAccount(user, profile: null);
    }

    public async Task<PagedResult<AuthAccountDto>> ListAdminsAsync(
        Guid actorId,
        PagedQuery query,
        CancellationToken cancellationToken)
    {
        await RequireSuperAdminAsync(actorId, cancellationToken);
        _logger.LogInformation("ListAdmins page {Page} size {PageSize}", query.Page, query.PageSize);
        var (rows, totalCount) = await _users.ListAdminsPageAsync(query.Skip, query.PageSize, cancellationToken);
        return new PagedResult<AuthAccountDto>(
            rows.Select(row => MapAccount(row, profile: null)).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<PagedResult<MemberAdminDto>> ListMembersAsync(
        Guid actorId,
        MemberAdminListQuery query,
        CancellationToken cancellationToken)
    {
        await RequireAdminActorAsync(actorId, cancellationToken);
        _logger.LogInformation(
            "ListMembers page {Page} size {PageSize} search {HasSearch} active {IsActive} restricted {IsRestricted}",
            query.Page,
            query.PageSize,
            !string.IsNullOrWhiteSpace(query.Search),
            query.IsActive,
            query.IsRestricted);
        var (rows, totalCount) = await _users.ListMembersPageAsync(
            query.Skip,
            query.PageSize,
            query.Search,
            query.IsActive,
            query.IsRestricted,
            cancellationToken);
        return new PagedResult<MemberAdminDto>(
            rows.Select(row => _mapper.Map<MemberAdminDto>(row)).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<MemberAdminDetailDto> GetMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await RequireAdminActorAsync(actorId, cancellationToken);
        var row = await RequireMemberTargetAsync(memberId, cancellationToken);
        _logger.LogInformation("GetMember {MemberId}", memberId);

        var photos = await _photos.ListByUserIdAsync(memberId, cancellationToken);
        var video = await _introductionVideos.FindByUserIdAsync(memberId, cancellationToken);
        return new MemberAdminDetailDto(
            row.Id,
            row.Phone,
            row.PhoneConfirmed,
            row.Email,
            row.EmailConfirmed,
            row.IsActive,
            row.IsRestricted,
            row.CreatedAtUtc,
            row.FirstName,
            row.LastName,
            row.Gender,
            row.DateOfBirth,
            row.City,
            row.Religion,
            _mapper.Map<List<MemberPhotoDto>>(photos),
            video is null ? null : _mapper.Map<IntroductionVideoDto>(video));
    }

    public async Task<MemberPhotoBytes> GetMemberPhotoAsync(
        Guid actorId,
        Guid memberId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        await RequireAdminActorAsync(actorId, cancellationToken);
        await RequireMemberTargetAsync(memberId, cancellationToken);
        var photo = await _photos.FindByIdAsync(memberId, photoId, cancellationToken)
            ?? throw new PhotoException("photo_not_found", "Photo not found.", statusCode: 404);
        return new MemberPhotoBytes(photo.Id, photo.ContentType, photo.Data);
    }

    public async Task<IntroductionVideoBytes> GetMemberVideoAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await RequireAdminActorAsync(actorId, cancellationToken);
        await RequireMemberTargetAsync(memberId, cancellationToken);
        var video = await _introductionVideos.FindByUserIdAsync(memberId, cancellationToken)
            ?? throw new VideoException("video_not_found", "Introduction video not found.", statusCode: 404);
        return new IntroductionVideoBytes(video.UserId, video.ContentType, video.Data);
    }

    public async Task<MemberAdminDto> RestrictMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await RequireSuperAdminAsync(actorId, cancellationToken);
        var target = await RequireMemberTargetAsync(memberId, cancellationToken);
        if (target.IsRestricted)
        {
            return _mapper.Map<MemberAdminDto>(target);
        }

        await _users.RestrictMemberAsync(memberId, cancellationToken);
        await _refreshSessions.RevokeAllForUserAsync(memberId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberRestricted,
                Outcome: AuditOutcomes.Success,
                Message: "Member restricted by super-admin.",
                UserId: actorId,
                SubjectUserId: memberId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: memberId.ToString("D"),
                Audience: _jwtOptions.AudienceAdmin,
                Changes: AuditChanges.Create([("isRestricted", false, true)])),
            cancellationToken);

        var updated = await RequireMemberTargetAsync(memberId, cancellationToken);
        _logger.LogInformation("RestrictMember {MemberId} by {ActorId}", memberId, actorId);
        return _mapper.Map<MemberAdminDto>(updated);
    }

    public async Task<MemberAdminDto> UnrestrictMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await RequireSuperAdminAsync(actorId, cancellationToken);
        var target = await RequireMemberTargetAsync(memberId, cancellationToken);
        if (!target.IsRestricted)
        {
            return _mapper.Map<MemberAdminDto>(target);
        }

        await _users.UnrestrictMemberAsync(memberId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.MemberUnrestricted,
                Outcome: AuditOutcomes.Success,
                Message: "Member unrestricted by super-admin.",
                UserId: actorId,
                SubjectUserId: memberId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: memberId.ToString("D"),
                Audience: _jwtOptions.AudienceAdmin,
                Changes: AuditChanges.Create([("isRestricted", true, false)])),
            cancellationToken);

        var updated = await RequireMemberTargetAsync(memberId, cancellationToken);
        _logger.LogInformation("UnrestrictMember {MemberId} by {ActorId}", memberId, actorId);
        return _mapper.Map<MemberAdminDto>(updated);
    }

    public async Task<AuthAccountDto> DeactivateAdminAsync(
        Guid actorId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        await RequireSuperAdminAsync(actorId, cancellationToken);
        if (actorId == targetId)
        {
            throw new AuthException(
                "cannot_deactivate_self",
                "You cannot deactivate your own account.",
                statusCode: 409);
        }

        var target = await RequireAdminTargetAsync(targetId, cancellationToken);
        if (!target.IsActive)
        {
            return MapAccount(target, profile: null);
        }

        var activeAdmins = await _users.CountActiveAdminsAsync(cancellationToken);
        if (activeAdmins <= 1)
        {
            throw new AuthException(
                "last_admin",
                "The last active admin cannot be deactivated.",
                statusCode: 409);
        }

        if (target.IsSuperAdmin)
        {
            var activeSupers = await _users.CountActiveSuperAdminsAsync(cancellationToken);
            if (activeSupers <= 1)
            {
                throw new AuthException(
                    "last_super_admin",
                    "The last active super-admin cannot be deactivated.",
                    statusCode: 409);
            }
        }

        await _users.DeactivateMemberAsync(targetId, cancellationToken);
        await _refreshSessions.RevokeAllForUserAsync(targetId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.AdminDeactivated,
                Outcome: AuditOutcomes.Success,
                Message: "Admin account deactivated.",
                UserId: actorId,
                SubjectUserId: targetId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: targetId.ToString("D"),
                Audience: _jwtOptions.AudienceAdmin,
                Changes: AuditChanges.Create([("isActive", true, false)])),
            cancellationToken);

        var updated = await RequireAdminTargetAsync(targetId, cancellationToken);
        _logger.LogInformation("DeactivateAdmin succeeded for user {UserId}", targetId);
        return MapAccount(updated, profile: null);
    }

    public async Task<AuthAccountDto> ActivateAdminAsync(
        Guid actorId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        await RequireSuperAdminAsync(actorId, cancellationToken);
        var target = await RequireAdminTargetAsync(targetId, cancellationToken);
        if (target.IsActive)
        {
            return MapAccount(target, profile: null);
        }

        await _users.ActivateMemberAsync(targetId, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.AdminActivated,
                Outcome: AuditOutcomes.Success,
                Message: "Admin account activated.",
                UserId: actorId,
                SubjectUserId: targetId,
                SubjectType: AuditSubjectTypes.User,
                SubjectId: targetId.ToString("D"),
                Audience: _jwtOptions.AudienceAdmin,
                Changes: AuditChanges.Create([("isActive", false, true)])),
            cancellationToken);

        var updated = await RequireAdminTargetAsync(targetId, cancellationToken);
        _logger.LogInformation("ActivateAdmin succeeded for user {UserId}", targetId);
        return MapAccount(updated, profile: null);
    }

    private async Task<MemberAdminRecord> RequireMemberTargetAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await _users.FindMemberAdminAsync(memberId, cancellationToken);
        if (member is null)
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        return member;
    }

    private async Task<UserRecord> RequireAdminTargetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null
            || !string.Equals(user.AccountKind, nameof(AccountKind.Admin), StringComparison.Ordinal))
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        return user;
    }

    private async Task<UserRecord> RequireAdminActorAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await _users.FindByIdAsync(actorId, cancellationToken);
        if (actor is null
            || !string.Equals(actor.AccountKind, nameof(AccountKind.Admin), StringComparison.Ordinal))
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        return actor;
    }

    private async Task RequireSuperAdminAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await RequireAdminActorAsync(actorId, cancellationToken);
        if (!actor.IsSuperAdmin)
        {
            throw new AuthException(
                "super_admin_required",
                "Super-admin rights are required.",
                statusCode: 403);
        }
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
        if (user is null || user.IsDeleted)
        {
            await _refreshSessions.RevokeFamilyAsync(session.FamilyId, cancellationToken);
            throw new AuthException("invalid_refresh", "Account no longer exists.", statusCode: 401);
        }

        if (user.IsRestricted)
        {
            await _refreshSessions.RevokeAllForUserAsync(user.Id, cancellationToken);
            throw new AuthException(
                "account_restricted",
                "This account is restricted. Contact support.",
                statusCode: 403);
        }

        if (!user.IsActive)
        {
            await _refreshSessions.RevokeAllForUserAsync(user.Id, cancellationToken);
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before continuing.",
                statusCode: 403);
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

    async Task<AuthAccountDto> IAdminAuthService.GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAdminMe {UserId}", userId);
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null
            || !string.Equals(user.AccountKind, nameof(AccountKind.Admin), StringComparison.Ordinal))
        {
            _logger.LogWarning("GetAdminMe user not found {UserId}", userId);
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        return MapAccount(user, profile: null);
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
        var challenge = await _otpChallenges.GetAsync(
            identifier.ChannelKey,
            identifier.Destination,
            cancellationToken);
        if (challenge is null
            || challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || !string.Equals(challenge.Purpose, expectedPurpose, StringComparison.Ordinal)
            || !string.Equals(challenge.Audience, audience, StringComparison.Ordinal))
        {
            await WriteOtpFailedAsync(identifier, audience, "otp_expired", cancellationToken);
            throw new AuthException("otp_expired", "OTP expired or not found. Request a new code.", statusCode: 401);
        }

        if (challenge.Attempts >= _otpOptions.MaxAttempts)
        {
            await _otpChallenges.RemoveAsync(identifier.ChannelKey, identifier.Destination, cancellationToken);
            await WriteOtpFailedAsync(identifier, audience, "otp_locked", cancellationToken);
            throw new AuthException("otp_locked", "Too many invalid attempts. Request a new code.", statusCode: 401);
        }

        var codeHash = TokenHasher.Hash(code?.Trim() ?? string.Empty);
        if (!SecureEquals.Hex(codeHash, challenge.CodeHash))
        {
            var stillValid = await _otpChallenges.IncrementAttemptsAsync(
                identifier.ChannelKey,
                identifier.Destination,
                cancellationToken);
            if (!stillValid)
            {
                await WriteOtpFailedAsync(identifier, audience, "otp_locked", cancellationToken);
                throw new AuthException(
                    "otp_locked",
                    "Too many invalid attempts. Request a new code.",
                    statusCode: 401);
            }

            await WriteOtpFailedAsync(identifier, audience, "otp_invalid", cancellationToken);
            throw new AuthException("otp_invalid", "Invalid OTP code.", statusCode: 401);
        }

        await _otpChallenges.RemoveAsync(identifier.ChannelKey, identifier.Destination, cancellationToken);
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

    private async Task<TokenResponse> IssueMemberTokensAsync(
        UserRecord user,
        string audience,
        string amr,
        CancellationToken cancellationToken)
    {
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
            cancellationToken);

        var access = _tokens.CreateAccessToken(user, audience, refresh.SessionId, amr, authTimeUtc: now);
        return new TokenResponse(
            AccessToken: access.AccessToken,
            RefreshToken: refresh.RefreshToken,
            TokenType: "Bearer",
            ExpiresInSeconds: (int)(access.ExpiresAtUtc - now).TotalSeconds,
            Account: await MapAccountAsync(user, cancellationToken));
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
