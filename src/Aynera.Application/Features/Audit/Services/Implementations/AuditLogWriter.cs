using Aynera.Application.Features.Audit.Repositories;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Aynera.Application.Features.Audit.Services.Implementations;

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
