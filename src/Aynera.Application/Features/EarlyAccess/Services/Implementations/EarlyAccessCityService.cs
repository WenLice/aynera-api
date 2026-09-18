using AutoMapper;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.EarlyAccess.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Domain.EarlyAccess.Requests;
using Aynera.Domain.EarlyAccess.Responses;
using Aynera.Domain.EarlyAccess.Exceptions;
using Microsoft.Extensions.Logging;

namespace Aynera.Application.Features.EarlyAccess.Services.Implementations;

public sealed class EarlyAccessCityService : IEarlyAccessCityService
{
    private readonly IEarlyAccessCityRepository _cities;
    private readonly IMapper _mapper;
    private readonly IAuditWriter _audit;
    private readonly ILogger<EarlyAccessCityService> _logger;

    public EarlyAccessCityService(
        IEarlyAccessCityRepository cities,
        IMapper mapper,
        IAuditWriter audit,
        ILogger<EarlyAccessCityService> logger)
    {
        _cities = cities;
        _mapper = mapper;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EarlyAccessCityDto>> ListAsync(CancellationToken cancellationToken)
    {
        var cities = await _cities.ListAllAsync(cancellationToken);
        return _mapper.Map<List<EarlyAccessCityDto>>(cities);
    }

    public async Task<EarlyAccessCityDto> CreateAsync(
        CreateEarlyAccessCityRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateEarlyAccessCity {Name}", request.Name);
        var name = request.Name.Trim();
        if (await _cities.NameExistsAsync(name, excludingId: null, cancellationToken))
        {
            throw new EarlyAccessException(
                "early_access_city_exists",
                $"City '{name}' already exists.",
                statusCode: 409);
        }

        var created = await _cities.AddAsync(
            new EarlyAccessCityRecord(
                Id: Guid.NewGuid(),
                Name: name,
                Wave: request.Wave,
                SortOrder: request.SortOrder,
                IsActive: request.IsActive,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: null),
            cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.EarlyAccessCityCreated,
                Outcome: AuditOutcomes.Success,
                Message: $"Early access city '{created.Name}' created.",
                SubjectType: AuditSubjectTypes.EarlyAccessCity,
                SubjectId: created.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("name", null, created.Name),
                    ("wave", null, created.Wave),
                    ("sortOrder", null, created.SortOrder),
                    ("isActive", null, created.IsActive)
                ])),
            cancellationToken);

        return _mapper.Map<EarlyAccessCityDto>(created);
    }

    public async Task<EarlyAccessCityDto> UpdateAsync(
        Guid id,
        UpdateEarlyAccessCityRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("UpdateEarlyAccessCity {CityId}", id);
        var existing = await _cities.FindByIdAsync(id, cancellationToken)
            ?? throw new EarlyAccessException("early_access_city_not_found", "City was not found.", 404);

        var name = request.Name?.Trim() ?? existing.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new EarlyAccessException("validation_failed", "City name is required.");
        }

        if (await _cities.NameExistsAsync(name, excludingId: id, cancellationToken))
        {
            throw new EarlyAccessException(
                "early_access_city_exists",
                $"City '{name}' already exists.",
                statusCode: 409);
        }

        var isActive = request.IsActive ?? existing.IsActive;
        var updated = await _cities.UpdateAsync(
            existing with
            {
                Name = name,
                Wave = request.Wave ?? existing.Wave,
                SortOrder = request.SortOrder ?? existing.SortOrder,
                IsActive = isActive,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.EarlyAccessCityUpdated,
                Outcome: AuditOutcomes.Success,
                Message: $"Early access city '{updated.Name}' updated.",
                SubjectType: AuditSubjectTypes.EarlyAccessCity,
                SubjectId: updated.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("name", existing.Name, updated.Name),
                    ("wave", existing.Wave, updated.Wave),
                    ("sortOrder", existing.SortOrder, updated.SortOrder),
                    ("isActive", existing.IsActive, updated.IsActive)
                ])),
            cancellationToken);

        return _mapper.Map<EarlyAccessCityDto>(updated);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SoftDeleteEarlyAccessCity {CityId}", id);
        var existing = await _cities.FindByIdAsync(id, cancellationToken)
            ?? throw new EarlyAccessException("early_access_city_not_found", "City was not found.", 404);

        await _cities.SoftDeleteAsync(id, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.EarlyAccessCityDeleted,
                Outcome: AuditOutcomes.Success,
                Message: $"Early access city '{existing.Name}' soft-deleted.",
                SubjectType: AuditSubjectTypes.EarlyAccessCity,
                SubjectId: existing.Id.ToString("D"),
                Changes: AuditChanges.Create(
                [
                    ("name", existing.Name, null),
                    ("isActive", existing.IsActive, null),
                    ("isDeleted", false, true)
                ])),
            cancellationToken);
    }
}
