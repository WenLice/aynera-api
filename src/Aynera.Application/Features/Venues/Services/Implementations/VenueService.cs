using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.Venues.Repositories;
using Aynera.Application.Features.Venues.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Venues.Enums;
using Aynera.Domain.Venues.Exceptions;
using Aynera.Domain.Venues.Records;
using Aynera.Domain.Venues.Requests;
using Aynera.Domain.Venues.Responses;
using Aynera.Domain.Venues.Validators;
using Microsoft.Extensions.Logging;

namespace Aynera.Application.Features.Venues.Services.Implementations;

public sealed class VenueService : IVenueService
{
    private readonly IVenueRepository _venues;
    private readonly IVenueNotificationRepository _notifications;
    private readonly IEarlyAccessCityRepository _cities;
    private readonly IAuditWriter _audit;
    private readonly ILogger<VenueService> _logger;

    public VenueService(
        IVenueRepository venues,
        IVenueNotificationRepository notifications,
        IEarlyAccessCityRepository cities,
        IAuditWriter audit,
        ILogger<VenueService> logger)
    {
        _venues = venues;
        _notifications = notifications;
        _cities = cities;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VenueDto>> ListAsync(CancellationToken cancellationToken)
    {
        var venues = await _venues.ListAllAsync(cancellationToken);
        var cities = await _cities.ListAllAsync(cancellationToken);
        var cityNames = cities.ToDictionary(c => c.Id, c => c.Name);
        return venues.Select(v => ToDto(v, cityNames.GetValueOrDefault(v.CityId))).ToList();
    }

    public async Task<VenueDto> CreateAsync(CreateVenueRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateVenue {Name}", request.Name);

        if (!VenueValidation.TryParseType(request.Type, out var type))
        {
            throw new VenueException("validation_failed", "Venue type must be one of: Cafe, EventPlace.");
        }

        var city = await _cities.FindByIdAsync(request.CityId, cancellationToken)
            ?? throw new VenueException("venue_city_invalid", "The selected city does not exist.");

        var created = await _venues.AddAsync(
            new VenueRecord(
                Id: Guid.NewGuid(),
                Name: request.Name.Trim(),
                Type: type,
                CityId: request.CityId,
                Area: request.Area.Trim(),
                Address: request.Address.Trim(),
                PhotoUrls: NormalizeUrls(request.PhotoUrls),
                ContactName: request.ContactName.Trim(),
                ContactEmail: request.ContactEmail.Trim(),
                ContactPhoneE164: request.ContactPhoneE164.Trim(),
                Capacity: request.Capacity,
                Notes: NormalizeNotes(request.Notes),
                IsActive: request.IsActive,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: null),
            cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.VenueCreated,
                Outcome: AuditOutcomes.Success,
                Message: $"Venue '{created.Name}' created.",
                SubjectType: AuditSubjectTypes.Venue,
                SubjectId: created.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("name", null, created.Name),
                    ("type", null, created.Type.ToString()),
                    ("cityId", null, created.CityId.ToString("D")),
                    ("isActive", null, created.IsActive)
                ])),
            cancellationToken);

        return ToDto(created, city.Name);
    }

    public async Task<VenueDto> UpdateAsync(Guid id, UpdateVenueRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("UpdateVenue {VenueId}", id);

        var existing = await _venues.FindByIdAsync(id, cancellationToken)
            ?? throw new VenueException("venue_not_found", "Venue was not found.", 404);

        var type = existing.Type;
        if (request.Type is not null && !VenueValidation.TryParseType(request.Type, out type))
        {
            throw new VenueException("validation_failed", "Venue type must be one of: Cafe, EventPlace.");
        }

        var cityId = request.CityId ?? existing.CityId;
        var city = await _cities.FindByIdAsync(cityId, cancellationToken);
        if (request.CityId is not null && city is null)
        {
            throw new VenueException("venue_city_invalid", "The selected city does not exist.");
        }

        var updated = await _venues.UpdateAsync(
            existing with
            {
                Name = request.Name?.Trim() ?? existing.Name,
                Type = type,
                CityId = cityId,
                Area = request.Area?.Trim() ?? existing.Area,
                Address = request.Address?.Trim() ?? existing.Address,
                PhotoUrls = request.PhotoUrls is null ? existing.PhotoUrls : NormalizeUrls(request.PhotoUrls),
                ContactName = request.ContactName?.Trim() ?? existing.ContactName,
                ContactEmail = request.ContactEmail?.Trim() ?? existing.ContactEmail,
                ContactPhoneE164 = request.ContactPhoneE164?.Trim() ?? existing.ContactPhoneE164,
                Capacity = request.Capacity ?? existing.Capacity,
                Notes = request.Notes is null ? existing.Notes : NormalizeNotes(request.Notes),
                IsActive = request.IsActive ?? existing.IsActive,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.VenueUpdated,
                Outcome: AuditOutcomes.Success,
                Message: $"Venue '{updated.Name}' updated.",
                SubjectType: AuditSubjectTypes.Venue,
                SubjectId: updated.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("name", existing.Name, updated.Name),
                    ("type", existing.Type.ToString(), updated.Type.ToString()),
                    ("cityId", existing.CityId.ToString("D"), updated.CityId.ToString("D")),
                    ("isActive", existing.IsActive, updated.IsActive)
                ])),
            cancellationToken);

        return ToDto(updated, city?.Name ?? (await _cities.FindByIdAsync(updated.CityId, cancellationToken))?.Name);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SoftDeleteVenue {VenueId}", id);

        var existing = await _venues.FindByIdAsync(id, cancellationToken)
            ?? throw new VenueException("venue_not_found", "Venue was not found.", 404);

        await _venues.SoftDeleteAsync(id, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.VenueDeleted,
                Outcome: AuditOutcomes.Success,
                Message: $"Venue '{existing.Name}' soft-deleted.",
                SubjectType: AuditSubjectTypes.Venue,
                SubjectId: existing.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("name", existing.Name, null),
                    ("isActive", existing.IsActive, null),
                    ("isDeleted", false, true)
                ])),
            cancellationToken);
    }

    public async Task<IReadOnlyList<VenueNotificationDto>> QueueHeadsUpAsync(
        Guid venueId,
        SendVenueHeadsUpRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("QueueVenueHeadsUp {VenueId}", venueId);

        var venue = await _venues.FindByIdAsync(venueId, cancellationToken)
            ?? throw new VenueException("venue_not_found", "Venue was not found.", 404);

        if (!venue.IsActive)
        {
            throw new VenueException("venue_inactive", "Cannot notify an inactive venue.");
        }

        var now = DateTimeOffset.UtcNow;
        var note = NormalizeNotes(request.Note);
        var channels = new[] { VenueNotificationChannel.Email, VenueNotificationChannel.Sms };
        var records = channels
            .Select(channel => new VenueNotificationRecord(
                Id: Guid.NewGuid(),
                VenueId: venue.Id,
                Channel: channel,
                VisitOn: request.VisitOn,
                PartySize: request.PartySize,
                Note: note,
                CreatedByUserId: actorUserId,
                CreatedAtUtc: now,
                Attempts: 0,
                SentAtUtc: null,
                AbandonedAtUtc: null))
            .ToList();

        await _notifications.AddRangeAsync(records, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.VenueHeadsUpQueued,
                UserId: actorUserId,
                Outcome: AuditOutcomes.Success,
                Message: $"Heads-up queued for venue '{venue.Name}' ({request.PartySize} guests on {request.VisitOn:yyyy-MM-dd}).",
                SubjectType: AuditSubjectTypes.Venue,
                SubjectId: venue.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("visitOn", null, request.VisitOn.ToString("yyyy-MM-dd")),
                    ("partySize", null, request.PartySize),
                    ("channels", null, "Email,Sms")
                ])),
            cancellationToken);

        return records.Select(ToNotificationDto).ToList();
    }

    public async Task<IReadOnlyList<VenueNotificationDto>> ListNotificationsAsync(
        Guid venueId,
        CancellationToken cancellationToken)
    {
        var venue = await _venues.FindByIdAsync(venueId, cancellationToken)
            ?? throw new VenueException("venue_not_found", "Venue was not found.", 404);

        var records = await _notifications.ListByVenueAsync(venue.Id, cancellationToken);
        return records.Select(ToNotificationDto).ToList();
    }

    private static VenueNotificationDto ToNotificationDto(VenueNotificationRecord n) =>
        new(
            n.Id,
            n.VenueId,
            n.Channel.ToString(),
            n.VisitOn,
            n.PartySize,
            n.Note,
            Status: n.SentAtUtc is not null ? "Sent" : n.AbandonedAtUtc is not null ? "Abandoned" : "Pending",
            n.Attempts,
            n.CreatedAtUtc,
            n.SentAtUtc,
            n.AbandonedAtUtc);

    private static VenueDto ToDto(VenueRecord v, string? cityName) =>
        new(
            v.Id,
            v.Name,
            v.Type.ToString(),
            v.CityId,
            cityName,
            v.Area,
            v.Address,
            v.PhotoUrls,
            v.ContactName,
            v.ContactEmail,
            v.ContactPhoneE164,
            v.Capacity,
            v.Notes,
            v.IsActive,
            v.CreatedAtUtc,
            v.UpdatedAtUtc);

    private static IReadOnlyList<string> NormalizeUrls(IReadOnlyList<string>? urls) =>
        urls is null
            ? []
            : urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();

    private static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
