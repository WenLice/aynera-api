using AutoMapper;
using Elaris.Application.Features.Feedback.Repositories;
using Elaris.Domain.Feedback.Records;
using Elaris.Persistence;
using Elaris.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elaris.Infrastructure.Repositories;

public sealed class FeedbackSubmissionRepository : IFeedbackSubmissionRepository
{
    private readonly ElarisDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<FeedbackSubmissionRepository> _logger;

    public FeedbackSubmissionRepository(ElarisDbContext db, IMapper mapper, ILogger<FeedbackSubmissionRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<FeedbackSubmissionRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.FeedbackSubmissions.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (_mapper.Map<List<FeedbackSubmissionRecord>>(entities), totalCount);
    }

    public async Task<FeedbackSubmissionRecord> AddAsync(
        FeedbackSubmissionRecord feedback,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FeedbackSubmission AddAsync");
        var entity = _mapper.Map<FeedbackSubmission>(feedback);
        _db.FeedbackSubmissions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<FeedbackSubmissionRecord>(entity);
    }
}
