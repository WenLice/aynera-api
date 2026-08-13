using Elaris.Application.Features.Auth.Repositories;
using Elaris.Application.Features.Feedback.Models;
using Elaris.Application.Features.Feedback.Repositories;
using Elaris.Application.Features.Feedback.Services.Implementations;
using Elaris.Application.Features.PublicForms.Repositories;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.Feedback.Records;
using Elaris.Domain.Feedback.Requests;
using Elaris.Domain.Feedback.Exceptions;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Tests;

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
            ["member"]);
        var service = CreateService(repo, users: new FeedbackUserLookup(existing));

        var result = await service.SubmitAsync(
            new SubmitFeedbackRequest(
                "Ada",
                "member@example.com",
                "Account help needed."),
            null,
            null,
            CancellationToken.None);

        Assert.True(result.IsExistingUser);
        Assert.True(repo.All[0].IsExistingUser);
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
                    "This message is definitely too long"),
                null,
                null,
                CancellationToken.None));

        Assert.Equal("feedback_message_too_long", ex.ErrorCode);
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

    public Task<UserRecord> CreateMemberAsync(string phoneE164, string? email, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken) =>
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
}
