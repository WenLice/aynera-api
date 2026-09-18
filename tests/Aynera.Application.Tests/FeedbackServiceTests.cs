using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Feedback.Models;
using Aynera.Application.Features.Feedback.Repositories;
using Aynera.Application.Features.Feedback.Services.Implementations;
using Aynera.Application.Features.PublicForms.Repositories;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Common;
using Aynera.Domain.Feedback.Records;
using Aynera.Domain.Feedback.Requests;
using Aynera.Domain.Feedback.Exceptions;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Tests;

public class FeedbackServiceTests
{
    [Fact]
    public async Task Submit_CreatesRow_MarksNonExistingUser()
    {
        var repo = new InMemoryFeedbackRepo();
        var service = CreateService(repo, users: new FeedbackUserLookup(null));

        var result = await service.SubmitAsync(
            new SubmitFeedbackRequest(
                "Ada",
                "ada@example.com",
                "I need help with a safety concern.",
                "+919876543210"),
            "127.0.0.1",
            "test-agent",
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.False(result.IsExistingUser);
        Assert.Single(repo.All);
        Assert.False(repo.All[0].IsExistingUser);
    }

    [Fact]
    public async Task Submit_ExistingEmail_SetsIsExistingUser()
    {
        var repo = new InMemoryFeedbackRepo();
        var existing = new UserRecord(
            Guid.NewGuid(),
            "+919988776655",
            true,
            "member@example.com",
            true,
            "Member",
            true,
            false,
            false, false,
            ["member"]);
        var service = CreateService(repo, users: new FeedbackUserLookup(existing));

        var result = await service.SubmitAsync(
            new SubmitFeedbackRequest(
                "Ada",
                "member@example.com",
                "Account help needed.",
                "+919876543210"),
            null,
            null,
            CancellationToken.None);

        Assert.True(result.IsExistingUser);
        Assert.True(repo.All[0].IsExistingUser);
        Assert.Equal(existing.Id, repo.All[0].MemberId);
    }

    [Fact]
    public async Task Submit_AdminEmail_DoesNotLinkAsMember()
    {
        var repo = new InMemoryFeedbackRepo();
        var admin = new UserRecord(
            Guid.NewGuid(),
            "+919988776655",
            true,
            "ops@example.com",
            true,
            "Admin",
            true,
            false,
            false,
            false,
            ["admin"]);
        var service = CreateService(repo, users: new FeedbackUserLookup(admin));

        var result = await service.SubmitAsync(
            new SubmitFeedbackRequest(
                "Ops",
                "ops@example.com",
                "Not a member grievance.",
                "+919876543210"),
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsExistingUser);
        Assert.Null(repo.All[0].MemberId);
    }

    [Fact]
    public async Task Submit_TooLong_Throws()
    {
        var service = CreateService(new InMemoryFeedbackRepo(), maxLength: 10);

        var ex = await Assert.ThrowsAsync<FeedbackException>(() =>
            service.SubmitAsync(
                new SubmitFeedbackRequest(
                    "Ada",
                    "ada@example.com",
                    "This message is definitely too long",
                    "+919876543210"),
                null,
                null,
                CancellationToken.None));

        Assert.Equal("feedback_message_too_long", ex.ErrorCode);
    }

    [Fact]
    public async Task List_ReturnsNewestFirstWithMessage()
    {
        var repo = new InMemoryFeedbackRepo();
        var older = new FeedbackSubmissionRecord(
            Guid.NewGuid(),
            "Ada",
            "first@example.com",
            "+919876543210",
            "Older grievance",
            null,
            null,
            false,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var memberId = Guid.NewGuid();
        var newer = older with
        {
            Id = Guid.NewGuid(),
            Email = "second@example.com",
            Message = "Newer grievance",
            IsExistingUser = true,
            MemberId = memberId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await repo.AddAsync(older, CancellationToken.None);
        await repo.AddAsync(newer, CancellationToken.None);

        var service = CreateService(repo);
        var page = await service.ListAsync(new PagedQuery(), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Newer grievance", page.Items[0].Message);
        Assert.Equal("second@example.com", page.Items[0].Email);
        Assert.True(page.Items[0].IsExistingUser);
        Assert.Equal(memberId, page.Items[0].MemberId);
    }

    private static FeedbackService CreateService(
        IFeedbackSubmissionRepository repo,
        int maxLength = 2000,
        IUserRepository? users = null) =>
        new(
            repo,
            users ?? new FeedbackUserLookup(null),
            new FeedbackAllowAllRateLimiter(),
            TestMapper.Instance,
            DiscardLogger<FeedbackService>.Instance,
            Options.Create(new FeedbackOptions
            {
                MaxMessageLength = maxLength,
                MaxRequestsPerIpPerHour = 100
            }));
}

file sealed class FeedbackAllowAllRateLimiter : IPublicFormRateLimiter
{
    public Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireAsync(
        string bucket,
        string? clientIp,
        int maxPerHour,
        CancellationToken cancellationToken) =>
        Task.FromResult<(bool, int?)>((true, null));
}

file sealed class FeedbackUserLookup(UserRecord? match) : IUserRepository
{
    public Task<UserRecord?> FindByPhoneAsync(string phoneE164, CancellationToken cancellationToken) =>
        Task.FromResult<UserRecord?>(null);

    public Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(
            match is not null
            && string.Equals(match.Email, email, StringComparison.OrdinalIgnoreCase)
                ? match
                : null);

    public Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<UserRecord?>(null);

    public Task<UserRecord> CreateMemberAsync(
        string phoneE164,
        string? email,
        string? password,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task MarkEmailConfirmedAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SetConfirmedEmailAsync(Guid userId, string email, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);

    public Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task TouchLastLoginAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SoftDeleteMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RestrictMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UnrestrictMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task SetPasswordAsync(
        Guid userId,
        string password,
        string? currentPassword,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PasswordCheckResult> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> AnyAdminExistsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<(IReadOnlyList<UserRecord> Items, int TotalCount)> ListAdminsPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<UserRecord>, int)>(([], 0));

    public Task<(IReadOnlyList<MemberAdminRecord> Items, int TotalCount)> ListMembersPageAsync(
        int skip,
        int take,
        string? search,
        bool? isActive,
        bool? isRestricted,
        CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<MemberAdminRecord>, int)>(([], 0));

    public Task<MemberAdminRecord?> FindMemberAdminAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<MemberAdminRecord?>(null);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<UserRecord> CreateAdminAsync(
        string email,
        string? phoneE164,
        string password,
        CancellationToken cancellationToken,
        bool isSuperAdmin = false) =>
        throw new NotSupportedException();
}

file sealed class InMemoryFeedbackRepo : IFeedbackSubmissionRepository
{
    public List<FeedbackSubmissionRecord> All { get; } = [];

    public Task<FeedbackSubmissionRecord> AddAsync(
        FeedbackSubmissionRecord feedback,
        CancellationToken cancellationToken)
    {
        All.Add(feedback);
        return Task.FromResult(feedback);
    }

    public Task<(IReadOnlyList<FeedbackSubmissionRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var ordered = All.OrderByDescending(x => x.CreatedAtUtc).ToList();
        return Task.FromResult<(IReadOnlyList<FeedbackSubmissionRecord>, int)>(
            (ordered.Skip(skip).Take(take).ToList(), ordered.Count));
    }
}
