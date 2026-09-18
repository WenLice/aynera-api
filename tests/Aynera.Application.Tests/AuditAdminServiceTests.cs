using Aynera.Application.Features.Audit.Repositories;
using Aynera.Application.Features.Audit.Services.Implementations;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Requests;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Common;

namespace Aynera.Application.Tests;

public class AuditAdminServiceTests
{
    [Fact]
    public async Task List_DefaultsToRestrictActions_NewestFirst()
    {
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var events = new FakeAuditEventRepository();
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(-2),
                AuditActions.MemberRestricted,
                AuditOutcomes.Success,
                "Member restricted by super-admin.",
                adminId,
                memberId,
                """{"isRestricted":{"old":false,"new":true}}""",
                "corr-1"));
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                AuditActions.MemberUnrestricted,
                AuditOutcomes.Success,
                "Member unrestricted by super-admin.",
                adminId,
                memberId,
                null,
                "corr-2"));
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                AuditActions.AdminCreated,
                AuditOutcomes.Success,
                "Should not appear by default.",
                adminId,
                null,
                null,
                null));

        var users = new FakeAuditUsers();
        users.AddAdmin(adminId);
        var service = new AuditAdminService(events, users, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditAdminService>.Instance);

        var page = await service.ListAsync(adminId, new AuditAdminListQuery(), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(AuditActions.MemberUnrestricted, page.Items[0].Action);
        Assert.Equal(AuditActions.MemberRestricted, page.Items[1].Action);
    }

    [Fact]
    public async Task List_FiltersByMemberAndAction()
    {
        var adminId = Guid.NewGuid();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        var events = new FakeAuditEventRepository();
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                AuditActions.MemberRestricted,
                AuditOutcomes.Success,
                "A",
                adminId,
                memberA,
                null,
                null));
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                AuditActions.MemberRestricted,
                AuditOutcomes.Success,
                "B",
                adminId,
                memberB,
                null,
                null));

        var users = new FakeAuditUsers();
        users.AddAdmin(adminId);
        var service = new AuditAdminService(events, users, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditAdminService>.Instance);

        var page = await service.ListAsync(
            adminId,
            new AuditAdminListQuery { Action = AuditActions.MemberRestricted, MemberId = memberB },
            CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(memberB, page.Items[0].SubjectUserId);
    }

    [Fact]
    public async Task List_ForMember_ReturnsAllActionsForSubject()
    {
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var events = new FakeAuditEventRepository();
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                AuditActions.PhotosUploaded,
                AuditOutcomes.Success,
                "Photos uploaded.",
                memberId,
                memberId,
                null,
                null));
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                AuditActions.MemberRestricted,
                AuditOutcomes.Success,
                "Restricted.",
                adminId,
                memberId,
                null,
                null));
        events.Seed(
            new AuditEventAdminRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(-2),
                AuditActions.MemberRestricted,
                AuditOutcomes.Success,
                "Other member.",
                adminId,
                Guid.NewGuid(),
                null,
                null));

        var users = new FakeAuditUsers();
        users.AddAdmin(adminId);
        var service = new AuditAdminService(events, users, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditAdminService>.Instance);

        var page = await service.ListAsync(
            adminId,
            new AuditAdminListQuery { MemberId = memberId },
            CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, item => item.Action == AuditActions.PhotosUploaded);
        Assert.Contains(page.Items, item => item.Action == AuditActions.MemberRestricted);
    }

    [Fact]
    public async Task List_RejectsMemberActor()
    {
        var memberId = Guid.NewGuid();
        var users = new FakeAuditUsers();
        users.AddMember(memberId);
        var service = new AuditAdminService(
            new FakeAuditEventRepository(),
            users,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditAdminService>.Instance);

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            service.ListAsync(memberId, new AuditAdminListQuery(), CancellationToken.None));

        Assert.Equal("user_not_found", ex.ErrorCode);
    }
}

file sealed class FakeAuditEventRepository : IAuditEventRepository
{
    private readonly List<AuditEventAdminRecord> _rows = [];

    public void Seed(AuditEventAdminRecord row) => _rows.Add(row);

    public Task AddWithLogAsync(
        AuditLogRecord log,
        string action,
        string outcome,
        Guid? subjectUserId,
        string? subjectType,
        string? subjectId,
        string? audience,
        IReadOnlyDictionary<string, AuditFieldChange>? changes,
        object? metadata,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<(IReadOnlyList<AuditEventAdminRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        IReadOnlyList<string>? actions,
        Guid? subjectUserId,
        string? subjectType,
        string? subjectId,
        CancellationToken cancellationToken)
    {
        var filtered = _rows
            .Where(row => actions is null || actions.Count == 0 || actions.Contains(row.Action))
            .Where(row => subjectUserId is null || row.SubjectUserId == subjectUserId)
            .OrderByDescending(row => row.OccurredAtUtc)
            .ThenByDescending(row => row.Id)
            .ToList();
        return Task.FromResult<(IReadOnlyList<AuditEventAdminRecord>, int)>(
            (filtered.Skip(skip).Take(take).ToList(), filtered.Count));
    }
}

file sealed class FakeAuditUsers : IUserRepository
{
    private readonly Dictionary<Guid, UserRecord> _byId = new();

    public void AddAdmin(Guid id) =>
        _byId[id] = new UserRecord(
            id, "+919999999999", true, "ops@example.com", true, "Admin", true, false, true, false, ["admin"]);

    public void AddMember(Guid id) =>
        _byId[id] = new UserRecord(
            id, "+918888888888", true, "m@example.com", true, "Member", true, false, false, false, ["member"]);

    public Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.TryGetValue(userId, out var user) ? user : null);

    public Task<UserRecord?> FindByPhoneAsync(string phoneE164, CancellationToken cancellationToken) =>
        Task.FromResult<UserRecord?>(null);

    public Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult<UserRecord?>(null);

    public Task<UserRecord> CreateMemberAsync(
        string phoneE164,
        string? email,
        string? password,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task MarkEmailConfirmedAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetConfirmedEmailAsync(Guid userId, string email, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);
    public Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task TouchLastLoginAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SoftDeleteMemberAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RestrictMemberAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task UnrestrictMemberAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task SetPasswordAsync(Guid userId, string password, string? currentPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<bool> AnyAdminExistsAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<(IReadOnlyList<UserRecord> Items, int TotalCount)> ListAdminsPageAsync(
        int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<UserRecord>, int)>(([], 0));
    public Task<(IReadOnlyList<MemberAdminRecord> Items, int TotalCount)> ListMembersPageAsync(
        int skip, int take, string? search, bool? isActive, bool? isRestricted, CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<MemberAdminRecord>, int)>(([], 0));
    public Task<MemberAdminRecord?> FindMemberAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<MemberAdminRecord?>(null);
    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    public Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    public Task<UserRecord> CreateAdminAsync(
        string email, string? phoneE164, string password, CancellationToken cancellationToken, bool isSuperAdmin = false) =>
        throw new NotSupportedException();
}
