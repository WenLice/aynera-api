using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Venues.Requests;
using Aynera.Domain.Venues.Responses;
using Aynera.Infrastructure.Services;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class VenueEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Admin_CanCreateListUpdateAndDeleteVenue()
    {
        var token = await CreateAdminTokenAsync();
        var cityId = await FirstCityIdAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create
        var create = await client.PostAsJsonAsync(
            "/venues/Create",
            new CreateVenueRequest(
                Name: $"Blue Tokai {Guid.NewGuid():N}",
                Type: "Cafe",
                CityId: cityId,
                Area: "Hauz Khas",
                Address: "12 Aurobindo Marg, New Delhi",
                ContactName: "Priya",
                ContactEmail: "priya@bluetokai.example",
                ContactPhoneE164: "+919876543210",
                PhotoUrls: ["https://img.example/a.jpg"],
                Capacity: 40));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<VenueDto>>(JsonOptions);
        Assert.True(created!.Success);
        var venue = created.Data!;
        Assert.Equal("Cafe", venue.Type);
        Assert.NotNull(venue.CityName);
        Assert.True(venue.IsActive);

        // List
        var list = await client.GetAsync("/venues/GetAll");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadFromJsonAsync<ApiResponse<List<VenueDto>>>(JsonOptions);
        Assert.Contains(listBody!.Data!, v => v.Id == venue.Id);

        // Update
        var update = await client.PatchAsJsonAsync(
            $"/venues/{venue.Id}",
            new UpdateVenueRequest(Type: "EventPlace", IsActive: false));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ApiResponse<VenueDto>>(JsonOptions);
        Assert.Equal("EventPlace", updated!.Data!.Type);
        Assert.False(updated.Data.IsActive);

        // Delete
        var delete = await client.DeleteAsync($"/venues/{venue.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var afterDelete = await client.GetAsync("/venues/GetAll");
        var afterBody = await afterDelete.Content.ReadFromJsonAsync<ApiResponse<List<VenueDto>>>(JsonOptions);
        Assert.DoesNotContain(afterBody!.Data!, v => v.Id == venue.Id);
    }

    [Fact]
    public async Task AnonymousCaller_CannotListVenues()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/venues/GetAll");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_QueuesHeadsUp_AndDispatcherDeliversEmailAndSms()
    {
        var token = await CreateAdminTokenAsync();
        var cityId = await FirstCityIdAsync();
        var contactEmail = $"venue-{Guid.NewGuid():N}@bluetokai.example";
        var contactPhone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = await client.PostAsJsonAsync(
            "/venues/Create",
            new CreateVenueRequest(
                Name: $"Heads-up Café {Guid.NewGuid():N}",
                Type: "Cafe",
                CityId: cityId,
                Area: "Hauz Khas",
                Address: "12 Aurobindo Marg, New Delhi",
                ContactName: "Priya",
                ContactEmail: contactEmail,
                ContactPhoneE164: contactPhone,
                PhotoUrls: null,
                Capacity: 40));
        var venue = (await create.Content.ReadFromJsonAsync<ApiResponse<VenueDto>>(JsonOptions))!.Data!;

        var visitOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var queue = await client.PostAsJsonAsync(
            $"/venues/{venue.Id}/notifications/Create",
            new SendVenueHeadsUpRequest(visitOn, PartySize: 5, Note: "Prefer window seats"));
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        var queued = await queue.Content.ReadFromJsonAsync<ApiResponse<List<VenueNotificationDto>>>(JsonOptions);
        Assert.Equal(2, queued!.Data!.Count);
        Assert.All(queued.Data, n => Assert.Equal("Pending", n.Status));

        // Drive the outbox dispatcher directly (rather than waiting on the background timer).
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<VenueNotificationDispatcher>()
                .DispatchPendingAsync(CancellationToken.None);
        }

        var emailNotice = factory.Email.GetVenueHeadsUp(contactEmail);
        var smsNotice = factory.Sms.GetVenueHeadsUp(contactPhone);
        Assert.NotNull(emailNotice);
        Assert.Equal(5, emailNotice!.PartySize);
        Assert.Equal(visitOn, emailNotice.VisitOn);
        Assert.NotNull(smsNotice);

        var history = await client.GetAsync($"/venues/{venue.Id}/notifications/GetAll");
        var historyBody = await history.Content.ReadFromJsonAsync<ApiResponse<List<VenueNotificationDto>>>(JsonOptions);
        Assert.Equal(2, historyBody!.Data!.Count);
        Assert.All(historyBody.Data, n => Assert.Equal("Sent", n.Status));
    }

    private async Task<Guid> FirstCityIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var cities = await scope.ServiceProvider
            .GetRequiredService<IEarlyAccessCityRepository>()
            .ListAllAsync(CancellationToken.None);
        return cities[0].Id;
    }

    private async Task<string> CreateAdminTokenAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            AccountKind = AccountKind.Admin
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AuthRoles.Admin)).Succeeded);
        var account = (await sp.GetRequiredService<IUserRepository>()
            .FindByIdAsync(user.Id, CancellationToken.None))!;
        var token = sp.GetRequiredService<ITokenService>()
            .CreateAccessToken(account, AuthAudiences.Admin, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow);
        return token.AccessToken;
    }
}
