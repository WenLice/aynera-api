using AutoMapper;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class MemberAdmissionRepository : IMemberAdmissionRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<MemberAdmissionRepository> _logger;

    public MemberAdmissionRepository(
        AyneraDbContext db,
        IMapper mapper,
        ILogger<MemberAdmissionRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<MemberAdmissionRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await _db.MemberAdmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        return entity is null ? null : _mapper.Map<MemberAdmissionRecord>(entity);
    }

    public async Task<MemberAdmissionRecord> UpsertAsync(
        MemberAdmissionRecord admission,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberAdmission UpsertAsync {UserId} {State}", admission.UserId, admission.State);

        var entity = await _db.MemberAdmissions
            .FirstOrDefaultAsync(x => x.UserId == admission.UserId, cancellationToken);

        if (entity is null)
        {
            entity = new MemberAdmission { UserId = admission.UserId, CreatedAtUtc = admission.CreatedAtUtc };
            _db.MemberAdmissions.Add(entity);
        }

        entity.State = admission.State;
        entity.SubmittedAtUtc = admission.SubmittedAtUtc;
        entity.DecidedAtUtc = admission.DecidedAtUtc;
        entity.DecidedByUserId = admission.DecidedByUserId;
        entity.DecisionReason = admission.DecisionReason;
        entity.ReviewNote = admission.ReviewNote;
        entity.UpdatedAtUtc = admission.UpdatedAtUtc;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<MemberAdmissionRecord>(entity);
    }

    public async Task<(IReadOnlyList<MemberAdmissionRecord> Items, int TotalCount)> ListPageAsync(
        AdmissionState? state,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.MemberAdmissions.AsNoTracking();

        if (state is not null)
        {
            query = query.Where(x => x.State == state);
        }

        var total = await query.CountAsync(cancellationToken);

        var entities = await query
            // Oldest waiting first, so the review queue drains in the order members submitted.
            .OrderBy(x => x.SubmittedAtUtc ?? x.CreatedAtUtc)
            .ThenBy(x => x.UserId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (_mapper.Map<List<MemberAdmissionRecord>>(entities), total);
    }
}
