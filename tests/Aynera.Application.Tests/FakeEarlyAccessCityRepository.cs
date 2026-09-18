using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Domain.EarlyAccess.Records;

namespace Aynera.Application.Tests;

/// <summary>In-memory city catalog seeded with the launch cities, for services that resolve a city by name.</summary>
public sealed class FakeEarlyAccessCityRepository : IEarlyAccessCityRepository
{
    private readonly List<EarlyAccessCityRecord> _all = [];

    /// <summary>
    /// Default set mirrors the real seed: Bangalore is the only Wave 1 city, Delhi and Mumbai
    /// are Wave 2 but still active. Wave is not an access gate, so every one of them resolves.
    /// </summary>
    public FakeEarlyAccessCityRepository(params string[] openCities)
    {
        if (openCities.Length == 0)
        {
            _all.Add(new EarlyAccessCityRecord(Guid.NewGuid(), "Bangalore", 1, 1, true, DateTimeOffset.UtcNow, null));
            _all.Add(new EarlyAccessCityRecord(Guid.NewGuid(), "Delhi", 2, 2, true, DateTimeOffset.UtcNow, null));
            _all.Add(new EarlyAccessCityRecord(Guid.NewGuid(), "Mumbai", 2, 3, true, DateTimeOffset.UtcNow, null));
            return;
        }

        var order = 1;
        foreach (var name in openCities)
        {
            _all.Add(new EarlyAccessCityRecord(Guid.NewGuid(), name, 1, order++, true, DateTimeOffset.UtcNow, null));
        }
    }

    public EarlyAccessCityRecord this[string name] =>
        _all.Single(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public void AddInactive(string name) =>
        _all.Add(new EarlyAccessCityRecord(Guid.NewGuid(), name, 2, 99, false, DateTimeOffset.UtcNow, null));

    public Task<IReadOnlyList<EarlyAccessCityRecord>> ListOpenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlyAccessCityRecord>>(_all.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList());

    public Task<IReadOnlyList<EarlyAccessCityRecord>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlyAccessCityRecord>>(_all.OrderBy(x => x.SortOrder).ToList());

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
        _all[_all.FindIndex(x => x.Id == city.Id)] = city;
        return Task.FromResult(city);
    }

    public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _all.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }
}
