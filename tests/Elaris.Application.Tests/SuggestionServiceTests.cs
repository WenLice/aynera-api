using Elaris.Application.Features.PublicForms.Repositories;
using Elaris.Application.Features.Suggestions.Models;
using Elaris.Application.Features.Suggestions.Repositories;
using Elaris.Application.Features.Suggestions.Services.Implementations;
using Elaris.Domain.Suggestions.Records;
using Elaris.Domain.Suggestions.Requests;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Tests;

public class SuggestionServiceTests
{
    [Fact]
    public async Task Submit_CreatesRow()
    {
        var repo = new InMemorySuggestionRepo();
        var service = new SuggestionService(
            repo,
            new SuggestionAllowAllRateLimiter(),
            TestMapper.Instance,
            DiscardLogger<SuggestionService>.Instance,
            Options.Create(new SuggestionOptions()));

        var result = await service.SubmitAsync(
            new SubmitSuggestionRequest(
                "Ada",
                "ada@example.com",
                "Please add more café partners.",
                "+919876543210"),
            null,
            null,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Single(repo.All);
    }

    [Fact]
    public async Task Submit_AllowsDuplicateEmails()
    {
        var repo = new InMemorySuggestionRepo();
        var service = new SuggestionService(
            repo,
            new SuggestionAllowAllRateLimiter(),
            TestMapper.Instance,
            DiscardLogger<SuggestionService>.Instance,
            Options.Create(new SuggestionOptions()));

        await service.SubmitAsync(
            new SubmitSuggestionRequest("Ada", "ada@example.com", "First", "+919876543210"),
            null,
            null,
            CancellationToken.None);
        await service.SubmitAsync(
            new SubmitSuggestionRequest("Ada", "ada@example.com", "Second", "+919876543210"),
            null,
            null,
            CancellationToken.None);

        Assert.Equal(2, repo.All.Count);
    }
}

file sealed class SuggestionAllowAllRateLimiter : IPublicFormRateLimiter
{
    public Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireAsync(
        string bucket,
        string? clientIp,
        int maxPerHour,
        CancellationToken cancellationToken) =>
        Task.FromResult<(bool, int?)>((true, null));
}

file sealed class InMemorySuggestionRepo : ISuggestionRepository
{
    public List<SuggestionRecord> All { get; } = [];

    public Task<SuggestionRecord> AddAsync(
        SuggestionRecord suggestion,
        CancellationToken cancellationToken)
    {
        All.Add(suggestion);
        return Task.FromResult(suggestion);
    }
}
