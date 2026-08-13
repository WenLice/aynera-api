using Elaris.Application.Features.Audit.Repositories;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Domain.Audit.Records;
using Elaris.Domain.Audit.Statics;
using Elaris.Domain.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Elaris.Application.Features.Audit.Services.Implementations;

public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly IAuditLogRepository _logs;
    private readonly ICorrelationId _correlationId;
    private readonly ILogger<AuditLogWriter> _logger;

    public AuditLogWriter(
        IAuditLogRepository logs,
        ICorrelationId correlationId,
        ILogger<AuditLogWriter> logger)
    {
        _logs = logs;
        _correlationId = correlationId;
        _logger = logger;
    }

    public async Task WriteAsync(
        string level,
        string message,
        string? category,
        string client,
        Guid? userId,
        string? clientIp,
        object? properties,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logs.AddAsync(
                new AuditLogRecord(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    level,
                    message,
                    category,
                    client,
                    _correlationId.Value,
                    userId,
                    clientIp,
                    AuditChanges.MetadataToJson(properties)),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write AuditLog.");
        }
    }
}
