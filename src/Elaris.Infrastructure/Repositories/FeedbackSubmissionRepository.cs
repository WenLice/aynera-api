using AutoMapper;
using Elaris.Application.Features.Feedback.Repositories;
using Elaris.Domain.Feedback.Records;
using Elaris.Persistence;
using Elaris.Persistence.Entities;
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
