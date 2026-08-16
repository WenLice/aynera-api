using Elaris.Application.Features.PublicForms.Repositories;
using Elaris.Application.Features.Suggestions.Models;
using Elaris.Application.Features.Suggestions.Repositories;
using Elaris.Application.Features.Suggestions.Services.Implementations;
using Elaris.Domain.Common;
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

    [Fact]
    public async Task List_ReturnsNewestFirstWithMessage()
    {
        var repo = new InMemorySuggestionRepo();
        var older = new SuggestionRecord(
            Guid.NewGuid(),
            "Ada",
            "first@example.com",
            "+919876543210",
            "First idea",
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var newer = older with
        {
            Id = Guid.NewGuid(),
            Email = "second@example.com",
            Phone = "+919876543211",
            Message = "Second idea",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await repo.AddAsync(older, CancellationToken.None);
        await repo.AddAsync(newer, CancellationToken.None);

        var service = new SuggestionService(
            repo,
            new SuggestionAllowAllRateLimiter(),
            TestMapper.Instance,
            DiscardLogger<SuggestionService>.Instance,
            Options.Create(new SuggestionOptions()));

        var page = await service.ListAsync(new PagedQuery(), CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Second idea", page.Items[0].Message);
        Assert.Equal("second@example.com", page.Items[0].Email);
        Assert.Equal("+919876543211", page.Items[0].Phone);
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

    public Task<(IReadOnlyList<SuggestionRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var ordered = All.OrderByDescending(x => x.CreatedAtUtc).ToList();
        return Task.FromResult<(IReadOnlyList<SuggestionRecord>, int)>(
            (ordered.Skip(skip).Take(take).ToList(), ordered.Count));
    }
}
