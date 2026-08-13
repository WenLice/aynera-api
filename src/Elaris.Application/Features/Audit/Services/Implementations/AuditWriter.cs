using Elaris.Application.Features.Audit.Repositories;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Domain.Audit.Records;
using Elaris.Domain.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Elaris.Application.Features.Audit.Services.Implementations;

public sealed class AuditWriter : IAuditWriter
{
    private readonly IAuditEventRepository _events;
    private readonly ICorrelationId _correlationId;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(
        IAuditEventRepository events,
        ICorrelationId correlationId,
        ICurrentUser currentUser,
        ILogger<AuditWriter> logger)
    {
        _events = events;
        _correlationId = correlationId;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEventWriteModel model, CancellationToken cancellationToken)
    {
        try
        {
            var log = new AuditLogRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                model.Level,
                model.Message,
                model.Category ?? model.Action,
                model.Client,
                _correlationId.Value,
                model.UserId ?? _currentUser.UserId,
                model.ClientIp,
                PropertiesJson: null);

            await _events.AddWithLogAsync(
                log,
                model.Action,
                model.Outcome,
                model.SubjectUserId,
                model.SubjectType,
                model.SubjectId,
                model.Audience,
                model.Changes,
                model.Metadata,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write AuditEvent {Action}.", model.Action);
        }
    }
}
