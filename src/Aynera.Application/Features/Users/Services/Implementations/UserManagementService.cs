using AutoMapper;
using Aynera.Application.Common;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
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
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Application.Features.Videos.Services.Interfaces;
using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Videos.Responses;

namespace Aynera.Application.Features.Users.Services.Implementations;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IUserRepository _users;
    private readonly IPhotoService _photos;
    private readonly IIntroductionVideoService _introductionVideos;
    private readonly IRefreshSessionRepository _refreshSessions;
    private readonly IMapper _mapper;
    private readonly IAuditWriter _audit;
    private readonly ILogger<UserManagementService> _logger;
    private readonly JwtOptions _jwtOptions;
    private readonly IWorkflowTransaction _transaction;

    public UserManagementService(
        IUserRepository users,
        IPhotoService photos,
        IIntroductionVideoService introductionVideos,
        IRefreshSessionRepository refreshSessions,
        IMapper mapper,
        IAuditWriter audit,
        ILogger<UserManagementService> logger,
        IOptions<JwtOptions> jwtOptions,
        IWorkflowTransaction transaction)
    {
        _users = users;
        _photos = photos;
        _introductionVideos = introductionVideos;
        _refreshSessions = refreshSessions;
        _mapper = mapper;
        _audit = audit;
        _logger = logger;
        _jwtOptions = jwtOptions.Value;
        _transaction = transaction;
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
        return _mapper.Map<AuthAccountDto>(user);
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
            rows.Select(row => _mapper.Map<AuthAccountDto>(row)).ToList(),
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

        var photos = await _photos.ListAsync(memberId, cancellationToken);
        var video = await _introductionVideos.GetAsync(memberId, cancellationToken);
        return new MemberAdminDetailDto(
            row.Id,
            row.Phone,
            row.PhoneConfirmed,
            row.Email,
            row.EmailConfirmed,
            row.IsActive,
            row.IsRestricted,
            row.CreatedAtUtc,
            row.Name,
            row.Gender,
            row.DateOfBirth,
            row.City,
            row.Religion,
            photos,
            video,
            row.CityId,
            row.Nickname,
            row.HeightCm,
            row.Hometown,
            row.Work,
            row.GenderIsPublic);
    }

    public async Task<MemberPhotoBytes> GetMemberPhotoAsync(
        Guid actorId,
        Guid memberId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        await RequireAdminActorAsync(actorId, cancellationToken);
        await RequireMemberTargetAsync(memberId, cancellationToken);
        return await _photos.GetBytesAsync(memberId, photoId, cancellationToken);
    }

    public async Task<IntroductionVideoBytes> GetMemberVideoAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await RequireAdminActorAsync(actorId, cancellationToken);
        await RequireMemberTargetAsync(memberId, cancellationToken);
        return await _introductionVideos.GetBytesAsync(memberId, cancellationToken);
    }

    public async Task<MemberAdminDto> RestrictMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var result = await _transaction.ExecuteAsync(
            new[] { $"account:{actorId:D}", $"account:{memberId:D}" }, async ct =>
            {
                await RequireSuperAdminAsync(actorId, ct);
                var target = await RequireMemberTargetAsync(memberId, ct);
                if (!target.IsRestricted)
                    await _users.RestrictMemberAsync(memberId, ct);
                // Repeated requests also repair sessions left by an older partial operation.
                await _refreshSessions.RevokeAllForUserAsync(memberId, ct);
                return (Changed: !target.IsRestricted, User: await RequireMemberTargetAsync(memberId, ct));
            }, cancellationToken);
        if (!result.Changed) return _mapper.Map<MemberAdminDto>(result.User);

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

        _logger.LogInformation("RestrictMember {MemberId} by {ActorId}", memberId, actorId);
        return _mapper.Map<MemberAdminDto>(result.User);
    }

    public async Task<MemberAdminDto> UnrestrictMemberAsync(
        Guid actorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var result = await _transaction.ExecuteAsync(
            new[] { $"account:{actorId:D}", $"account:{memberId:D}" }, async ct =>
            {
                await RequireSuperAdminAsync(actorId, ct);
                var target = await RequireMemberTargetAsync(memberId, ct);
                if (target.IsRestricted)
                    await _users.UnrestrictMemberAsync(memberId, ct);
                return (Changed: target.IsRestricted, User: await RequireMemberTargetAsync(memberId, ct));
            }, cancellationToken);
        if (!result.Changed) return _mapper.Map<MemberAdminDto>(result.User);

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

        _logger.LogInformation("UnrestrictMember {MemberId} by {ActorId}", memberId, actorId);
        return _mapper.Map<MemberAdminDto>(result.User);
    }

    public async Task<AuthAccountDto> DeactivateAdminAsync(
        Guid actorId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        // Serialize administrator population checks and recheck the actor after acquiring locks.
        var result = await _transaction.ExecuteAsync(
            new[] { "admins:lifecycle", $"account:{actorId:D}", $"account:{targetId:D}" }, async ct =>
            {
                await RequireSuperAdminAsync(actorId, ct);
                if (actorId == targetId)
                {
                    throw new AuthException(
                        "cannot_deactivate_self",
                        "You cannot deactivate your own account.",
                        statusCode: 409);
                }

                var target = await RequireAdminTargetAsync(targetId, ct);
                if (!target.IsActive)
                {
                    await _refreshSessions.RevokeAllForUserAsync(targetId, ct);
                    return (Changed: false, User: target);
                }

                var activeAdmins = await _users.CountActiveAdminsAsync(ct);
                if (activeAdmins <= 1)
                {
                    throw new AuthException(
                        "last_admin",
                        "The last active admin cannot be deactivated.",
                        statusCode: 409);
                }

                if (target.IsSuperAdmin)
                {
                    var activeSupers = await _users.CountActiveSuperAdminsAsync(ct);
                    if (activeSupers <= 1)
                    {
                        throw new AuthException(
                            "last_super_admin",
                            "The last active super-admin cannot be deactivated.",
                            statusCode: 409);
                    }
                }

                await _users.DeactivateMemberAsync(targetId, ct);
                await _refreshSessions.RevokeAllForUserAsync(targetId, ct);
                return (Changed: true, User: await RequireAdminTargetAsync(targetId, ct));
            }, cancellationToken);
        if (!result.Changed) return _mapper.Map<AuthAccountDto>(result.User);
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

        _logger.LogInformation("DeactivateAdmin succeeded for user {UserId}", targetId);
        return _mapper.Map<AuthAccountDto>(result.User);
    }

    public async Task<AuthAccountDto> ActivateAdminAsync(
        Guid actorId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        // Serialize administrator population checks and recheck the actor after acquiring locks.
        var result = await _transaction.ExecuteAsync(
            new[] { "admins:lifecycle", $"account:{actorId:D}", $"account:{targetId:D}" }, async ct =>
            {
                await RequireSuperAdminAsync(actorId, ct);
                var target = await RequireAdminTargetAsync(targetId, ct);
                if (target.IsActive)
                {
                    return (Changed: false, User: target);
                }

                await _users.ActivateMemberAsync(targetId, ct);
                return (Changed: true, User: await RequireAdminTargetAsync(targetId, ct));
            }, cancellationToken);
        if (!result.Changed) return _mapper.Map<AuthAccountDto>(result.User);
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

        _logger.LogInformation("ActivateAdmin succeeded for user {UserId}", targetId);
        return _mapper.Map<AuthAccountDto>(result.User);
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
        if (!actor.IsSuperAdmin || !actor.IsActive || actor.IsDeleted || actor.IsRestricted)
        {
            throw new AuthException(
                "super_admin_required",
                "Super-admin rights are required.",
                statusCode: 403);
        }
    }

    public async Task<AuthAccountDto> GetAdminMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAdminMe {UserId}", userId);
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null
            || !string.Equals(user.AccountKind, nameof(AccountKind.Admin), StringComparison.Ordinal))
        {
            _logger.LogWarning("GetAdminMe user not found {UserId}", userId);
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        return _mapper.Map<AuthAccountDto>(user);
    }

}

