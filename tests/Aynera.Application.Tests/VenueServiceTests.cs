using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.Venues.Repositories;
using Aynera.Application.Features.Venues.Services.Implementations;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Domain.Venues.Exceptions;
using Aynera.Domain.Venues.Records;
using Aynera.Domain.Venues.Requests;

namespace Aynera.Application.Tests;

public class VenueServiceTests
{
    private static (VenueService Service, Guid CityId) Build()
    {
        var cityId = Guid.NewGuid();
        var cities = new VenueCityRepo(new EarlyAccessCityRecord(
            cityId, "Delhi", 1, 0, true, DateTimeOffset.UtcNow, null));
        var service = new VenueService(
            new VenueRepo(),
            new VenueNotificationRepo(),
            cities,
            NoopAuditWriter.Instance,
            DiscardLogger<VenueService>.Instance);
        return (service, cityId);
    }

    private static CreateVenueRequest NewRequest(Guid cityId, string name = "Blue Tokai") =>
        new(
            Name: name,
            Type: "Cafe",
            CityId: cityId,
            Area: "Hauz Khas",
            Address: "12 Aurobindo Marg",
            ContactName: "Priya",
            ContactEmail: "priya@bluetokai.example",
            ContactPhoneE164: "+919876543210",
            PhotoUrls: ["https://img.example/a.jpg", "  "],
            Capacity: 40);

    [Fact]
    public async Task Create_ResolvesCityName_AndDropsBlankPhotoUrls()
    {
        var (service, cityId) = Build();

        var created = await service.CreateAsync(NewRequest(cityId), CancellationToken.None);

        Assert.Equal("Blue Tokai", created.Name);
        Assert.Equal("Cafe", created.Type);
        Assert.Equal("Delhi", created.CityName);
        Assert.Single(created.PhotoUrls);
        Assert.Equal("https://img.example/a.jpg", created.PhotoUrls[0]);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Create_UnknownCity_Throws()
    {
        var (service, _) = Build();

        var ex = await Assert.ThrowsAsync<VenueException>(() =>
            service.CreateAsync(NewRequest(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("venue_city_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task Update_PartialFields_PreservesOthers()
    {
        var (service, cityId) = Build();
        var created = await service.CreateAsync(NewRequest(cityId), CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateVenueRequest(Type: "EventPlace", IsActive: false),
            CancellationToken.None);

        Assert.Equal("EventPlace", updated.Type);
        Assert.False(updated.IsActive);
        Assert.Equal("Blue Tokai", updated.Name); // preserved
        Assert.Equal("Hauz Khas", updated.Area);  // preserved
        Assert.NotNull(updated.UpdatedAtUtc);
    }

    [Fact]
    public async Task Update_MissingVenue_Throws()
    {
        var (service, _) = Build();

        var ex = await Assert.ThrowsAsync<VenueException>(() =>
            service.UpdateAsync(Guid.NewGuid(), new UpdateVenueRequest(IsActive: false), CancellationToken.None));

        Assert.Equal("venue_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task SoftDelete_ThenSoftDeleteAgain_Throws()
    {
        var (service, cityId) = Build();
        var created = await service.CreateAsync(NewRequest(cityId), CancellationToken.None);

        await service.SoftDeleteAsync(created.Id, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<VenueException>(() =>
            service.SoftDeleteAsync(created.Id, CancellationToken.None));
        Assert.Equal("venue_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task List_ReturnsCreatedVenues_WithCityNames()
    {
        var (service, cityId) = Build();
        await service.CreateAsync(NewRequest(cityId, "Cafe A"), CancellationToken.None);
        await service.CreateAsync(NewRequest(cityId, "Cafe B"), CancellationToken.None);

        var all = await service.ListAsync(CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.All(all, v => Assert.Equal("Delhi", v.CityName));
    }

    [Fact]
    public async Task QueueHeadsUp_CreatesEmailAndSmsRows_ThenListable()
    {
        var (service, cityId) = Build();
        var venue = await service.CreateAsync(NewRequest(cityId), CancellationToken.None);
        var visitOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);

        var queued = await service.QueueHeadsUpAsync(
            venue.Id,
            new SendVenueHeadsUpRequest(visitOn, PartySize: 4, Note: "Window seats please"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(2, queued.Count);
        Assert.Contains(queued, n => n.Channel == "Email");
        Assert.Contains(queued, n => n.Channel == "Sms");
        Assert.All(queued, n => Assert.Equal("Pending", n.Status));
        Assert.All(queued, n => Assert.Equal(4, n.PartySize));

        var history = await service.ListNotificationsAsync(venue.Id, CancellationToken.None);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task QueueHeadsUp_MissingVenue_Throws()
    {
        var (service, _) = Build();

        var ex = await Assert.ThrowsAsync<VenueException>(() =>
            service.QueueHeadsUpAsync(
                Guid.NewGuid(),
                new SendVenueHeadsUpRequest(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), 2, null),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("venue_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task QueueHeadsUp_InactiveVenue_Throws()
    {
        var (service, cityId) = Build();
        var venue = await service.CreateAsync(NewRequest(cityId), CancellationToken.None);
        await service.UpdateAsync(venue.Id, new UpdateVenueRequest(IsActive: false), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<VenueException>(() =>
            service.QueueHeadsUpAsync(
                venue.Id,
                new SendVenueHeadsUpRequest(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), 2, null),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("venue_inactive", ex.ErrorCode);
    }
}

file sealed class VenueRepo : IVenueRepository
{
    private readonly List<VenueRecord> _all = [];

    public Task<IReadOnlyList<VenueRecord>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VenueRecord>>(_all.ToList());

    public Task<VenueRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_all.FirstOrDefault(x => x.Id == id));

    public Task<VenueRecord> AddAsync(VenueRecord venue, CancellationToken cancellationToken)
    {
        _all.Add(venue);
        return Task.FromResult(venue);
    }

    public Task<VenueRecord> UpdateAsync(VenueRecord venue, CancellationToken cancellationToken)
    {
        var idx = _all.FindIndex(x => x.Id == venue.Id);
        _all[idx] = venue;
        return Task.FromResult(venue);
    }

    public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _all.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }
}

file sealed class VenueNotificationRepo : IVenueNotificationRepository
{
    private readonly List<VenueNotificationRecord> _all = [];

    public Task AddRangeAsync(IEnumerable<VenueNotificationRecord> notifications, CancellationToken cancellationToken)
    {
        _all.AddRange(notifications);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VenueNotificationRecord>> ListByVenueAsync(Guid venueId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VenueNotificationRecord>>(
            _all.Where(x => x.VenueId == venueId).ToList());
}

file sealed class VenueCityRepo : IEarlyAccessCityRepository
{
    private readonly List<EarlyAccessCityRecord> _all;

    public VenueCityRepo(params EarlyAccessCityRecord[] cities) => _all = cities.ToList();

    public Task<IReadOnlyList<EarlyAccessCityRecord>> ListOpenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlyAccessCityRecord>>(_all.Where(x => x.IsActive).ToList());

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
