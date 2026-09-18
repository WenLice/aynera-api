using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.EarlyAccess.Services.Implementations;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Domain.EarlyAccess.Requests;
using Aynera.Domain.EarlyAccess.Exceptions;

namespace Aynera.Application.Tests;

public class EarlyAccessCityServiceTests
{
    [Fact]
    public async Task Create_ThenDeactivate_HidesFromOpenListLogic()
    {
        var cities = new CityRepo();
        var service = new EarlyAccessCityService(
            cities,
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<EarlyAccessCityService>.Instance);

        var created = await service.CreateAsync(
            new CreateEarlyAccessCityRequest("Hyderabad", Wave: 2, SortOrder: 10, IsActive: true),
            CancellationToken.None);

        Assert.Equal("Hyderabad", created.Name);

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateEarlyAccessCityRequest(IsActive: false),
            CancellationToken.None);

        Assert.False(updated.IsActive);
        Assert.Null(await cities.FindOpenByNameAsync("Hyderabad", CancellationToken.None));
    }

    [Fact]
    public async Task SoftDelete_ThenFindById_ReturnsNull()
    {
        var cities = new CityRepo();
        var service = new EarlyAccessCityService(
            cities,
            TestMapper.Instance,
            NoopAuditWriter.Instance,
            DiscardLogger<EarlyAccessCityService>.Instance);
        var created = await service.CreateAsync(
            new CreateEarlyAccessCityRequest("Chennai", 2, 11, true),
            CancellationToken.None);

        await service.SoftDeleteAsync(created.Id, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<EarlyAccessException>(() =>
            service.SoftDeleteAsync(created.Id, CancellationToken.None));
        Assert.Equal("early_access_city_not_found", ex.ErrorCode);
    }
}

file sealed class CityRepo : IEarlyAccessCityRepository
{
    private readonly List<EarlyAccessCityRecord> _all = [];

    public Task<IReadOnlyList<EarlyAccessCityRecord>> ListOpenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlyAccessCityRecord>>(
            _all.Where(x => x.IsActive).ToList());

    public Task<IReadOnlyList<EarlyAccessCityRecord>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlyAccessCityRecord>>(_all.ToList());

    public Task<EarlyAccessCityRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_all.FirstOrDefault(x => x.Id == id));

    public Task<EarlyAccessCityRecord?> FindOpenByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_all.FirstOrDefault(x =>
            x.IsActive && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken) =>
        Task.FromResult(_all.Any(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && x.Id != excludingId));

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
