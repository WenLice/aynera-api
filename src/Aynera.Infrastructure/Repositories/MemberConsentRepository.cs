using AutoMapper;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Domain.Admissions.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class MemberConsentRepository : IMemberConsentRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<MemberConsentRepository> _logger;

    public MemberConsentRepository(
        AyneraDbContext db,
        IMapper mapper,
        ILogger<MemberConsentRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MemberConsentRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entities = await _db.MemberConsents
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.PolicyKind)
            .ThenBy(x => x.Version)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<MemberConsentRecord>>(entities);
    }

    public async Task<MemberConsentRecord> AcceptAsync(
        MemberConsentRecord consent,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "MemberConsent AcceptAsync {UserId} {PolicyKind} {Version}",
            consent.UserId,
            consent.PolicyKind,
            consent.Version);

        // Acceptance is append-only but idempotent: the same document version accepted twice keeps
        // the original timestamp rather than resetting the member's acceptance date.
        var existing = await _db.MemberConsents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == consent.UserId
                    && x.PolicyKind == consent.PolicyKind
                    && x.Version == consent.Version,
                cancellationToken);

        if (existing is not null)
        {
            return _mapper.Map<MemberConsentRecord>(existing);
        }

        var entity = new MemberConsent
        {
            Id = consent.Id,
            UserId = consent.UserId,
            PolicyKind = consent.PolicyKind,
            Version = consent.Version,
            AcceptedAtUtc = consent.AcceptedAtUtc
        };

        _db.MemberConsents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<MemberConsentRecord>(entity);
    }
}
