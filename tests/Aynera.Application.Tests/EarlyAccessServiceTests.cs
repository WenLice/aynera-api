using Aynera.Application.Features.EarlyAccess.Models;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.EarlyAccess.Services.Implementations;
using Aynera.Application.Features.PublicForms.Repositories;
using Aynera.Domain.Common;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Domain.EarlyAccess.Requests;
using Aynera.Domain.EarlyAccess.Exceptions;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Tests;

public class EarlyAccessServiceTests
{
    [Fact]
    public async Task Register_CreatesThenUpsertsByEmail()
    {
        var signups = new InMemoryEarlyAccessRepo();
        var cities = new InMemoryCityRepo();
        cities.SeedOpen("Delhi", "Mumbai");
        var service = CreateService(signups, cities);

        var first = await service.RegisterAsync(
            new JoinEarlyAccessRequest(
                "Ada Lovelace",
                "ada@example.com",
                "Delhi",
                "Aynera",
                true,
                true,
                "+919876543210"),
            "127.0.0.1",
            "test-agent",
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.Equal("Delhi", first.City);

        var second = await service.RegisterAsync(
            new JoinEarlyAccessRequest(
                "Ada L",
                "ADA@example.com",
                "Mumbai",
                "Aynera Professionals",
                true,
                true,
                "+919876543210"),
            "127.0.0.1",
            "test-agent",
            CancellationToken.None);

        Assert.False(second.Created);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Mumbai", second.City);
        Assert.Equal("Aynera Professionals", second.Interest);
        Assert.Single(signups.All);
    }

    [Fact]
    public async Task Register_CityNotOpen_Throws()
    {
        var cities = new InMemoryCityRepo();
        cities.SeedOpen("Delhi");
        var service = CreateService(new InMemoryEarlyAccessRepo(), cities);

        var ex = await Assert.ThrowsAsync<EarlyAccessException>(() =>
            service.RegisterAsync(
                new JoinEarlyAccessRequest(
                    "Ada Lovelace",
                    "ada@example.com",
                    "Pune",
                    "Aynera",
                    true,
                    true,
                    "+919876543210"),
                null,
                null,
                CancellationToken.None));

        Assert.Equal("early_access_city_not_open", ex.ErrorCode);
    }

    [Fact]
    public async Task ListOpenCities_ReturnsActiveOnly()
    {
        var cities = new InMemoryCityRepo();
        cities.SeedOpen("Delhi", "Mumbai");
        cities.AddInactive("Pune");
        var service = CreateService(new InMemoryEarlyAccessRepo(), cities);

        var open = await service.ListOpenCitiesAsync(CancellationToken.None);

        Assert.Equal(2, open.Count);
        Assert.All(open, c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task ListSignups_ReturnsNewestFirstWithContactFields()
    {
        var signups = new InMemoryEarlyAccessRepo();
        var older = new EarlyAccessSignupRecord(
            Guid.NewGuid(),
            "Ada",
            "ada@example.com",
            "+919876543210",
            "Delhi",
            "Aynera",
            true,
            true,
            null,
            null,
            true,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            null);
        var newer = older with
        {
            Id = Guid.NewGuid(),
            Email = "later@example.com",
            FullName = "Later",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await signups.AddAsync(older, CancellationToken.None);
        await signups.AddAsync(newer, CancellationToken.None);

        var service = CreateService(signups, new InMemoryCityRepo());
        var page = await service.ListSignupsAsync(new PagedQuery(), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(newer.Id, page.Items[0].Id);
        Assert.Equal("Later", page.Items[0].FullName);
        Assert.Equal("later@example.com", page.Items[0].Email);
        Assert.Equal("+919876543210", page.Items[0].Phone);
        Assert.Equal("Aynera", page.Items[0].Interest);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task ListSignups_SecondPage_ReturnsOlderRow()
    {
        var signups = new InMemoryEarlyAccessRepo();
        var older = new EarlyAccessSignupRecord(
            Guid.NewGuid(),
            "Ada",
            "ada@example.com",
            "+919876543210",
            "Delhi",
            "Aynera",
            true,
            true,
            null,
            null,
            true,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            null);
        var newer = older with
        {
            Id = Guid.NewGuid(),
            Email = "later@example.com",
            FullName = "Later",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await signups.AddAsync(older, CancellationToken.None);
        await signups.AddAsync(newer, CancellationToken.None);

        var service = CreateService(signups, new InMemoryCityRepo());
        var page = await service.ListSignupsAsync(
            new PagedQuery { Page = 2, PageSize = 1 },
            CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Items);
        Assert.Equal(older.Id, page.Items[0].Id);
        Assert.True(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    private static EarlyAccessService CreateService(
        IEarlyAccessSignupRepository signups,
        IEarlyAccessCityRepository cities) =>
        new(
            signups,
            cities,
            new AllowAllRateLimiter(),
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<EarlyAccessService>.Instance,
            Options.Create(new EarlyAccessOptions { MaxRequestsPerIpPerHour = 100 }));
}

file sealed class AllowAllRateLimiter : IPublicFormRateLimiter
{
    public Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireAsync(
        string bucket,
        string? clientIp,
        int maxPerHour,
        CancellationToken cancellationToken) =>
        Task.FromResult<(bool, int?)>((true, null));
}

file sealed class InMemoryEarlyAccessRepo : IEarlyAccessSignupRepository
{
    public List<EarlyAccessSignupRecord> All { get; } = [];

    public Task<EarlyAccessSignupRecord?> FindByEmailAsync(
        string emailNormalized,
        CancellationToken cancellationToken) =>
        Task.FromResult(All.FirstOrDefault(x => x.Email == emailNormalized));

    public Task<(IReadOnlyList<EarlyAccessSignupRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var ordered = All.OrderByDescending(x => x.CreatedAtUtc).ToList();
        return Task.FromResult<(IReadOnlyList<EarlyAccessSignupRecord>, int)>(
            (ordered.Skip(skip).Take(take).ToList(), ordered.Count));
    }

    public Task<EarlyAccessSignupRecord> AddAsync(
        EarlyAccessSignupRecord signup,
        CancellationToken cancellationToken)
    {
        All.Add(signup);
        return Task.FromResult(signup);
    }

    public Task<EarlyAccessSignupRecord> UpdateAsync(
        EarlyAccessSignupRecord signup,
        CancellationToken cancellationToken)
    {
        var idx = All.FindIndex(x => x.Id == signup.Id);
        All[idx] = signup;
        return Task.FromResult(signup);
    }
}

file sealed class InMemoryCityRepo : IEarlyAccessCityRepository
{
    private readonly List<EarlyAccessCityRecord> _all = [];

    public void SeedOpen(params string[] names)
    {
        var order = 1;
        foreach (var name in names)
        {
            _all.Add(new EarlyAccessCityRecord(
                Guid.NewGuid(),
                name,
                Wave: 1,
                SortOrder: order++,
                IsActive: true,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: null));
        }
    }

    public void AddInactive(string name) =>
        _all.Add(new EarlyAccessCityRecord(
            Guid.NewGuid(),
            name,
            2,
            99,
            false,
            DateTimeOffset.UtcNow,
            null));

    public Task<IReadOnlyList<EarlyAccessCityRecord>> ListOpenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlyAccessCityRecord>>(
            _all.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList());

    public Task<IReadOnlyList<EarlyAccessCityRecord>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlyAccessCityRecord>>(_all.OrderBy(x => x.SortOrder).ToList());

    public Task<EarlyAccessCityRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_all.FirstOrDefault(x => x.Id == id));

    public Task<EarlyAccessCityRecord?> FindOpenByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_all.FirstOrDefault(x =>
            x.IsActive && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken) =>
        Task.FromResult(_all.Any(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)
            && x.Id != excludingId));

    public Task<EarlyAccessCityRecord> AddAsync(EarlyAccessCityRecord city, CancellationToken cancellationToken)
    {
        _all.Add(city);
        return Task.FromResult(city);
    }

    public Task<EarlyAccessCityRecord> UpdateAsync(EarlyAccessCityRecord city, CancellationToken cancellationToken)
    {
        var idx = _all.FindIndex(x => x.Id == city.Id);
        _all[idx] = city;
        return Task.FromResult(city);
    }

    public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _all.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }
}
